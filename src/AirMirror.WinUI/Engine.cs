// GPL-3.0. AirPlay backend carried forward from AirMirror 1.5.1.
using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Windows.Foundation;
namespace AirMirror {
    internal static class Paths {
        internal static readonly string Root = Environment.GetEnvironmentVariable("AIRMIRROR_DATA_DIR") ??
            System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AirMirror");
        internal static readonly string Engine = System.IO.Path.Combine(Root, "engine-0.4.1-playout-v4");
        internal static readonly string Preferences = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "AirMirror-Local", "settings.ini");
        internal static string Log { get { return System.IO.Path.Combine(Root, "receiver.log"); } }
        internal static string Report { get { return System.IO.Path.Combine(Root, "self-test.txt"); } }
        internal static string EngineExe { get { return System.IO.Path.Combine(Engine, "airplay-windows.exe"); } }
        internal static string FrameRate { get { return System.IO.Path.Combine(Root, "frame-rate.txt"); } }
        internal static string DisplayOptions { get { return System.IO.Path.Combine(Root, "display-options.ini"); } }
        internal static string PlaybackMode { get { return System.IO.Path.Combine(Root, "playback-mode.txt"); } }

        internal static void Extract() {
            string marker = System.IO.Path.Combine(Engine, "bundle.sha256");
            var asm = Assembly.GetExecutingAssembly();
            string expected;
            using (var reader = new StreamReader(asm.GetManifestResourceStream("engine.sha256"))) expected = reader.ReadToEnd().Trim();
            using (var stream = asm.GetManifestResourceStream("engine.zip"))
            {
                if (stream == null || !String.Equals(Convert.ToHexString(SHA256.HashData(stream)), expected, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("Native payload checksum mismatch");
                stream.Position = 0;
                using (var zip = new ZipArchive(stream, ZipArchiveMode.Read)) {
                    var repairs = new List<KeyValuePair<ZipArchiveEntry,string>>();
                    string prefix = System.IO.Path.GetFullPath(Engine) + System.IO.Path.DirectorySeparatorChar;
                    foreach (var entry in zip.Entries) {
                        string target = System.IO.Path.GetFullPath(System.IO.Path.Combine(Engine, entry.FullName));
                        if (!target.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Unsafe bundle entry");
                        if (!String.IsNullOrEmpty(entry.Name) && !CacheMatches(entry,target))
                            repairs.Add(new KeyValuePair<ZipArchiveEntry,string>(entry,target));
                    }
                    if (repairs.Count == 0 && File.Exists(marker) && File.ReadAllText(marker) == expected) return;
                    Directory.CreateDirectory(Engine);
                    // A partially repaired cache must never retain a success marker.
                    File.Delete(marker);
                    foreach (var repair in repairs) {
                        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(repair.Value));
                        string temporary = repair.Value + "." + Guid.NewGuid().ToString("N") + ".tmp";
                        try {
                            using (var src = repair.Key.Open()) using (var dst = File.Create(temporary)) src.CopyTo(dst);
                            File.Move(temporary,repair.Value,true);
                        } finally { if (File.Exists(temporary)) File.Delete(temporary); }
                    }
                    // Repair only bundled files. Pairing identity and other runtime files survive.
                    File.WriteAllText(marker, expected);
                }
            }
        }
        private static bool CacheMatches(ZipArchiveEntry entry,string target) {
            try {
                if (!File.Exists(target) || new FileInfo(target).Length != entry.Length) return false;
                using (var source = entry.Open()) using (var cached = File.OpenRead(target))
                    return SHA256.HashData(source).SequenceEqual(SHA256.HashData(cached));
            } catch (IOException) { return false; } catch (UnauthorizedAccessException) { return false; }
        }
    }

    internal sealed class Preferences {
        internal string Name = "AirMirror";
        internal int Width = 1920, Height = 1080;
        internal int Fps = 60;
        internal bool Gpu = true;
        internal bool Vsync;
        internal bool Smooth = true;
        internal static Preferences Load() {
            var value = new Preferences();
            try {
                foreach (string line in File.ReadAllLines(Paths.Preferences)) {
                    int eq = line.IndexOf('='); if (eq < 0) continue;
                    string key = line.Substring(0, eq), text = line.Substring(eq + 1);
                    int n;
                    if (key == "receiver_name" && text.Length > 0) value.Name = text;
                    if (key == "mirror_width" && int.TryParse(text, out n) && n >= 320 && n <= 7680) value.Width = n;
                    if (key == "mirror_height" && int.TryParse(text, out n) && n >= 240 && n <= 4320) value.Height = n;
                    if (key == "mirror_hwaccel") value.Gpu = text == "1";
                }
            } catch (IOException) { } catch (UnauthorizedAccessException) { }
            try {
                int fps;
                if (int.TryParse(File.ReadAllText(Paths.FrameRate).Trim(), out fps) && (fps == 30 || fps == 60)) value.Fps = fps;
            } catch (IOException) { } catch (UnauthorizedAccessException) { }
            try { value.Vsync = File.ReadAllText(Paths.DisplayOptions).Trim() == "vsync=1"; }
            catch (IOException) { } catch (UnauthorizedAccessException) { }
            try { value.Smooth = File.ReadAllText(Paths.PlaybackMode).Trim() != "fast"; }
            catch (IOException) { } catch (UnauthorizedAccessException) { }
            return value;
        }
        internal void Save() {
            if (Fps != 30) Fps = 60;
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Paths.Preferences));
            string text = "receiver_name=" + Name + "\nmirror_width=" + Width + "\nmirror_height=" + Height +
                "\nmax_fps=" + Fps + "\nrefresh_rate=" + Fps + "\nmirror_hwaccel=" + (Gpu ? "1" : "0") +
                "\nhevc_enabled=1\nvsync_enabled=" + (Vsync ? "1" : "0") + "\nfullscreen=0\nchromeless=1\nvideo_rotation=-1\ndiagnostics=0\n" +
                "window_placement_valid=0\nwindow_maximized=0\nwindow_width=1000\nwindow_height=720\n";
            File.WriteAllText(Paths.Preferences, text, new UTF8Encoding(false));
            Directory.CreateDirectory(Paths.Root);
            File.WriteAllText(Paths.FrameRate, Fps.ToString());
            File.WriteAllText(Paths.DisplayOptions, Vsync ? "vsync=1" : "vsync=0");
            File.WriteAllText(Paths.PlaybackMode, Smooth ? "smooth" : "fast");
        }
    }

    internal sealed class FrameSize {
        internal readonly int Width, Height;
        internal FrameSize(int width, int height) { Width = width; Height = height; }
        internal int Orientation { get { return Width > Height ? 1 : -1; } }
    }

    internal static class StreamLayout {
        internal static Size Fit(FrameSize frame, Size chrome, Size work, double longEdge) {
            double maxWidth = Math.Max(200, work.Width - 32), maxHeight = Math.Max(240, work.Height - 32);
            double scale = Math.Min(Math.Max(1,longEdge) / Math.Max(frame.Width,frame.Height),
                Math.Min(Math.Max(1,maxWidth-chrome.Width)/frame.Width, Math.Max(1,maxHeight-chrome.Height)/frame.Height));
            return new Size(Math.Min(maxWidth,Math.Max(Math.Min(360,maxWidth),frame.Width*scale+chrome.Width)),
                Math.Min(maxHeight,Math.Max(Math.Min(420,maxHeight),frame.Height*scale+chrome.Height)));
        }
    }

    internal static partial class Native {
        internal delegate bool EnumProc(IntPtr hwnd, IntPtr arg);
        [DllImport("user32.dll")] internal static extern bool EnumWindows(EnumProc callback, IntPtr arg);
        [DllImport("user32.dll")] internal static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint pid);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] internal static extern int GetClassName(IntPtr hwnd, StringBuilder text, int count);
        [DllImport("user32.dll")] internal static extern IntPtr SetParent(IntPtr child, IntPtr parent);
        [DllImport("user32.dll", EntryPoint="GetWindowLongW")] internal static extern int GetWindowLong(IntPtr hwnd, int index);
        [DllImport("user32.dll", EntryPoint="SetWindowLongW")] internal static extern int SetWindowLong(IntPtr hwnd, int index, int value);
        [DllImport("user32.dll")] internal static extern bool ShowWindow(IntPtr hwnd, int command);
        [DllImport("user32.dll")] internal static extern bool EnableWindow(IntPtr hwnd, bool enabled);
        [DllImport("user32.dll")] internal static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int w, int h, uint flags);
        [DllImport("user32.dll")] internal static extern bool PostMessage(IntPtr hwnd, uint msg, IntPtr wparam, IntPtr lparam);
        [DllImport("user32.dll")] internal static extern bool IsWindow(IntPtr hwnd);
        [DllImport("user32.dll")] internal static extern bool IsWindowVisible(IntPtr hwnd);
        [DllImport("user32.dll")] internal static extern bool GetWindowRect(IntPtr hwnd, out RECT rect);
        [DllImport("user32.dll")] internal static extern bool GetCursorPos(out POINT point);
        [DllImport("user32.dll")] internal static extern IntPtr MonitorFromWindow(IntPtr hwnd,uint flags);
        [DllImport("user32.dll",CharSet=CharSet.Unicode)] internal static extern bool GetMonitorInfo(IntPtr monitor,ref MONITORINFO info);
        [DllImport("kernel32.dll", CharSet=CharSet.Unicode)] internal static extern IntPtr CreateJobObject(IntPtr attrs, string name);
        [DllImport("kernel32.dll")] internal static extern bool SetInformationJobObject(IntPtr job, int infoClass, IntPtr info, uint length);
        [DllImport("kernel32.dll")] internal static extern bool AssignProcessToJobObject(IntPtr job, IntPtr process);
        [DllImport("kernel32.dll")] internal static extern bool CloseHandle(IntPtr handle);
        [DllImport("kernel32.dll", CharSet=CharSet.Unicode)] internal static extern IntPtr LoadLibrary(string path);
        [DllImport("plist-2.0.dll", CallingConvention=CallingConvention.Cdecl)] internal static extern int plist_from_bin(byte[] data, uint length, out IntPtr plist);
        [DllImport("plist-2.0.dll", CallingConvention=CallingConvention.Cdecl)] internal static extern IntPtr plist_dict_get_item(IntPtr plist, string key);
        [DllImport("plist-2.0.dll", CallingConvention=CallingConvention.Cdecl)] internal static extern IntPtr plist_array_get_item(IntPtr plist, uint index);
        [DllImport("plist-2.0.dll", CallingConvention=CallingConvention.Cdecl)] internal static extern void plist_get_uint_val(IntPtr plist, out ulong value);
        [DllImport("plist-2.0.dll", CallingConvention=CallingConvention.Cdecl)] internal static extern void plist_free(IntPtr plist);
        [DllImport("plist-2.0.dll", CallingConvention=CallingConvention.Cdecl)] internal static extern IntPtr plist_new_dict();
        [DllImport("plist-2.0.dll", CallingConvention=CallingConvention.Cdecl)] internal static extern IntPtr plist_new_array();
        [DllImport("plist-2.0.dll", CallingConvention=CallingConvention.Cdecl)] internal static extern void plist_dict_set_item(IntPtr plist,string key,IntPtr item);
        [DllImport("plist-2.0.dll", CallingConvention=CallingConvention.Cdecl)] internal static extern int plist_to_bin(IntPtr plist,out IntPtr data,out uint length);
        [DllImport("plist-2.0.dll", CallingConvention=CallingConvention.Cdecl)] internal static extern void plist_mem_free(IntPtr data);
        [DllImport("airnet.dll", CallingConvention=CallingConvention.Winapi)] internal static extern uint AirNetSelfTest(uint iterations);
        [DllImport("airnet.dll", CallingConvention=CallingConvention.Winapi)] internal static extern uint AirVideoSelfTest();
        [StructLayout(LayoutKind.Sequential)] internal struct RECT { public int Left, Top, Right, Bottom; }
        [StructLayout(LayoutKind.Sequential)] internal struct POINT { public int X,Y; }
        [StructLayout(LayoutKind.Sequential)] internal struct MONITORINFO { public uint Size;public RECT Monitor,Work;public uint Flags; }
        [StructLayout(LayoutKind.Sequential)] internal struct BasicLimits {
            public long ProcessUserTimeLimit, JobUserTimeLimit;
            public uint LimitFlags;
            public UIntPtr MinWorkingSet, MaxWorkingSet;
            public uint ActiveProcessLimit;
            public UIntPtr Affinity;
            public uint PriorityClass, SchedulingClass;
        }
        [StructLayout(LayoutKind.Sequential)] internal struct IoCounters { public ulong a,b,c,d,e,f; }
        [StructLayout(LayoutKind.Sequential)] internal struct ExtendedLimits {
            public BasicLimits Basic; public IoCounters Io;
            public UIntPtr ProcessMemory, JobMemory, PeakProcessMemory, PeakJobMemory;
        }
        internal static IntPtr FindWindow(int id) {
            IntPtr found = IntPtr.Zero;
            EnumWindows(delegate(IntPtr hwnd, IntPtr arg) {
                uint pid; GetWindowThreadProcessId(hwnd, out pid);
                if (pid != id) return true;
                var cls = new StringBuilder(100); GetClassName(hwnd, cls, cls.Capacity);
                if (cls.ToString().StartsWith("SDL", StringComparison.Ordinal)) { found = hwnd; return false; }
                return true;
            }, IntPtr.Zero);
            return found;
        }
    }

    internal sealed class Receiver : IDisposable {
        internal Process Process;
        internal IntPtr Window;
        internal volatile bool Ready, Discovery, HasFrame, Stopping, NetworkPolicySeen, NativeFrameSeen, VideoSmokeSeen;
        internal volatile FrameSize Frame;
        internal event Action<string> Line;
        private IntPtr job;
        private readonly BlockingCollection<string> logQueue = new BlockingCollection<string>(2048);
        private Task logTask;
        private volatile bool logClosed;
        private readonly object lifetime = new object();
        private Task stopTask;
        private bool disposed;
        internal void Start(Preferences preferences,bool smokeTest = false) {
            Directory.CreateDirectory(Paths.Root);
            logTask = Task.Factory.StartNew(WriteLog, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
            logQueue.TryAdd("AirMirror 1.5.2 receiver " + DateTime.Now.ToString("s"));
            Process = new Process();
            Process.StartInfo = new ProcessStartInfo(Paths.EngineExe, "--log") {
                UseShellExecute = false, CreateNoWindow = true,
                WorkingDirectory = Paths.Engine, RedirectStandardOutput = true, RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8,
                WindowStyle = ProcessWindowStyle.Hidden
            };
            Process.OutputDataReceived += delegate(object sender, DataReceivedEventArgs e) { OnLine(e.Data); };
            Process.ErrorDataReceived += delegate(object sender, DataReceivedEventArgs e) { OnLine(e.Data); };
            Process.StartInfo.EnvironmentVariables["AIRMIRROR_VIDEO_MODE"] = preferences.Smooth ? "smooth" : "fast";
            Process.StartInfo.EnvironmentVariables["AIRMIRROR_VIDEO_FPS"] = preferences.Fps.ToString();
            Process.StartInfo.EnvironmentVariables["AIRMIRROR_VIDEO_SMOKE"] = smokeTest ? "1" : "0";
            Process.Start();
            try { Process.PriorityClass = ProcessPriorityClass.AboveNormal; } catch (System.ComponentModel.Win32Exception) { } catch (InvalidOperationException) { }
            job = Native.CreateJobObject(IntPtr.Zero, null);
            if (job != IntPtr.Zero) {
                var limits = new Native.ExtendedLimits(); limits.Basic.LimitFlags = 0x2000;
                IntPtr ptr = Marshal.AllocHGlobal(Marshal.SizeOf(limits));
                try { Marshal.StructureToPtr(limits, ptr, false); Native.SetInformationJobObject(job, 9, ptr, (uint)Marshal.SizeOf(limits)); Native.AssignProcessToJobObject(job, Process.Handle); }
                finally { Marshal.FreeHGlobal(ptr); }
            }
            Process.BeginOutputReadLine(); Process.BeginErrorReadLine();
        }
        private void OnLine(string text) {
            if (String.IsNullOrEmpty(text)) return;
            if (!logClosed) { try { logQueue.TryAdd(text); } catch (InvalidOperationException) { } }
            if (text.Contains("Ready. Close")) Ready = true;
            if (text.Contains("AirNet: immediate TCP acknowledgements enabled") || text.Contains("AirNet: TCP acknowledgement policy unavailable")) NetworkPolicySeen = true;
            if (text.Contains("AirVideo: native renderer smoke complete")) VideoSmokeSeen = true;
            if (text.Contains("mDNS register:") && text.Contains("_airplay._tcp") && text.Contains("status=0")) Discovery = true;
            if (text.Contains("decoded first frame:") || text.Contains("VideoRenderer texture (")) HasFrame = true;
            bool texture = text.Contains("VideoRenderer texture (");
            if (texture) NativeFrameSeen = true;
            if (texture || text.Contains("decoded first frame:")) {
                string marker = texture ? " created " : "decoded first frame: ";
                int offset = text.LastIndexOf(marker,StringComparison.Ordinal);
                if (offset >= 0) {
                    string[] dimensions = text.Substring(offset+marker.Length).Trim().Split('x');
                    int width, height;
                    if (dimensions.Length == 2 && int.TryParse(dimensions[0],out width) && int.TryParse(dimensions[1],out height) &&
                        width > 0 && height > 0 && width <= 8192 && height <= 8192) Frame = new FrameSize(width,height);
                }
            }
            // A retiring transport may end after its replacement starts. Only
            // the renderer's session-aware clear is authoritative for the UI.
            if (text.Contains("VideoRenderer clear_session:") || text.Contains("Connection ended: cleared active session=")) {
                HasFrame = false; Frame = null;
            }
            var handler = Line; if (handler != null) handler(text);
        }
        private void WriteLog() {
            try {
                if (File.Exists(Paths.Log)) File.Copy(Paths.Log,System.IO.Path.Combine(Paths.Root,"receiver.previous.log"),true);
                using (var writer = new StreamWriter(Paths.Log,false,new UTF8Encoding(false),65536)) {
                    var flushTimer = Stopwatch.StartNew();
                    while (!logQueue.IsCompleted) {
                        string line;
                        if (logQueue.TryTake(out line,200)) writer.WriteLine(line);
                        if (flushTimer.ElapsedMilliseconds >= 250) {
                            writer.Flush();
                            if (writer.BaseStream.Length > 2*1024*1024) { writer.BaseStream.SetLength(0); writer.BaseStream.Position = 0; }
                            flushTimer.Restart();
                        }
                    }
                }
            } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
        internal static bool VerifyMetadata(out double elapsedMilliseconds) {
            var watch = Stopwatch.StartNew();
            using (var probe = new Receiver()) {
                for (int i=0; i<10000; i++) probe.OnLine("diagnostic burst");
                probe.OnLine("VideoRenderer texture (GPU BGRA) created 498x1080");
                bool portrait = probe.Frame != null && probe.Frame.Width == 498 && probe.Frame.Height == 1080 && probe.Frame.Orientation == -1;
                probe.OnLine("VideoRenderer texture (GPU BGRA) created 1080x498");
                bool landscape = probe.Frame != null && probe.Frame.Width == 1080 && probe.Frame.Height == 498 && probe.Frame.Orientation == 1;
                probe.OnLine("mirror stream ended: 123 frames");
                bool replacementSurvived = probe.HasFrame && probe.Frame != null;
                probe.OnLine("VideoRenderer clear_session: reset cover / metadata / progress / video frame");
                bool cleared = !probe.HasFrame && probe.Frame == null;
                elapsedMilliseconds = watch.Elapsed.TotalMilliseconds;
                return portrait && landscape && replacementSurvived && cleared && probe.logQueue.Count <= 2048;
            }
        }
        internal bool Exited { get { try { return Process == null || Process.HasExited; } catch { return true; } } }
        internal Task Stop() {
            lock (lifetime) {
                if (stopTask != null) return stopTask;
                Stopping = true;
                if (disposed) return stopTask = Task.CompletedTask;
                var current = Process;
                var window = Window;
                return stopTask = Task.Run(delegate {
                    try {
                        if (current != null && !current.HasExited) {
                            if (window != IntPtr.Zero) Native.PostMessage(window, 0x0010, IntPtr.Zero, IntPtr.Zero);
                            if (!current.WaitForExit(5000)) { current.Kill(); current.WaitForExit(2000); }
                        }
                    } catch (InvalidOperationException) { }
                    finally { FinishDispose(); }
                });
            }
        }
        public void Dispose() {
            lock (lifetime) {
                if (disposed) return;
                if (stopTask != null && !stopTask.IsCompleted) {
                    // Terminate the job if necessary, but keep the captured Process
                    // usable until the shared stop operation has finished waiting.
                    if (job != IntPtr.Zero) { Native.CloseHandle(job); job = IntPtr.Zero; }
                    return;
                }
                FinishDispose();
            }
        }
        private void FinishDispose() {
            Task writer;
            lock (lifetime) {
                if (disposed) return;
                disposed = true;
                if (job != IntPtr.Zero) { Native.CloseHandle(job); job = IntPtr.Zero; }
                if (Process != null) { Process.Dispose(); Process = null; }
                logClosed = true; logQueue.CompleteAdding();
                writer = logTask;
            }
            if (writer != null) { try { writer.Wait(750); } catch (AggregateException) { } }
        }
    }

}
