/* AirMirror private Winsock adapter, GPL-3.0. No system-wide changes.
 * All unmodified Winsock exports are direct PE forwarders to WS2_32.
 * The engine's inline mirror accept/read thread gets a scoped Playback task.
 */
#define WIN32_LEAN_AND_MEAN
#ifndef _WIN32_WINNT
#define _WIN32_WINNT 0x0a00
#endif
#include <winsock2.h>
#include <windows.h>
#include <mstcpip.h>
#include <avrt.h>

#ifndef SIO_TCP_SET_ACK_FREQUENCY
#define SIO_TCP_SET_ACK_FREQUENCY _WSAIOW(IOC_VENDOR,23)
#endif

static _Thread_local SOCKET accepted_on_thread = INVALID_SOCKET;
static _Thread_local SOCKET playback_socket = INVALID_SOCKET;
static _Thread_local HANDLE playback_task;
static _Thread_local int playback_attempted;
static _Thread_local int quiet_notes;
static volatile LONG accepted_count, ack_count, ack_fallback, playback_count, reverted_count;

static void note(const char *message, DWORD length) {
    if (quiet_notes) return;
    DWORD written;
    HANDLE output = GetStdHandle(STD_ERROR_HANDLE);
    if (output && output != INVALID_HANDLE_VALUE) WriteFile(output,message,length,&written,NULL);
}
#define NOTE(text) note(text "\n", (DWORD)(sizeof(text "\n")-1))

static void release_playback(SOCKET socket) {
    if (playback_task && playback_socket == socket) {
        BOOL reverted = AvRevertMmThreadCharacteristics(playback_task);
        playback_task = NULL; playback_socket = INVALID_SOCKET;
        if (reverted) InterlockedIncrement(&reverted_count);
    }
}

SOCKET WSAAPI am_accept(SOCKET listener, struct sockaddr *address, int *length) {
    SOCKET client = accept(listener,address,length);
    if (client == INVALID_SOCKET) return client;
    int previous_error = WSAGetLastError();
    int enabled = 1;
    DWORD frequency = 1, returned = 0;
    InterlockedIncrement(&accepted_count);
    setsockopt(client,IPPROTO_TCP,TCP_NODELAY,(const char *)&enabled,sizeof(enabled));
    if (WSAIoctl(client,SIO_TCP_SET_ACK_FREQUENCY,&frequency,sizeof(frequency),NULL,0,&returned,NULL,NULL) == 0) {
        InterlockedIncrement(&ack_count);
        NOTE("AirNet: immediate TCP acknowledgements enabled");
    } else {
        InterlockedIncrement(&ack_fallback);
        NOTE("AirNet: TCP acknowledgement policy unavailable; using system default");
    }
    if (playback_task) release_playback(playback_socket);
    accepted_on_thread = client;
    playback_attempted = 0;
    WSASetLastError(previous_error);
    return client;
}

int WSAAPI am_recv(SOCKET socket, char *buffer, int length, int flags) {
    /* Control connections are accepted on a separate listener thread and
     * served by workers. Their TLS never matches this test. MirrorListener
     * accepts and reads inline, so only its receive/decode thread is tagged.
     */
    if (socket != INVALID_SOCKET && socket == accepted_on_thread && !playback_attempted) {
        playback_attempted = 1;
        int previous_error = WSAGetLastError();
        DWORD index = 0;
        playback_task = AvSetMmThreadCharacteristicsW(L"Playback",&index);
        if (playback_task) {
            playback_socket = socket;
            InterlockedIncrement(&playback_count);
            NOTE("AirNet: scoped multimedia scheduling enabled");
        }
        WSASetLastError(previous_error);
    }
    int result = recv(socket,buffer,length,flags);
    int previous_error = WSAGetLastError();
    if (result <= 0) release_playback(socket);
    WSASetLastError(previous_error);
    return result;
}

int WSAAPI am_closesocket(SOCKET socket) {
    int previous_error = WSAGetLastError();
    release_playback(socket);
    if (accepted_on_thread == socket) { accepted_on_thread = INVALID_SOCKET; playback_attempted = 0; }
    WSASetLastError(previous_error);
    return closesocket(socket);
}

typedef struct AirNetStats {
    DWORD version, accepted, immediate_ack, fallback_ack, playback_started, playback_reverted;
} AirNetStats;

DWORD WINAPI am_stats(AirNetStats *output, DWORD size) {
    if (!output || size < sizeof(*output)) return 0;
    output->version = 1;
    output->accepted = InterlockedCompareExchange(&accepted_count,0,0);
    output->immediate_ack = InterlockedCompareExchange(&ack_count,0,0);
    output->fallback_ack = InterlockedCompareExchange(&ack_fallback,0,0);
    output->playback_started = InterlockedCompareExchange(&playback_count,0,0);
    output->playback_reverted = InterlockedCompareExchange(&reverted_count,0,0);
    return 1;
}

DWORD WINAPI am_self_test(DWORD iterations) {
    WSADATA wsa;
    if (WSAStartup(MAKEWORD(2,2),&wsa) != 0) return 0;
    int saved_quiet = quiet_notes; quiet_notes = 1;
    SOCKET listener = INVALID_SOCKET, sender = INVALID_SOCKET, receiver = INVALID_SOCKET;
    DWORD result = 0;
    LONG initial_playback = playback_count, initial_reverts = reverted_count, initial_ack = ack_count;
    struct sockaddr_in address;
    char sent[1024], received[1024];
    int address_length = sizeof(address), enabled, option_length = sizeof(enabled);
    DWORD timeout = 2500;
    if (iterations == 0 || iterations > 256) iterations = 32;
    listener = socket(AF_INET,SOCK_STREAM,IPPROTO_TCP);
    if (listener == INVALID_SOCKET) goto done;
    ZeroMemory(&address,sizeof(address));
    address.sin_family = AF_INET; address.sin_addr.s_addr = htonl(INADDR_LOOPBACK);
    if (bind(listener,(struct sockaddr *)&address,sizeof(address)) != 0 || listen(listener,1) != 0) goto done;
    if (getsockname(listener,(struct sockaddr *)&address,&address_length) != 0) goto done;
    for (DWORD cycle=0; cycle<iterations; cycle++) {
        sender = socket(AF_INET,SOCK_STREAM,IPPROTO_TCP);
        if (sender == INVALID_SOCKET) goto done;
        setsockopt(sender,SOL_SOCKET,SO_SNDTIMEO,(const char *)&timeout,sizeof(timeout));
        if (connect(sender,(struct sockaddr *)&address,sizeof(address)) != 0) goto done;
        receiver = am_accept(listener,NULL,NULL);
        if (receiver == INVALID_SOCKET) goto done;
        setsockopt(receiver,SOL_SOCKET,SO_RCVTIMEO,(const char *)&timeout,sizeof(timeout));
        enabled = 0; option_length = sizeof(enabled);
        if (getsockopt(receiver,IPPROTO_TCP,TCP_NODELAY,(char *)&enabled,&option_length) != 0 || enabled != 1) goto done;
        for (int block=0; block<16; block++) {
            int size = 129 + (block*59)%895;
            for (int i=0; i<size; i++) sent[i]=(char)((i*37+block+cycle)&255);
            int written=0;
            while (written<size) { int n=send(sender,sent+written,size-written,0); if (n<=0) goto done; written+=n; }
            int count=0;
            while (count<size) {
                int fragment = 1+(count*13+block)%97;
                if (fragment > size-count) fragment=size-count;
                int n=am_recv(receiver,received+count,fragment,0); if (n<=0) goto done; count+=n;
            }
            for (int i=0; i<size; i++) if (received[i]!=sent[i]) goto done;
        }
        shutdown(sender,SD_SEND);
        if (am_recv(receiver,received,1,0) != 0) goto done;
        am_closesocket(receiver); receiver=INVALID_SOCKET;
        closesocket(sender); sender=INVALID_SOCKET;
    }
    result |= 1; /* byte-exact fragmented payloads, close/EOF, repeated cycles */
    if (ack_count-initial_ack == (LONG)iterations) result |= 2;
    if (playback_count-initial_playback == (LONG)iterations) result |= 4;
    if (playback_count-initial_playback == reverted_count-initial_reverts) result |= 8;
    WSASetLastError(0);
    if (am_accept(INVALID_SOCKET,NULL,NULL) == INVALID_SOCKET && WSAGetLastError() == WSAENOTSOCK) result |= 16;
done:
    if (receiver != INVALID_SOCKET) am_closesocket(receiver);
    if (sender != INVALID_SOCKET) closesocket(sender);
    if (listener != INVALID_SOCKET) closesocket(listener);
    WSACleanup();
    quiet_notes = saved_quiet;
    return result;
}
