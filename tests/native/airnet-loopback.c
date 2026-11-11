/* Own-process loopback only: reproduces MirrorListener's timeout/retry sequence
 * against real Winsock and, when available, the real MMCSS service. */
#include "../../native/airnet.c"
#include <stdio.h>

int main(void) {
    WSADATA wsa;
    if (WSAStartup(MAKEWORD(2,2),&wsa)) return 2;
    int failed=0, scheduling_available=0;
    SOCKET listener=INVALID_SOCKET, sender=INVALID_SOCKET, receiver=INVALID_SOCKET;
    struct sockaddr_in address; ZeroMemory(&address,sizeof(address));
    address.sin_family=AF_INET;address.sin_addr.s_addr=htonl(INADDR_LOOPBACK);
    int address_length=sizeof(address);DWORD timeout=200;
    quiet_notes=1;
#define CHECK(value,message) do { if(!(value)){printf("FAIL %s (error %d)\n",message,WSAGetLastError());failed=1;goto done;} } while(0)
    listener=socket(AF_INET,SOCK_STREAM,IPPROTO_TCP);
    CHECK(listener!=INVALID_SOCKET,"create loopback listener");
    CHECK(bind(listener,(struct sockaddr *)&address,sizeof(address))==0,"bind loopback listener");
    CHECK(listen(listener,1)==0,"listen on loopback");
    CHECK(getsockname(listener,(struct sockaddr *)&address,&address_length)==0,"resolve private port");
    sender=socket(AF_INET,SOCK_STREAM,IPPROTO_TCP);
    CHECK(sender!=INVALID_SOCKET,"create loopback sender");
    CHECK(connect(sender,(struct sockaddr *)&address,sizeof(address))==0,"connect loopback sender");
    receiver=am_accept(listener,NULL,NULL);
    CHECK(receiver!=INVALID_SOCKET,"accept loopback sender");
    CHECK(setsockopt(receiver,SOL_SOCKET,SO_RCVTIMEO,(const char *)&timeout,sizeof(timeout))==0,"set production receive timeout");
    CHECK(send(sender,"a",1,0)==1,"send first byte");
    char byte=0;
    CHECK(am_recv(receiver,&byte,1,0)==1&&byte=='a',"receive first byte");
    scheduling_available=playback_task!=NULL;
    LONG initial_starts=playback_count,initial_reverts=reverted_count;
    CHECK(am_recv(receiver,&byte,1,0)==SOCKET_ERROR&&WSAGetLastError()==WSAETIMEDOUT,"observe real receive timeout");
    CHECK(!scheduling_available||(playback_task!=NULL&&reverted_count==initial_reverts),"keep playback scope through timeout");
    CHECK(send(sender,"b",1,0)==1,"resume sender after timeout");
    CHECK(am_recv(receiver,&byte,1,0)==1&&byte=='b',"resume receive after timeout");
    CHECK(!scheduling_available||(playback_task!=NULL&&playback_count==initial_starts),"retain one playback scope after resumed data");
    CHECK(shutdown(sender,SD_SEND)==0,"finish loopback sender");
    CHECK(am_recv(receiver,&byte,1,0)==0,"receive EOF");
    CHECK(!scheduling_available||(!playback_task&&reverted_count==initial_reverts+1),"release playback scope once at EOF");
done:
    if(receiver!=INVALID_SOCKET)am_closesocket(receiver);
    if(sender!=INVALID_SOCKET)closesocket(sender);
    if(listener!=INVALID_SOCKET)closesocket(listener);
    WSACleanup();
    if(!failed)printf("PASS real loopback timeout, resumed bytes and EOF (MMCSS %s)\n",scheduling_available?"available":"unavailable; fault-injection test covers scheduling");
    return failed?1:0;
}
