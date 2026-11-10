// GPL-3.0. Integration checks against the actual WinUI window and native receiver.
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Windows.Foundation;
namespace AirMirror;
internal static class Verification {
    internal static async Task Run(MainController owner) {
        var text=new StringBuilder("AirMirror 1.5.1 | WinUI 3 | "+DateTime.Now.ToString("s")+"\n");
        bool ok=true;
        if(Program.RecoveryTest) {
            var previous=owner.Receiver;
            int pid=previous.Process.Id;
            byte[] identity=SHA256.HashData(File.ReadAllBytes(Path.Combine(Paths.Engine,"identity.key")));
            previous.Process.Kill();
            var watch=Stopwatch.StartNew();
            while(watch.ElapsedMilliseconds<40000) {
                await Task.Delay(100);
                var current=owner.Receiver;
                if(current!=null && current.Process!=null && !current.Exited && current.Process.Id!=pid && current.Ready && current.Discovery && owner.VideoAttached)break;
            }
            var recovered=owner.Receiver;
            bool recovery=recovered!=null && recovered.Process!=null && !recovered.Exited && recovered.Process.Id!=pid && recovered.Ready && owner.VideoAttached && identity.SequenceEqual(SHA256.HashData(File.ReadAllBytes(Path.Combine(Paths.Engine,"identity.key"))));
            text.AppendLine("Actual child crash recovery and identity preserved: "+recovery);ok &= recovery;
        }
        var receiver=owner.Receiver;
        var preferences=owner.Options;
        bool ready=receiver!=null && receiver.Ready && receiver.Discovery && !receiver.Exited && owner.VideoAttached;
        text.AppendLine("Native startup, AirPlay discovery and child HWND attachment: "+ready);ok &= ready;
        if(!ready)throw new InvalidOperationException(text.ToString());
        bool windowing=await owner.VerifyWindowing();
        text.AppendLine("Tray normal/maximized restore, receiver continuity, full monitor bounds and restore: "+windowing);ok &= windowing;
        bool orientation=await owner.VerifyOrientation();
        text.AppendLine("Actual WinUI portrait/landscape resize and matching native video bounds: "+orientation);ok &= orientation;
        bool recoveryBounds=RecoveryPolicy.Verify();text.AppendLine("Bounded recovery backoff: "+recoveryBounds);ok &= recoveryBounds;
            double metadataMs;
            bool metadata = Receiver.VerifyMetadata(out metadataMs);
            text.AppendLine("Orientation metadata under log burst: " + metadata + " (" + metadataMs.ToString("0.0") + " ms)"); ok &= metadata;
            bool fitting = true;
            foreach (Size work in new[] { new Size(1920,1080),new Size(1366,768),new Size(1280,720),new Size(3840,2160) }) {
                Size portrait = StreamLayout.Fit(new FrameSize(498,1080),new Size(16,138),work,984);
                Size landscape = StreamLayout.Fit(new FrameSize(1080,498),new Size(16,138),work,984);
                fitting &= portrait.Height > portrait.Width && landscape.Width > landscape.Height && portrait.Height <= work.Height && landscape.Width <= work.Width;
            }
            text.AppendLine("Portrait/landscape fitting across screen sizes: " + fitting); ok &= fitting;
            byte[] engine = File.ReadAllBytes(Paths.EngineExe);
            bool embeddedGeometry = engine[0xcb1f1] == 0xc3 && engine.Skip(0xcb1f2).Take(5).All(b => b == 0x90);
            text.AppendLine("Native auto-resize disabled for embedding: " + embeddedGeometry); ok &= embeddedGeometry;
            text.AppendLine("Display synchronization enabled: " + preferences.Vsync);
            try {
                if (Native.LoadLibrary(System.IO.Path.Combine(Paths.Engine,"airnet.dll")) == IntPtr.Zero) throw new InvalidOperationException("Cannot load private network adapter");
                uint checks = Native.AirNetSelfTest(64);
                text.AppendLine("Native fragmented TCP/reconnect/EOF/error checks (64 cycles): 0x" + checks.ToString("X"));
                text.AppendLine("Immediate ACK available: " + ((checks & 2) != 0) + "; multimedia scheduling available: " + ((checks & 4) != 0));
                ok &= (checks & 25) == 25;
                if (Native.LoadLibrary(System.IO.Path.Combine(Paths.Engine,"avutil-60.dll")) == IntPtr.Zero) throw new InvalidOperationException("Cannot load frame verifier");
                uint playout = Native.AirVideoSelfTest();
                text.AppendLine("Fast/smooth cadence, rotation flush, bounded queue and shutdown: 0x" + playout.ToString("X"));
                ok &= playout == 63;
            } catch (Exception ex) { ok = false; text.AppendLine("Native network verification failure: " + ex.Message); }
            try {
                using (var client = new TcpClient()) {
                    client.ReceiveTimeout = client.SendTimeout = 3000; client.Connect(IPAddress.Loopback, 7000);
                    byte[] request = Encoding.ASCII.GetBytes("GET /info RTSP/1.0\r\nCSeq: 1\r\n\r\n");
                    var stream = client.GetStream(); stream.Write(request,0,request.Length);
                    string response;byte[] body=ReadRtsp(stream,out response);
                    text.AppendLine("RTSP /info: " + response.Split('\r')[0]);
                    if (!response.Contains("200 OK")) ok = false;
                    if (Native.LoadLibrary(System.IO.Path.Combine(Paths.Engine,"plist-2.0.dll")) == IntPtr.Zero) throw new InvalidOperationException("Cannot load plist verifier");
                    IntPtr plist = IntPtr.Zero;
                    try {
                        Native.plist_from_bin(body,(uint)body.Length,out plist);
                        if (plist == IntPtr.Zero) throw new InvalidDataException("Invalid /info plist");
                        IntPtr display = Native.plist_array_get_item(Native.plist_dict_get_item(plist,"displays"),0);
                        ulong advertised, refresh;
                        Native.plist_get_uint_val(Native.plist_dict_get_item(display,"maxFPS"),out advertised);
                        Native.plist_get_uint_val(Native.plist_dict_get_item(display,"refreshRate"),out refresh);
                        text.AppendLine("Advertised maxFPS: " + advertised + "; refreshRate: " + refresh + "; requested: " + preferences.Fps);
                        ok &= advertised == (ulong)preferences.Fps && refresh == (ulong)preferences.Fps && advertised <= 60;
                    } finally { if (plist != IntPtr.Zero) Native.plist_free(plist); }
                    IntPtr empty=Native.plist_new_dict(),binary=IntPtr.Zero;uint length;
                    byte[] setup;
                    Native.plist_dict_set_item(empty,"streams",Native.plist_new_array());
                    try { Native.plist_to_bin(empty,out binary,out length);setup=new byte[length];Marshal.Copy(binary,setup,0,setup.Length); }
                    finally { if(binary!=IntPtr.Zero)Native.plist_mem_free(binary);Native.plist_free(empty); }
                    SendRtsp(stream,"SETUP",2,setup);ReadRtsp(stream,out response);
                    if(!response.StartsWith("RTSP/1.0 200"))throw new InvalidDataException("Synthetic setup failed: "+response.Split('\r')[0]);
                    SendRtsp(stream,"TEARDOWN",3,new byte[0]);ReadRtsp(stream,out response);
                    bool rendered=response.StartsWith("RTSP/1.0 200")&&SpinWait.SpinUntil(delegate { return receiver.VideoSmokeSeen&&receiver.NativeFrameSeen; },2000);
                    text.AppendLine("Buffered frames through real native renderer and teardown: "+rendered);ok &= rendered;
                }
            } catch (Exception ex) { ok = false; text.AppendLine("RTSP failure: " + ex.Message); }
            bool activeAdapter = SpinWait.SpinUntil(delegate { return receiver.NetworkPolicySeen; },1000);
            text.AppendLine("Private network adapter active in real engine: " + activeAdapter); ok &= activeAdapter;
            text.AppendLine("Sustained iPhone Wi-Fi frame pacing: requires a live sender test");

        File.WriteAllText(Paths.Report,(ok?"PASS\n":"FAIL\n")+text,Encoding.UTF8);
        Environment.ExitCode=ok?0:1;
    }
        private static byte[] ReadRtsp(NetworkStream stream,out string response) {
            byte[] buffer=new byte[4096];var packet=new MemoryStream();int headerEnd=-1,bodyLength=0;response="";
            while(true){
                int count=stream.Read(buffer,0,buffer.Length);if(count==0)throw new EndOfStreamException("Incomplete RTSP reply");
                packet.Write(buffer,0,count);if(packet.Length>1048576)throw new InvalidDataException("Oversized RTSP reply");
                response=Encoding.ASCII.GetString(packet.ToArray());headerEnd=response.IndexOf("\r\n\r\n",StringComparison.Ordinal);if(headerEnd<0)continue;
                var length=Regex.Match(response.Substring(0,headerEnd),@"Content-Length:\s*(\d+)",RegexOptions.IgnoreCase);
                bodyLength=length.Success?int.Parse(length.Groups[1].Value):0;
                if(packet.Length>=headerEnd+4+bodyLength)break;
            }
            return packet.ToArray().Skip(headerEnd+4).Take(bodyLength).ToArray();
        }
        private static void SendRtsp(NetworkStream stream,string method,int sequence,byte[] body) {
            byte[] header=Encoding.ASCII.GetBytes(method+" / RTSP/1.0\r\nCSeq: "+sequence+"\r\nContent-Type: application/x-apple-binary-plist\r\nContent-Length: "+body.Length+"\r\n\r\n");
            stream.Write(header,0,header.Length);if(body.Length>0)stream.Write(body,0,body.Length);
        }
}
