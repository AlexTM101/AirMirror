/* Deterministic OS-boundary fault injection; exercises the real am_recv wrapper
 * without creating network connections or depending on MMCSS availability. */
#define WIN32_LEAN_AND_MEAN
#include <winsock2.h>
#include <windows.h>
#include <avrt.h>
#include <stdio.h>

static int receive_result, receive_error, starts, reverts;
static int WSAAPI test_recv(SOCKET socket, char *buffer, int length, int flags) {
    (void)socket; (void)buffer; (void)length; (void)flags;
    WSASetLastError(receive_error); return receive_result;
}
static HANDLE WINAPI test_start(LPCWSTR name, LPDWORD index) {
    (void)name; (void)index; starts++; return (HANDLE)(ULONG_PTR)1;
}
static BOOL WINAPI test_revert(HANDLE task) { (void)task; reverts++; return TRUE; }
#define recv test_recv
#define AvSetMmThreadCharacteristicsW test_start
#define AvRevertMmThreadCharacteristics test_revert
#include "../../native/airnet.c"
#undef recv

int main(void) {
    const int transient_errors[] = {WSAETIMEDOUT, WSAEWOULDBLOCK};
    int failures = 0;
    quiet_notes = 1;
    for (unsigned i=0; i<sizeof(transient_errors)/sizeof(transient_errors[0]); i++) {
        accepted_on_thread = (SOCKET)42; playback_attempted = 0;
        playback_task = NULL; playback_socket = INVALID_SOCKET; starts = reverts = 0;
        receive_result = 1; receive_error = 0;
        char byte;
        if (am_recv((SOCKET)42,&byte,1,0) != 1 || starts != 1) return 2;
        receive_result = SOCKET_ERROR; receive_error = transient_errors[i];
        int result = am_recv((SOCKET)42,&byte,1,0);
        if (result != SOCKET_ERROR || WSAGetLastError() != transient_errors[i] || !playback_task || reverts != 0) {
            printf("FAIL receive error %d ended an active playback scope\n",transient_errors[i]); failures++;
        }
        receive_result = 1; receive_error = 0;
        if (am_recv((SOCKET)42,&byte,1,0) != 1 || !playback_task || starts != 1) {
            puts("FAIL receive after timeout lost playback scheduling"); failures++;
        }
        receive_result = 0;
        am_recv((SOCKET)42,&byte,1,0);
        if (playback_task || reverts != 1) { puts("FAIL EOF did not release exactly once"); failures++; }
    }
    accepted_on_thread = (SOCKET)42; playback_attempted = 0;
    receive_result = 1; receive_error = 0;
    char byte; am_recv((SOCKET)42,&byte,1,0);
    receive_result = SOCKET_ERROR; receive_error = WSAECONNRESET;
    am_recv((SOCKET)42,&byte,1,0);
    if (playback_task || WSAGetLastError() != WSAECONNRESET) { puts("FAIL terminal error retained playback scope or changed error"); failures++; }
    if (!failures) puts("PASS recoverable receive timeouts, resumed receive, EOF and terminal error");
    return failures ? 1 : 0;
}
