/* GPL-3.0. Bounded decoded-frame playout. FFmpeg frames remain reference
 * counted, including GPU surfaces; no image copies or frame generation.
 */
#define WIN32_LEAN_AND_MEAN
#include <windows.h>
#include <avrt.h>
#include <stdint.h>
#include <stddef.h>
#include "presentation-layout.h"
#ifndef CREATE_WAITABLE_TIMER_HIGH_RESOLUTION
#define CREATE_WAITABLE_TIMER_HIGH_RESOLUTION 0x00000002
#endif

/* Stable public prefix of the pinned FFmpeg 8 / libavutil 60 AVFrame ABI. */
typedef struct AMFrame { unsigned char *data[8]; int linesize[8]; unsigned char **extended_data; int width,height,nb_samples,format; } AMFrame;
_Static_assert(offsetof(AMFrame,width)==104,"AVFrame x64 prefix mismatch");
typedef void (*PushFn)(void *,const AMFrame *,int64_t);
typedef void (*ControlFn)(void *);
typedef AMFrame *(*CloneFn)(const AMFrame *);
typedef void (*FreeFn)(AMFrame **);
typedef AMFrame *(*AllocFn)(void);
typedef int (*BufferFn)(AMFrame *,int);
typedef struct Sample { void *owner; AMFrame *frame; int64_t origin; LONGLONG arrival; } Sample;

static INIT_ONCE video_once=INIT_ONCE_STATIC_INIT;
static SRWLOCK video_lock=SRWLOCK_INIT;
static CONDITION_VARIABLE video_condition=CONDITION_VARIABLE_INIT;
static PushFn original_push;
static ControlFn original_clear,original_stop,original_destroy;
static CloneFn clone_frame; static FreeFn free_frame; static AllocFn alloc_frame; static BufferFn frame_buffer;
static Sample queue[3]; static int count,resetting,stopped,quit,busy,clock_armed;
static int width,height,format,smooth;
static int smoke_enabled;
static volatile LONG smoke_done;
static void *owner;
static HANDLE worker,wake_event,playout_timer;
static LONGLONG frequency,period,next_due;
static volatile LONG clones,frees,delivered,dropped,maximum_depth;
static volatile LONG sink_count;
static unsigned char sink_values[256]; static LONGLONG sink_times[256]; static int sink_widths[256];
static void native_smoke(void *renderer);

static LONGLONG clock_now(void) { LARGE_INTEGER value;QueryPerformanceCounter(&value);return value.QuadPart; }
static HANDLE new_timer(void) {
    HANDLE timer=CreateWaitableTimerExW(NULL,NULL,CREATE_WAITABLE_TIMER_HIGH_RESOLUTION,TIMER_ALL_ACCESS);
    return timer?timer:CreateWaitableTimerW(NULL,FALSE,NULL);
}
static void wait_deadline(HANDLE timer,LONGLONG deadline) {
    LONGLONG delta=deadline-clock_now();if(delta<=0)return;
    LARGE_INTEGER due;due.QuadPart=-(delta*10000000/frequency);if(due.QuadPart==0)due.QuadPart=-1;
    SetWaitableTimer(timer,&due,0,NULL,NULL,FALSE);WaitForSingleObject(timer,INFINITE);
}
static void release_frame(AMFrame **frame) { if(*frame){free_frame(frame);InterlockedIncrement(&frees);} }
static void flush_locked(void) { for(int i=0;i<count;i++)release_frame(&queue[i].frame);count=0;clock_armed=0;width=height=0; }
static BOOL CALLBACK initialize_video(PINIT_ONCE unused,void *parameter,void **context) {
    (void)unused;(void)parameter;(void)context;
    HMODULE av=GetModuleHandleW(L"avutil-60.dll");if(!av)av=LoadLibraryW(L"avutil-60.dll");
    clone_frame=(CloneFn)(void *)GetProcAddress(av,"av_frame_clone");free_frame=(FreeFn)(void *)GetProcAddress(av,"av_frame_free");
    alloc_frame=(AllocFn)(void *)GetProcAddress(av,"av_frame_alloc");frame_buffer=(BufferFn)(void *)GetProcAddress(av,"av_frame_get_buffer");
    unsigned char *base=(unsigned char *)GetModuleHandleW(NULL);
    original_push=(PushFn)(base+AM_ORIGINAL_PUSH_RVA);original_clear=(ControlFn)(base+AM_ORIGINAL_CLEAR_RVA);
    original_stop=(ControlFn)(base+AM_ORIGINAL_STOP_RVA);original_destroy=(ControlFn)(base+AM_ORIGINAL_DESTROY_RVA);
    LARGE_INTEGER ticks;QueryPerformanceFrequency(&ticks);frequency=ticks.QuadPart;
    char mode[32]={0},fps[16]={0};GetEnvironmentVariableA("AIRMIRROR_VIDEO_MODE",mode,sizeof(mode));GetEnvironmentVariableA("AIRMIRROR_VIDEO_FPS",fps,sizeof(fps));
    smooth=mode[0]=='s';period=frequency/(fps[0]=='3'?30:60);
    char smoke[8]={0};GetEnvironmentVariableA("AIRMIRROR_VIDEO_SMOKE",smoke,sizeof(smoke));smoke_enabled=smoke[0]=='1';
    return clone_frame&&free_frame&&alloc_frame&&frame_buffer;
}
static int initialized(void) { return InitOnceExecuteOnce(&video_once,initialize_video,NULL,NULL)!=0; }

static DWORD WINAPI playout(void *parameter) {
    (void)parameter;DWORD task_index=0;HANDLE task=AvSetMmThreadCharacteristicsW(L"Playback",&task_index);
    for(;;){
        AcquireSRWLockExclusive(&video_lock);
        if(quit){ReleaseSRWLockExclusive(&video_lock);break;}
        if(resetting||count==0){clock_armed=0;ReleaseSRWLockExclusive(&video_lock);WaitForSingleObject(wake_event,INFINITE);continue;}
        if(!clock_armed){next_due=queue[0].arrival+2*period;clock_armed=1;}
        LONGLONG now=clock_now();
        if(now<next_due){
            LONGLONG delta=next_due-now;ReleaseSRWLockExclusive(&video_lock);
            LARGE_INTEGER due;due.QuadPart=-(delta*10000000/frequency);if(!due.QuadPart)due.QuadPart=-1;
            SetWaitableTimer(playout_timer,&due,0,NULL,NULL,FALSE);
            HANDLE events[2]={wake_event,playout_timer};WaitForMultipleObjects(2,events,FALSE,INFINITE);continue;
        }
        Sample sample=queue[0];for(int i=1;i<count;i++)queue[i-1]=queue[i];count--;busy=1;
        next_due+=period;if(next_due<now+period/2)next_due=now+period;
        ReleaseSRWLockExclusive(&video_lock);
        original_push(sample.owner,sample.frame,sample.origin);InterlockedIncrement(&delivered);release_frame(&sample.frame);
        AcquireSRWLockExclusive(&video_lock);busy=0;WakeAllConditionVariable(&video_condition);ReleaseSRWLockExclusive(&video_lock);
    }
    if(task)AvRevertMmThreadCharacteristics(task);return 0;
}
static int start_worker_locked(void) {
    if(worker)return 1;
    wake_event=CreateEventW(NULL,FALSE,FALSE,NULL);playout_timer=new_timer();quit=0;
    if(wake_event&&playout_timer)worker=CreateThread(NULL,0,playout,NULL,0,NULL);
    if(worker)return 1;
    if(wake_event)CloseHandle(wake_event);if(playout_timer)CloseHandle(playout_timer);wake_event=playout_timer=NULL;return 0;
}

void AM_PushFrame(void *renderer,const AMFrame *frame,int64_t origin) {
    if(!frame||!initialized())return;
    if(!smooth){original_push(renderer,frame,origin);return;}
    AMFrame *copy=clone_frame(frame);if(!copy)return;InterlockedIncrement(&clones);
    AcquireSRWLockExclusive(&video_lock);
    if(resetting||(stopped&&owner==renderer)){ReleaseSRWLockExclusive(&video_lock);release_frame(&copy);return;}
    if(owner!=renderer){flush_locked();while(busy)SleepConditionVariableSRW(&video_condition,&video_lock,INFINITE,0);owner=renderer;stopped=0;}
    if(!start_worker_locked()){ReleaseSRWLockExclusive(&video_lock);release_frame(&copy);original_push(renderer,frame,origin);return;}
    if(width!=frame->width||height!=frame->height||format!=frame->format){flush_locked();width=frame->width;height=frame->height;format=frame->format;}
    if(count==3){release_frame(&queue[0].frame);queue[0]=queue[1];queue[1]=queue[2];count--;InterlockedIncrement(&dropped);}
    queue[count++]=(Sample){renderer,copy,origin,clock_now()};
    if(count>maximum_depth)maximum_depth=count;
    SetEvent(wake_event);ReleaseSRWLockExclusive(&video_lock);
}
void AM_ClearFrames(void *renderer) {
    if(!initialized())return;
    if(smoke_enabled&&InterlockedCompareExchange(&smoke_done,1,0)==0)native_smoke(renderer);
    AcquireSRWLockExclusive(&video_lock);resetting=1;flush_locked();if(wake_event)SetEvent(wake_event);
    while(busy)SleepConditionVariableSRW(&video_condition,&video_lock,INFINITE,0);
    ReleaseSRWLockExclusive(&video_lock);original_clear(renderer);
    AcquireSRWLockExclusive(&video_lock);resetting=0;ReleaseSRWLockExclusive(&video_lock);
}
static void stop_playout(void *renderer) {
    AcquireSRWLockExclusive(&video_lock);stopped=1;owner=renderer;resetting=1;quit=1;flush_locked();
    HANDLE thread=worker;if(wake_event)SetEvent(wake_event);ReleaseSRWLockExclusive(&video_lock);
    if(thread)WaitForSingleObject(thread,INFINITE);
    AcquireSRWLockExclusive(&video_lock);
    if(worker)CloseHandle(worker);if(wake_event)CloseHandle(wake_event);if(playout_timer)CloseHandle(playout_timer);
    worker=wake_event=playout_timer=NULL;busy=0;resetting=0;ReleaseSRWLockExclusive(&video_lock);
}
void AM_StopFrames(void *renderer) { if(!initialized())return;stop_playout(renderer);original_stop(renderer); }
void AM_DestroyFrames(void *renderer) { if(!initialized())return;stop_playout(renderer);original_destroy(renderer); }

/* Only the explicit --self-test child receives this environment flag. This
 * exercises the real relocated C++ push path and the real D3D/SDL renderer.
 */
static void native_smoke(void *renderer) {
    HANDLE timer=new_timer();if(!timer)return;LONGLONG begin=clock_now();int good=1;
    for(int i=0;i<6;i++){
        AMFrame *frame=alloc_frame();if(!frame){good=0;break;}frame->width=128;frame->height=72;frame->format=0;
        if(frame_buffer(frame,32)<0){free_frame(&frame);good=0;break;}
        FillMemory(frame->data[0],frame->linesize[0]*72,(BYTE)(32+i*20));
        FillMemory(frame->data[1],frame->linesize[1]*36,128);FillMemory(frame->data[2],frame->linesize[2]*36,128);
        AM_PushFrame(renderer,frame,0);free_frame(&frame);wait_deadline(timer,begin+(i+1)*period);
    }
    LONGLONG limit=clock_now()+frequency;
    for(;;){AcquireSRWLockExclusive(&video_lock);int pending=count||busy;ReleaseSRWLockExclusive(&video_lock);if(!pending||clock_now()>limit)break;Sleep(1);}
    Sleep(50);CloseHandle(timer);
    if(good){const char message[]="AirVideo: native renderer smoke complete\n";DWORD written;WriteFile(GetStdHandle(STD_ERROR_HANDLE),message,sizeof(message)-1,&written,NULL);}
}

static void test_sink(void *renderer,const AMFrame *frame,int64_t origin) {
    (void)renderer;(void)origin;LONG index=InterlockedCompareExchange(&sink_count,0,0);
    if(index<256){sink_values[index]=frame->data[0][0];sink_times[index]=clock_now();sink_widths[index]=frame->width;}
    InterlockedIncrement(&sink_count);
}
static void test_control(void *renderer) { (void)renderer; }
static void test_reset(int mode,int fps) {
    stop_playout((void *)1);smooth=mode;period=frequency/fps;stopped=resetting=quit=clock_armed=0;owner=(void *)1;sink_count=0;
}
static int test_push(unsigned char marker,int w,int h) {
    AMFrame *frame=alloc_frame();if(!frame)return 0;frame->width=w;frame->height=h;frame->format=0;
    if(frame_buffer(frame,32)<0){free_frame(&frame);return 0;}frame->data[0][0]=marker;
    AM_PushFrame((void *)1,frame,0);free_frame(&frame);return 1;
}
static int await_count(int wanted,DWORD timeout) {
    ULONGLONG end=GetTickCount64()+timeout;while(sink_count<wanted&&GetTickCount64()<end)Sleep(1);return sink_count==wanted;
}
static int test_cadence(int fps,int frames) {
    test_reset(1,fps);HANDLE timer=new_timer();if(!timer)return 0;
    LONGLONG begin=clock_now(),arrival=begin;int offsets[4]={4,29,8,25};int ok=1;
    for(int i=0;i<frames;i++){
        if(i){arrival+=(LONGLONG)offsets[(i-1)%4]*frequency/1000*(fps==30?2:1);wait_deadline(timer,arrival);}
        if(!test_push((unsigned char)i,128,72)){ok=0;break;}
    }
    if(!await_count(frames,1000))ok=0;
    for(int i=0;i<frames&&i<sink_count;i++)if(sink_values[i]!=i||sink_widths[i]!=128)ok=0;
    for(int i=1;i<sink_count&&i<256;i++)if(sink_times[i]-sink_times[i-1]<period*2/5)ok=0;
    if(sink_count&&sink_times[0]-begin<period*3/2)ok=0;
    CloseHandle(timer);AM_StopFrames((void *)1);return ok;
}
DWORD WINAPI AirVideoSelfTest(void) {
    if(!initialized())return 0;
    PushFn saved_push=original_push;ControlFn saved_clear=original_clear,saved_stop=original_stop,saved_destroy=original_destroy;
    original_push=test_sink;original_clear=original_stop=original_destroy=test_control;
    DWORD result=0;LONG initial_clones=clones,initial_frees=frees;
    test_reset(0,60);int ok=1;for(int i=0;i<4;i++)if(!test_push((unsigned char)i,128,72)||sink_count!=i+1)ok=0;if(ok)result|=1;
    if(test_cadence(60,16))result|=2;if(test_cadence(30,8))result|=4;
    test_reset(1,60);test_push(1,128,72);test_push(2,128,72);AM_ClearFrames((void *)1);
    if(test_push(9,72,128)&&await_count(1,500)&&sink_values[0]==9&&sink_widths[0]==72)result|=8;
    AM_StopFrames((void *)1);LONG before=sink_count;Sleep(50);if(sink_count==before&&clones-initial_clones==frees-initial_frees)result|=16;
    test_reset(1,60);for(int i=0;i<100;i++)test_push((unsigned char)i,128,72);AM_StopFrames((void *)1);
    if(maximum_depth<=3&&dropped>0&&clones-initial_clones==frees-initial_frees)result|=32;
    original_push=saved_push;original_clear=saved_clear;original_stop=saved_stop;original_destroy=saved_destroy;
    return result;
}
