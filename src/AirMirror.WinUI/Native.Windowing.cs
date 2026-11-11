// GPL-3.0. Win32 integration for the WinUI shell and notification area.
using System.Runtime.InteropServices;

namespace AirMirror;

internal static partial class Native {
    internal delegate IntPtr SubclassProc(IntPtr window,uint message,IntPtr wparam,IntPtr lparam,UIntPtr id,IntPtr data);
    [DllImport("comctl32.dll")] internal static extern bool SetWindowSubclass(IntPtr window,SubclassProc callback,UIntPtr id,IntPtr data);
    [DllImport("comctl32.dll")] internal static extern bool RemoveWindowSubclass(IntPtr window,SubclassProc callback,UIntPtr id);
    [DllImport("comctl32.dll")] internal static extern IntPtr DefSubclassProc(IntPtr window,uint message,IntPtr wparam,IntPtr lparam);
    [DllImport("user32.dll",CharSet=CharSet.Unicode)] internal static extern IntPtr CreateWindowEx(int extended,string cls,string title,int style,int x,int y,int width,int height,IntPtr parent,IntPtr menu,IntPtr instance,IntPtr data);
    [DllImport("user32.dll")] internal static extern bool DestroyWindow(IntPtr window);
    [DllImport("user32.dll")] internal static extern IntPtr GetParent(IntPtr window);
    [DllImport("user32.dll")] internal static extern bool IsIconic(IntPtr window);
    [DllImport("user32.dll")] internal static extern bool IsZoomed(IntPtr window);
    [DllImport("user32.dll")] internal static extern bool SetForegroundWindow(IntPtr window);
    [DllImport("user32.dll")] internal static extern bool GetClientRect(IntPtr window,out RECT rect);
    [DllImport("user32.dll")] internal static extern bool ClientToScreen(IntPtr window,ref POINT point);
    [DllImport("user32.dll")] internal static extern uint GetDpiForWindow(IntPtr window);
    [DllImport("user32.dll",EntryPoint="SetWindowLongPtrW")] internal static extern IntPtr SetWindowLongPtr(IntPtr window,int index,IntPtr value);
    [DllImport("user32.dll",CharSet=CharSet.Unicode)] internal static extern int MessageBox(IntPtr owner,string text,string title,uint type);
    [DllImport("user32.dll",CharSet=CharSet.Unicode)] internal static extern IntPtr LoadImage(IntPtr instance,string name,uint type,int x,int y,uint flags);
    [DllImport("user32.dll")] internal static extern bool DestroyIcon(IntPtr icon);
    [DllImport("user32.dll")] internal static extern IntPtr SetCursor(IntPtr cursor);
    [DllImport("user32.dll")] internal static extern int ShowCursor(bool show);
    [DllImport("user32.dll")] internal static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] internal static extern short GetKeyState(int key);
    [DllImport("user32.dll",CharSet=CharSet.Unicode)] internal static extern uint RegisterWindowMessage(string message);
    [DllImport("user32.dll")] internal static extern IntPtr CreatePopupMenu();
    [DllImport("user32.dll",CharSet=CharSet.Unicode)] internal static extern bool AppendMenu(IntPtr menu,uint flags,UIntPtr id,string text);
    [DllImport("user32.dll")] internal static extern uint TrackPopupMenuEx(IntPtr menu,uint flags,int x,int y,IntPtr owner,IntPtr parameters);
    [DllImport("user32.dll")] internal static extern bool DestroyMenu(IntPtr menu);
    [DllImport("dwmapi.dll")] internal static extern int DwmSetWindowAttribute(IntPtr window,uint attribute,ref int value,uint size);
    [DllImport("kernel32.dll",CharSet=CharSet.Unicode)] internal static extern IntPtr AddDllDirectory(string path);
    [DllImport("kernel32.dll")] internal static extern bool SetDefaultDllDirectories(uint flags);
    [DllImport("shell32.dll",CharSet=CharSet.Unicode)] internal static extern bool Shell_NotifyIcon(uint action,ref NotifyData data);
    [StructLayout(LayoutKind.Sequential)] internal struct MINMAXINFO { public POINT Reserved,MaxSize,MaxPosition,MinTrackSize,MaxTrackSize; }
    [StructLayout(LayoutKind.Sequential,CharSet=CharSet.Unicode)] internal struct NotifyData {
        public uint Size; public IntPtr Window; public uint Id,Flags,Callback; public IntPtr Icon;
        [MarshalAs(UnmanagedType.ByValTStr,SizeConst=128)] public string Tip;
        public uint State,StateMask;
        [MarshalAs(UnmanagedType.ByValTStr,SizeConst=256)] public string Info;
        public uint Timeout;
        [MarshalAs(UnmanagedType.ByValTStr,SizeConst=64)] public string InfoTitle;
        public uint InfoFlags; public Guid Guid; public IntPtr BalloonIcon;
    }
}

internal sealed class WindowHook : IDisposable {
    readonly IntPtr handle;
    readonly Native.SubclassProc callback;
    readonly Func<uint,IntPtr,IntPtr,IntPtr?> process;
    internal WindowHook(IntPtr window,Func<uint,IntPtr,IntPtr,IntPtr?> handler) {
        handle=window; process=handler; callback=Observe;
        if (!Native.SetWindowSubclass(handle,callback,new UIntPtr(1),IntPtr.Zero)) throw new InvalidOperationException("Window hook failed");
    }
    IntPtr Observe(IntPtr window,uint message,IntPtr wparam,IntPtr lparam,UIntPtr id,IntPtr data) {
        var result=process(message,wparam,lparam);
        return result ?? Native.DefSubclassProc(window,message,wparam,lparam);
    }
    public void Dispose() { Native.RemoveWindowSubclass(handle,callback,new UIntPtr(1)); }
}

internal sealed class TrayIcon : IDisposable {
    Native.NotifyData data;
    readonly IntPtr icon;
    bool disposed;
    internal const uint CallbackMessage=0x8001;
    internal readonly uint TaskbarCreated=Native.RegisterWindowMessage("TaskbarCreated");
    internal bool Registered { get; private set; }
    internal TrayIcon(IntPtr handle,string iconPath) {
        icon=Native.LoadImage(IntPtr.Zero,iconPath,1,32,32,0x10);
        if (icon==IntPtr.Zero) throw new InvalidOperationException("Tray icon unavailable");
        data=new Native.NotifyData { Size=(uint)Marshal.SizeOf<Native.NotifyData>(),Window=handle,Id=1,Flags=7,
            Callback=CallbackMessage,Icon=icon,Tip="AirMirror 1.5.2",Info="",InfoTitle="" };
        Add();
    }
    internal void Add() { if (!disposed) Registered=Native.Shell_NotifyIcon(0,ref data); }
    public void Dispose() {
        if(disposed)return;disposed=true;
        Native.Shell_NotifyIcon(2,ref data);Native.DestroyIcon(icon);Registered=false;
    }
}
