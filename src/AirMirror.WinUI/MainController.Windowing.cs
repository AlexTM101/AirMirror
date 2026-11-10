// GPL-3.0. Tray lifecycle, native child geometry and true monitor fullscreen.
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Windows.Graphics;

namespace AirMirror;

internal sealed partial class MainController {
    bool hidden,full,active,wasMaximized;
    RectInt32? normalBounds;
    RectInt32 beforeFullscreen;
    bool maximizedBeforeFullscreen;
    Window controlsWindow;
    IntPtr controlsHandle;
    long lastActivity;
    Native.POINT lastPointer;
    bool pointerKnown,cursorHidden;

    IntPtr? ObserveWindow(uint message,IntPtr wparam,IntPtr lparam) {
        if(message==0x100&&settingsWindow==null){
            int key=(int)wparam;
            if(key==122){ToggleFull();return IntPtr.Zero;}
            if(key==27&&full){ToggleFull();return IntPtr.Zero;}
            if(key==188&&Native.GetKeyState(17)<0){_=Settings();return IntPtr.Zero;}
        }
        if(message==tray?.TaskbarCreated){tray.Add();return null;}
        if(message==TrayIcon.CallbackMessage){
            uint notification=(uint)lparam.ToInt64()&0xffff;
            if(notification==0x203)Restore();
            if(notification==0x205||notification==0x7b)ShowMenu(true);
            return IntPtr.Zero;
        }
        if(message==5){
            if((int)wparam==1){Window.DispatcherQueue.TryEnqueue(HideToTray);}
            else if(!hidden&&!full){
                wasMaximized=(int)wparam==2;
                if(!wasMaximized){Native.GetWindowRect(Handle,out var bounds);normalBounds=new RectInt32(bounds.Left,bounds.Top,bounds.Right-bounds.Left,bounds.Bottom-bounds.Top);}
            }
        }
        if(message==0x24&&!full){
            var limits=Marshal.PtrToStructure<Native.MINMAXINFO>(lparam);
            limits.MinTrackSize.X=(int)((connected?360:780)*Scale);limits.MinTrackSize.Y=(int)((connected?420:650)*Scale);
            Marshal.StructureToPtr(limits,lparam,false);return IntPtr.Zero;
        }
        if(message==0x2e0||message==0x7e){appliedOrientation=0;QueueLayout();if(full)Window.DispatcherQueue.TryEnqueue(PlaceControls);}
        if(message==0x20&&full&&cursorHidden){Native.SetCursor(IntPtr.Zero);return new IntPtr(1);}
        return null;
    }
    internal void Minimize() {
        if(closing||settingsWindow!=null)return;
        Native.ShowWindow(Handle,6);
    }
    void HideToTray() {
        if(closing||!Native.IsIconic(Handle))return;
        if(!tray.Registered)tray.Add();
        if(!tray.Registered)return; // Keep the taskbar entry if Explorer cannot accept the icon.
        hidden=true;HideControls();Window.AppWindow.IsShownInSwitchers=false;Window.AppWindow.Hide();
    }
    internal void Restore() {
        if(closing)return;
        hidden=false;Window.AppWindow.IsShownInSwitchers=true;Window.AppWindow.Show();
        Native.ShowWindow(Handle,full?9:wasMaximized?3:9);Native.SetForegroundWindow(Handle);
        if(!connected)RestoreWelcomeBounds();
        appliedOrientation=0;QueueLayout();
        root.Focus(FocusState.Programmatic);
        if(full)RevealControls();
        if(settingsWindow!=null)settingsWindow.Activate();
    }
    void ShowMenu(bool fromTray=false) {
        if(closing)return;
        Native.GetCursorPos(out var point);IntPtr menu=Native.CreatePopupMenu();
        try {
            void Item(uint id,string text,bool enabled=true) => Native.AppendMenu(menu,enabled?0u:1u,new UIntPtr(id),text);
            if(fromTray){Item(1,"Открыть AirMirror");Native.AppendMenu(menu,0x800,UIntPtr.Zero,null);}
            Item(2,"Настройки",preferences!=null&&!busy);Item(3,full?"Выйти из полного экрана":"Полный экран");Item(4,"Перезапустить приёмник",!busy);
            Item(5,"Свернуть в трей",!hidden&&settingsWindow==null);
            if(fromTray){Native.AppendMenu(menu,0x800,UIntPtr.Zero,null);Item(6,"Выход");}
            Native.SetForegroundWindow(Handle);
            uint command=Native.TrackPopupMenuEx(menu,0x102,point.X,point.Y,Handle,IntPtr.Zero);
            Native.PostMessage(Handle,0,IntPtr.Zero,IntPtr.Zero);
            switch(command){case 1:Restore();break;case 2:Restore();_=Settings();break;case 3:Restore();ToggleFull();break;case 4:_=Restart();break;case 5:Minimize();break;case 6:_=Shutdown();break;}
        } finally {Native.DestroyMenu(menu);}
    }
    internal void ToggleFull() {
        if(closing)return;
        if(!full){
            beforeFullscreen=CurrentNormalBounds();maximizedBeforeFullscreen=Native.IsZoomed(Handle);full=true;
            Window.AppWindow.SetPresenter(AppWindowPresenterKind.FullScreen);footerRow.Height=new GridLength(0);footer.Visibility=Visibility.Collapsed;
            if(!Program.SelfTest)EnsureControls();RevealControls();
        } else {
            full=false;HideControls();Window.AppWindow.SetPresenter(AppWindowPresenterKind.Overlapped);
            Window.AppWindow.MoveAndResize(beforeFullscreen);
            if(maximizedBeforeFullscreen)((OverlappedPresenter)Window.AppWindow.Presenter).Maximize();
            footer.Visibility=Visibility.Visible;footerRow.Height=new GridLength(30);
            if(!connected)RestoreWelcomeBounds();
        }
        appliedOrientation=0;QueueLayout();
    }
    void EnsureControls() {
        if(controlsWindow!=null)return;
        controlsWindow=new Window { Title="Управление AirMirror" };
        controlsHandle=WinRT.Interop.WindowNative.GetWindowHandle(controlsWindow);
        var panel=new StackPanel { Orientation=Orientation.Horizontal,Padding=new Thickness(6),Background=Ui.Brush(22,24,28) };
        var options=Ui.Icon("\uE713","Настройки");options.Click+=async(_,_)=>await Settings();panel.Children.Add(options);
        var retry=Ui.Icon("\uE72C","Перезапустить");retry.Click+=async(_,_)=>await Restart();panel.Children.Add(retry);
        var minimize=Ui.Icon("\uE921","Свернуть в трей");minimize.Click+=(_,_)=>Minimize();panel.Children.Add(minimize);
        var leave=Ui.Icon("\uE73F","Выйти из полного экрана · Esc");leave.Click+=(_,_)=>ToggleFull();panel.Children.Add(leave);
        panel.RequestedTheme=ElementTheme.Dark;panel.PointerMoved+=(_,_)=>{lastActivity=Stopwatch.GetTimestamp();SetCursorHidden(false);};
        controlsWindow.Content=panel;controlsWindow.AppWindow.IsShownInSwitchers=false;
        Native.SetWindowLongPtr(controlsHandle,-8,Handle);
        Native.SetWindowLong(controlsHandle,-20,Native.GetWindowLong(controlsHandle,-20)|0x80|0x8000000);
        var presenter=(OverlappedPresenter)controlsWindow.AppWindow.Presenter;
        presenter.SetBorderAndTitleBar(false,false);presenter.IsResizable=false;presenter.IsMaximizable=false;presenter.IsMinimizable=false;presenter.IsAlwaysOnTop=true;
        PlaceControls();controlsWindow.Activate();Native.SetForegroundWindow(Handle);controlsWindow.AppWindow.Hide();
    }
    void PlaceControls() {
        if(controlsWindow==null)return;
        var bounds=MonitorInfo().Monitor;int scaleWidth=(int)(132*Scale),scaleHeight=(int)(40*Scale);
        controlsWindow.AppWindow.MoveAndResize(new RectInt32(bounds.Right-scaleWidth-(int)(16*Scale),bounds.Top+(int)(16*Scale),scaleWidth,scaleHeight));
    }
    void RevealControls() {
        lastActivity=Stopwatch.GetTimestamp();SetCursorHidden(false);
        if(controlsWindow!=null&&!hidden&&active){PlaceControls();controlsWindow.AppWindow.Show(false);}
    }
    void HideControls() {controlsWindow?.AppWindow.Hide();SetCursorHidden(false);}
    void SetCursorHidden(bool hide) {
        if(cursorHidden==hide)return;cursorHidden=hide;Native.ShowCursor(!hide);
    }
    void UpdateControls() {
        if(!full||hidden||!active){if(cursorHidden)SetCursorHidden(false);return;}
        if(!Native.GetCursorPos(out var point))return;
        if(!pointerKnown||point.X!=lastPointer.X||point.Y!=lastPointer.Y){lastPointer=point;pointerKnown=true;RevealControls();}
        if(controlsHandle!=IntPtr.Zero&&Native.IsWindowVisible(controlsHandle)){
            Native.GetWindowRect(controlsHandle,out var bounds);
            if(point.X>=bounds.Left&&point.X<bounds.Right&&point.Y>=bounds.Top&&point.Y<bounds.Bottom)return;
        }
        if(Stopwatch.GetTimestamp()-lastActivity>2*Stopwatch.Frequency){controlsWindow?.AppWindow.Hide();SetCursorHidden(true);}
    }
    void CloseControls() {SetCursorHidden(false);if(controlsWindow!=null){controlsWindow.Close();controlsWindow=null;controlsHandle=IntPtr.Zero;}}

    internal async Task<bool> VerifyWindowing() {
        int pid=receiver.Process.Id;
        var original=CurrentNormalBounds();bool ok=tray.Registered;
        foreach(bool maximize in new[]{false,true}){
            if(maximize)((OverlappedPresenter)Window.AppWindow.Presenter).Maximize();
            else ((OverlappedPresenter)Window.AppWindow.Presenter).Restore();
            await Task.Delay(120);Minimize();await Task.Delay(150);
            ok&=hidden&&!Native.IsWindowVisible(Handle)&&!Window.AppWindow.IsShownInSwitchers;
            Restore();await Task.Delay(150);
            ok&=!hidden&&Native.IsWindowVisible(Handle)&&Window.AppWindow.IsShownInSwitchers&&Native.IsZoomed(Handle)==maximize&&VideoAttached&&receiver.Process.Id==pid&&!receiver.Exited;
        }
        ((OverlappedPresenter)Window.AppWindow.Presenter).Restore();Window.AppWindow.MoveAndResize(original);await Task.Delay(100);
        ToggleFull();await Task.Delay(150);Native.GetWindowRect(Handle,out var fullBounds);var monitor=MonitorInfo().Monitor;
        ok&=full&&footer.Visibility==Visibility.Collapsed&&fullBounds.Left==monitor.Left&&fullBounds.Top==monitor.Top&&fullBounds.Right==monitor.Right&&fullBounds.Bottom==monitor.Bottom;
        ToggleFull();await Task.Delay(150);Native.GetWindowRect(Handle,out var restored);
        ok&=!full&&footer.Visibility==Visibility.Visible&&Math.Abs(restored.Left-original.X)<=2&&Math.Abs(restored.Top-original.Y)<=2&&Math.Abs(restored.Right-restored.Left-original.Width)<=2&&Math.Abs(restored.Bottom-restored.Top-original.Height)<=2;
        return ok;
    }
    internal async Task<bool> VerifyOrientation() {
        var original=CurrentNormalBounds();bool result=true;
        verifyingLayout=true;connected=true;welcome.Visibility=Visibility.Collapsed;
        try {
            foreach(var frame in new[]{new FrameSize(498,1080),new FrameSize(1080,498)}){
                appliedOrientation=0;AdaptOrientation(frame);await Task.Delay(180);PositionVideo();
                Native.GetWindowRect(Handle,out var bounds);Native.GetWindowRect(videoHost,out var host);Native.GetWindowRect(receiver.Window,out var child);
                result&=(frame.Orientation<0?bounds.Bottom-bounds.Top>bounds.Right-bounds.Left:bounds.Right-bounds.Left>bounds.Bottom-bounds.Top)&&
                    host.Right>host.Left&&host.Bottom>host.Top&&host.Left==child.Left&&host.Top==child.Top&&host.Right==child.Right&&host.Bottom==child.Bottom&&VideoAttached;
            }
        } finally {
            verifyingLayout=false;connected=false;welcome.Visibility=Visibility.Visible;Native.ShowWindow(videoHost,0);
            Window.AppWindow.MoveAndResize(original);appliedOrientation=0;QueueLayout();
        }
        return result;
    }
}
