using System.Diagnostics;
using System.Reflection;
using AirMirror;

const string childReady = "AIRMIRROR_REGRESSION_CHILD_READY";
if (args.Contains("--child")) { Console.WriteLine(childReady); Console.Out.Flush(); Thread.Sleep(750); return 0; }
string root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "runtime-" + Guid.NewGuid().ToString("N")));
Environment.SetEnvironmentVariable("AIRMIRROR_DATA_DIR", root);
int failures = 0;
void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
void Run(string name, Action body) {
    try { body(); Console.WriteLine("PASS " + name); }
    catch (Exception ex) { failures++; Console.WriteLine("FAIL " + name + ": " + ex.GetBaseException().Message); }
}
ProcessStartInfo ChildStartInfo() {
    string host = Environment.ProcessPath!;
    var start = new ProcessStartInfo(host) {
        UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true
    };
    if (String.Equals(Path.GetFileNameWithoutExtension(host), "dotnet", StringComparison.OrdinalIgnoreCase))
        start.ArgumentList.Add(Assembly.GetExecutingAssembly().Location);
    start.ArgumentList.Add("--child");
    return start;
}
try {
    if (args.Contains("--bad-hash")) {
        Run("reject mismatched embedded payload before extracting", () => {
            bool rejected = false;
            try { Paths.Extract(); } catch (InvalidDataException) { rejected = true; }
            Check(rejected, "payload whose actual SHA-256 differs from the embedded hash was accepted");
            Check(!File.Exists(Paths.EngineExe), "mismatched payload wrote an executable");
            Check(!File.Exists(Path.Combine(Paths.Engine, "bundle.sha256")), "mismatched payload wrote a trusted marker");
        });
        return failures == 0 ? 0 : 1;
    }
    Run("self-launch starts the actual regression child", () => {
        using var child = new Process { StartInfo = ChildStartInfo() };
        child.Start();
        try {
            string signal = child.StandardOutput.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(5)).GetAwaiter().GetResult();
            Check(signal == childReady, "self-launch did not run the child branch of this test assembly");
            Check(child.WaitForExit(3000) && child.ExitCode == 0, "regression child did not finish normally");
        } finally { if (!child.HasExited) { child.Kill(); child.WaitForExit(); } }
    });
    Run("same-resolution reconnect restores portrait metadata", () => {
        using var receiver = new Receiver();
        var line = typeof(Receiver).GetMethod("OnLine", BindingFlags.Instance | BindingFlags.NonPublic);
        void Feed(string value) => line.Invoke(receiver, [value]);
        Feed("[12:00:00.000] INFO   VideoRenderer texture (GPU BGRA) created 498x1080");
        Feed("[12:00:01.000] INFO   VideoRenderer clear_session: reset cover / metadata / progress / video frame");
        Check(receiver.Frame == null && !receiver.HasFrame, "disconnect did not clear old metadata");
        Feed("[12:00:02.000] INFO   decoded first frame: 498x1080");
        Check(receiver.HasFrame && receiver.Frame?.Width == 498 && receiver.Frame.Height == 1080 && receiver.Frame.Orientation == -1,
            "first frame after reconnect has no usable portrait dimensions");
        Feed("[12:00:03.000] INFO   decoded first frame: 0x99999");
        Check(receiver.Frame?.Width == 498, "invalid decoder dimensions replaced validated dimensions");
    });
    Run("concurrent stop shares completion and tolerates dispose", () => {
        var child = new Process { StartInfo = ChildStartInfo() };
        child.Start();
        int pid = child.Id;
        var receiver = new Receiver { Process = child, Window = IntPtr.Zero };
        bool shared = false;
        Exception stopError = null;
        try {
            Task first = receiver.Stop(), second = receiver.Stop();
            shared = ReferenceEquals(first, second);
            Parallel.For(0, 16, _ => receiver.Dispose());
            try { Task.WhenAll(first, second).WaitAsync(TimeSpan.FromSeconds(10)).GetAwaiter().GetResult(); }
            catch (Exception ex) { stopError = ex; }
            receiver.Stop().GetAwaiter().GetResult();
            receiver.Dispose();
        } finally {
            try { using var remaining = Process.GetProcessById(pid); if (!remaining.HasExited) { remaining.Kill(); remaining.WaitForExit(); } }
            catch (ArgumentException) { }
        }
        Check(shared, "concurrent Stop calls return distinct completion tasks");
        Check(stopError == null, "Stop raced Dispose: " + stopError?.Message);
        Check(receiver.Process == null && receiver.Exited, "stop retained its disposed process");
    });
    Run("extract pinned bundle", Paths.Extract);
    if (!File.Exists(Paths.EngineExe)) return 1;
    string dependency = Path.Combine(Paths.Engine, "avutil-60.dll");
    byte[] original = File.ReadAllBytes(dependency);
    byte[] identity = [10, 27, 99, 41, 206, 12];
    string identityPath = Path.Combine(Paths.Engine, "identity.key");
    File.WriteAllBytes(identityPath, identity);
    Run("repair same-size dependency tamper with valid marker", () => {
        byte[] tampered = (byte[])original.Clone(); tampered[42] ^= 1;
        File.WriteAllBytes(dependency, tampered);
        Paths.Extract();
        Check(File.ReadAllBytes(dependency).SequenceEqual(original), "valid marker hid a modified native dependency");
        Check(File.ReadAllBytes(identityPath).SequenceEqual(identity), "cache repair changed pairing identity");
    });
    Run("restore missing dependency with valid marker", () => {
        File.Delete(dependency);
        Paths.Extract();
        Check(File.Exists(dependency) && File.ReadAllBytes(dependency).SequenceEqual(original), "valid marker hid a missing native dependency");
    });
    // Isolate the following failure probe even when the missing-file regression is red.
    File.WriteAllBytes(dependency, original);
    Run("valid cache avoids rewriting unchanged payload files", () => {
        var stamp = new DateTime(2020, 2, 3, 4, 5, 6, DateTimeKind.Utc);
        File.SetLastWriteTimeUtc(Paths.EngineExe, stamp);
        Paths.Extract();
        Check(File.GetLastWriteTimeUtc(Paths.EngineExe) == stamp, "valid cache was unnecessarily re-extracted");
    });
    Run("failed repair leaves no success marker and preserves identity", () => {
        using (var locked = new FileStream(dependency, FileMode.Open, FileAccess.ReadWrite, FileShare.None)) {
            bool failed = false;
            try { Paths.Extract(); } catch (IOException) { failed = true; } catch (UnauthorizedAccessException) { failed = true; }
            Check(failed, "locked corrupt/unreadable dependency was trusted");
            Check(!File.Exists(Path.Combine(Paths.Engine, "bundle.sha256")), "failed repair retained a success marker");
        }
        Check(File.ReadAllBytes(identityPath).SequenceEqual(identity), "failed repair changed pairing identity");
        Paths.Extract();
        Check(File.Exists(Path.Combine(Paths.Engine, "bundle.sha256")), "retry failed to finish cache repair");
    });
} finally {
    // This path is generated beneath this test's output directory, never the user's cache.
    if (Directory.Exists(root)) Directory.Delete(root, true);
}
return failures == 0 ? 0 : 1;
