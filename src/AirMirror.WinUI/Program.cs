// GPL-3.0. Native WinUI 3 application entry point and single-instance activation.
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using System.Runtime.InteropServices;

namespace AirMirror;

internal static class Program {
    internal static bool SelfTest, RecoveryTest;
    internal static void Trace(string text) { if(SelfTest){Directory.CreateDirectory(Paths.Root);File.AppendAllText(Path.Combine(Paths.Root,"startup-trace.txt"),text+"\n");} }
    [STAThread]
    static int Main(string[] args) {
        SelfTest = args.Contains("--self-test") || args.Contains("--self-test-recovery");
        RecoveryTest = args.Contains("--self-test-recovery");
        try {
            Trace("Main");
            using var activate = new EventWaitHandle(false, EventResetMode.AutoReset, "Local\\AirMirror.ShowWindow.v1");
            using var mutex = new Mutex(false, "Local\\AirMirror.Shell.v1");
            bool owned;
            try { owned = mutex.WaitOne(0); } catch (AbandonedMutexException) { owned = true; }
            if (!owned) { if (!SelfTest) activate.Set(); return SelfTest ? 2 : 0; }
            NativeLibrary.SetDllImportResolver(typeof(Program).Assembly, (name,assembly,path) => {
                string library=Path.Combine(Paths.Engine,name);
                return File.Exists(library)?NativeLibrary.Load(library):IntPtr.Zero;
            });
            WinRT.ComWrappersSupport.InitializeComWrappers();
            Trace("Com wrappers initialized");
            Application.Start(_ => {
                Trace("Application start callback");
                var queue = DispatcherQueue.GetForCurrentThread();
                SynchronizationContext.SetSynchronizationContext(new DispatcherQueueSynchronizationContext(queue));
                var app = new MirrorApp();
                Trace("Application constructed");
                app.Activation = ThreadPool.RegisterWaitForSingleObject(activate, (_, _) =>
                    queue.TryEnqueue(() => app.Controller?.Restore()), null, Timeout.Infinite, false);
            });
            mutex.ReleaseMutex();
            return Environment.ExitCode;
        } catch (Exception ex) {
            Directory.CreateDirectory(Paths.Root);
            File.WriteAllText(Path.Combine(Paths.Root,"startup-error.txt"),ex.ToString());
            if (!SelfTest) Native.MessageBox(IntPtr.Zero,"Не удалось открыть AirMirror.\n\n"+ex.Message,"AirMirror",0x10);
            return 1;
        }
    }
}

public sealed partial class MirrorApp : Application {
    internal MainController Controller;
    internal RegisteredWaitHandle Activation;
    internal MirrorApp() {
        Program.Trace("Application constructor");
        UnhandledException += (_, e) => {
            Directory.CreateDirectory(Paths.Root);
            File.WriteAllText(Path.Combine(Paths.Root,"ui-error.txt"),e.Exception.ToString());
            if (Program.SelfTest) { Environment.ExitCode=1; Exit(); }
            else Native.MessageBox(Controller?.Handle??IntPtr.Zero,e.Message,"Ошибка AirMirror",0x10);
            e.Handled = true;
        };
        InitializeComponent();
        Program.Trace("Resources loaded");
    }
    protected override void OnLaunched(LaunchActivatedEventArgs args) {
        try {
            Program.Trace("OnLaunched");
            Controller = new MainController(this);
            Program.Trace("Controller constructed");
            Controller.Window.Activate();
            Program.Trace("Window activated");
        } catch(Exception ex) {
            Directory.CreateDirectory(Paths.Root);File.WriteAllText(Path.Combine(Paths.Root,"launch-error.txt"),ex.ToString());
            if(!Program.SelfTest)Native.MessageBox(IntPtr.Zero,"Не удалось открыть AirMirror.\n\n"+ex.Message,"AirMirror",0x10);
            Environment.ExitCode=1;Exit();
        }
    }
    internal void Finish() { Activation?.Unregister(null); Exit(); }
}
