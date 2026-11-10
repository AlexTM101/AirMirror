// GPL-3.0. WinUI 3 shell around the existing AirPlay receiver.
using Microsoft.UI;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using System.Diagnostics;
using System.Reflection;
using Windows.Foundation;
using Windows.Graphics;
using Windows.System;
using WinRT.Interop;
using Path = System.IO.Path;
using FontWeights = Microsoft.UI.Text.FontWeights;
using DispatcherQueuePriority = Microsoft.UI.Dispatching.DispatcherQueuePriority;

namespace AirMirror;

internal sealed partial class MainController {
    readonly MirrorApp app;
    internal readonly Window Window;
    internal readonly IntPtr Handle;
    internal readonly Grid root=new() { IsTabStop=true }, region=new(), welcome=new();
    readonly TextBlock status=Ui.Text("Запускаем приёмник…",13,true), receiverName=Ui.Text("AirMirror"), select=Ui.Text("");
    readonly Ellipse dot=new() { Width=8,Height=8,Fill=Ui.Brush(255,188,45),VerticalAlignment=VerticalAlignment.Center };
    readonly Border footer=new();
    readonly RowDefinition footerRow=new() { Height=new GridLength(30) };
    readonly Button settings=Ui.Icon("\uE713","Настройки · Ctrl+,"), restart=Ui.Icon("\uE72C","Перезапустить приёмник");
    readonly Microsoft.UI.Dispatching.DispatcherQueueTimer timer;
    readonly WindowHook hook;
    readonly TrayIcon tray;
    readonly RecoveryPolicy recovery=new();
    Receiver receiver;
    Preferences preferences;
    IntPtr videoHost;
    string failure;
    bool busy,closing,allowClose,connected,layoutQueued,testsStarted,loaded,verifyingLayout;
    int appliedOrientation;
    FrameSize lastFrame;
    long receiverStartedAt,automaticResizeUntil;
    RectInt32? welcomeBounds;
    double preferredVideoEdge=900;
    string iconPath;
    internal double Scale => Native.GetDpiForWindow(Handle)/96.0;
    internal Receiver Receiver => receiver;
    internal Preferences Options => preferences;
    internal bool VideoAttached => receiver!=null && Native.IsWindow(receiver.Window) && Native.GetParent(receiver.Window)==videoHost;

    internal MainController(MirrorApp application) {
        app=application;
        Window=new Window { Title="AirMirror 1.5.1" };
        Handle=WindowNative.GetWindowHandle(Window);
        Directory.CreateDirectory(Paths.Root);
        iconPath=Path.Combine(Paths.Root,"AirMirror.ico");
        using(var source=Assembly.GetExecutingAssembly().GetManifestResourceStream("app.ico"))
        using(var destination=File.Create(iconPath)) source.CopyTo(destination);
        Window.AppWindow.SetIcon(iconPath);
        BuildWelcome();
        root.Background=Ui.Theme("ApplicationPageBackgroundThemeBrush");
        root.RowDefinitions.Add(new RowDefinition { Height=new GridLength(1,GridUnitType.Star) });
        root.RowDefinitions.Add(footerRow);
        region.Children.Add(welcome);root.Children.Add(region);
        var controls=new StackPanel { Orientation=Orientation.Horizontal,HorizontalAlignment=HorizontalAlignment.Right,Margin=new Thickness(8,0,8,0) };
        controls.Children.Add(settings);
        var fullscreen=Ui.Icon("\uE740","Полный экран · F11"); fullscreen.Click+=(_,_)=>ToggleFull();controls.Children.Add(fullscreen);
        controls.Children.Add(restart);footer.Child=controls;Grid.SetRow(footer,1);root.Children.Add(footer);
        Window.Content=root;
        videoHost=Native.CreateWindowEx(0,"STATIC","AirMirror Video",unchecked((int)0x46000000),0,0,1,1,Handle,IntPtr.Zero,IntPtr.Zero,IntPtr.Zero);
        if(videoHost==IntPtr.Zero)throw new InvalidOperationException("Не удалось создать область видео.");
        Native.EnableWindow(videoHost,false);
        hook=new WindowHook(Handle,ObserveWindow);
        tray=new TrayIcon(Handle,iconPath);
        settings.Click+=async(_,_)=>await Settings();restart.Click+=async(_,_)=>await Restart();
        AddShortcut(VirtualKey.F11,VirtualKeyModifiers.None,ToggleFull);
        AddShortcut(VirtualKey.Escape,VirtualKeyModifiers.None,()=>{if(full)ToggleFull();});
        AddShortcut((VirtualKey)188,VirtualKeyModifiers.Control,()=>{_=Settings();});
        root.RightTapped+=(_,e)=>{ShowMenu();e.Handled=true;};
        root.ActualThemeChanged+=(_,_)=>UpdateTheme();
        root.Loaded+=async(_,_)=>{if(loaded)return;loaded=true;UpdateTheme();await Restart();};
        root.SizeChanged+=(_,_)=>QueueLayout();
        region.SizeChanged+=(_,_)=>{
            if(connected&&!hidden&&!full&&Stopwatch.GetTimestamp()>automaticResizeUntil)
                preferredVideoEdge=Math.Max(region.ActualWidth,region.ActualHeight);
            QueueLayout();
        };
        Window.AppWindow.Changed+=(_,e)=>{if(e.DidSizeChange){appliedOrientation=0;QueueLayout();}};
        Window.AppWindow.Closing+=(sender,e)=>{if(!allowClose){e.Cancel=true;_=Shutdown();}};
        Window.Activated+=(_,e)=>{
            active=e.WindowActivationState!=WindowActivationState.Deactivated;
            if(active&&loaded&&settingsWindow==null)root.Focus(FocusState.Programmatic);
            if(full){if(active)RevealControls();else HideControls();}
        };
        timer=Window.DispatcherQueue.CreateTimer();timer.Interval=TimeSpan.FromMilliseconds(150);
        timer.Tick+=(_,_)=>Poll();timer.Start();
        var monitor=MonitorInfo();int width=(int)Math.Min(1000*Scale,monitor.Work.Right-monitor.Work.Left-32);
        int height=(int)Math.Min(760*Scale,monitor.Work.Bottom-monitor.Work.Top-32);
        Window.AppWindow.MoveAndResize(new RectInt32(monitor.Work.Left+(monitor.Work.Right-monitor.Work.Left-width)/2,
            monitor.Work.Top+(monitor.Work.Bottom-monitor.Work.Top-height)/2,width,height));
    }

    void BuildWelcome() {
        welcome.Margin=new Thickness(48,24,48,22);
        welcome.RowDefinitions.Add(new RowDefinition { Height=new GridLength(1,GridUnitType.Star) });
        welcome.RowDefinitions.Add(new RowDefinition { Height=GridLength.Auto });
        var hero=new StackPanel { HorizontalAlignment=HorizontalAlignment.Center,VerticalAlignment=VerticalAlignment.Center,MaxWidth=650,Spacing=23 };
        var gradient=new LinearGradientBrush { StartPoint=new Point(0,0),EndPoint=new Point(1,1) };
        gradient.GradientStops.Add(new GradientStop { Color=Windows.UI.Color.FromArgb(255,121,185,255),Offset=0 });
        gradient.GradientStops.Add(new GradientStop { Color=Windows.UI.Color.FromArgb(255,66,101,243),Offset=.55 });
        gradient.GradientStops.Add(new GradientStop { Color=Windows.UI.Color.FromArgb(255,140,84,233),Offset=1 });
        var picture=new Canvas { Width=62,Height=52 };
        foreach(var xy in new[]{new Point(3,6),new Point(20,19)}) {
            var screen=new Rectangle { Width=40,Height=28,RadiusX=5,RadiusY=5,Stroke=Ui.Brush(255,255,255),StrokeThickness=2.5 };
            if(xy.X>3)screen.Fill=Ui.Brush(98,121,241);
            Canvas.SetLeft(screen,xy.X);Canvas.SetTop(screen,xy.Y);picture.Children.Add(screen);
        }
        hero.Children.Add(new Border { Child=picture,Width=88,Height=88,Background=gradient,CornerRadius=new CornerRadius(23),HorizontalAlignment=HorizontalAlignment.Center });
        var title=Ui.Text("Ваш iPhone. На большом экране.",30);title.FontWeight=FontWeights.SemiBold;title.TextAlignment=TextAlignment.Center;hero.Children.Add(title);
        var badge=new StackPanel { Orientation=Orientation.Horizontal,Spacing=12,VerticalAlignment=VerticalAlignment.Center };
        badge.Children.Add(dot);status.VerticalAlignment=VerticalAlignment.Center;badge.Children.Add(status);
        receiverName.VerticalAlignment=VerticalAlignment.Center;receiverName.FontWeight=FontWeights.SemiBold;badge.Children.Add(receiverName);
        var card=Ui.Card(badge,new Thickness(22,13,22,13));card.HorizontalAlignment=HorizontalAlignment.Center;hero.Children.Add(card);welcome.Children.Add(hero);
        var guide=new StackPanel { Spacing=15 };
        var heading=Ui.Text("Подключение за пару касаний",15);heading.FontWeight=FontWeights.SemiBold;guide.Children.Add(heading);
        var lines=new[]{"Подключите iPhone и ПК к одной сети Wi-Fi.","Откройте Пункт управления → Повтор экрана.","Выберите AirMirror — изображение появится здесь."};
        for(int i=0;i<lines.Length;i++){
            var row=new Grid { ColumnSpacing=10 };row.ColumnDefinitions.Add(new ColumnDefinition { Width=new GridLength(24) });row.ColumnDefinitions.Add(new ColumnDefinition());
            var number=Ui.Text((i+1).ToString(),12,true);number.HorizontalAlignment=HorizontalAlignment.Center;number.VerticalAlignment=VerticalAlignment.Center;
            row.Children.Add(new Border { Child=number,Width=23,Height=23,CornerRadius=new CornerRadius(12),Background=Ui.Theme("SubtleFillColorSecondaryBrush") });
            TextBlock label=i==2?select:Ui.Text(lines[i],13);if(i==2){select.Text=lines[i];select.FontSize=13;}
            label.VerticalAlignment=VerticalAlignment.Center;Grid.SetColumn(label,1);row.Children.Add(label);guide.Children.Add(row);
        }
        var instructions=Ui.Card(guide,new Thickness(25,21,25,21));instructions.MaxWidth=680;instructions.Margin=new Thickness(0,24,0,0);Grid.SetRow(instructions,1);welcome.Children.Add(instructions);
    }

    void AddShortcut(VirtualKey key,VirtualKeyModifiers modifiers,Action run) {
        var shortcut=new KeyboardAccelerator { Key=key,Modifiers=modifiers };
        shortcut.Invoked+=(_,e)=>{run();e.Handled=true;};root.KeyboardAccelerators.Add(shortcut);
    }
    void UpdateTheme() {
        int dark=root.ActualTheme==ElementTheme.Dark?1:0;Native.DwmSetWindowAttribute(Handle,20,ref dark,4);
    }
    void Status(string text,bool error=false,bool ready=false) {
        if(status.Text!=text)status.Text=text;
        dot.Fill=error?Ui.Brush(242,83,83):ready?Ui.Brush(40,184,110):Ui.Brush(255,188,45);
    }
    async Task Restart(bool automatic=false) {
        if(busy||closing)return;
        busy=true;settings.IsEnabled=restart.IsEnabled=false;
        if(!automatic)recovery.Reset();
        try {
            if(receiver!=null){await receiver.Stop();receiver=null;}
            connected=false;lastFrame=null;appliedOrientation=0;failure=null;
            Native.ShowWindow(videoHost,0);welcome.Visibility=Visibility.Visible;region.Background=null;footer.Background=null;
            RestoreWelcomeBounds();Status("Запускаем приёмник…");
            await Task.Run(Paths.Extract);
            if(closing)return;
            Native.SetDefaultDllDirectories(0x1000);Native.AddDllDirectory(Paths.Engine);
            preferences=Preferences.Load();preferences.Save();
            receiverName.Text=preferences.Name;select.Text="Выберите «"+preferences.Name+"» — изображение появится здесь.";
            receiver=new Receiver();var current=receiver;
            receiver.Line+=line=>{
                if(receiver!=current)return;
                if(line.Contains("Failed to start AirPlay server"))failure="Порт 7000 занят другим приложением.";
                if(line.Contains("SDL window/renderer create failed"))failure="Не удалось запустить видеовывод. Обновите драйвер видеокарты.";
            };
            receiver.Start(preferences,Program.SelfTest);receiverStartedAt=Stopwatch.GetTimestamp();
        } catch(Exception ex) {
            failure=ex.Message;Status("Не удалось запустить приёмник",true);
            if(Program.SelfTest){File.WriteAllText(Paths.Report,"FAIL\n"+ex);Environment.ExitCode=1;_=Shutdown();}
            else await Ui.Message(root.XamlRoot,"Не удалось запустить приёмник",ex.Message);
        } finally {busy=false;settings.IsEnabled=restart.IsEnabled=true;}
    }
    void Poll() {
        UpdateControls();
        if(closing||busy||verifyingLayout||receiver==null)return;
        if(receiver.Window==IntPtr.Zero&&!receiver.Exited){
            receiver.Window=Native.FindWindow(receiver.Process.Id);
            if(receiver.Window!=IntPtr.Zero){
                Native.ShowWindow(receiver.Window,0);Native.SetWindowLong(receiver.Window,-16,unchecked((int)0x46000000));
                Native.SetParent(receiver.Window,videoHost);Native.EnableWindow(receiver.Window,false);QueueLayout();
            }
        }
        if(receiver.Exited){
            if((!Program.SelfTest||Program.RecoveryTest)&&!receiver.Stopping&&failure==null&&recovery.Schedule(Stopwatch.GetTimestamp())){
                Status("Восстанавливаем приёмник…");
                if(recovery.Take(Stopwatch.GetTimestamp())){_=Restart(true);return;}
            } else Status(failure??"Приёмник остановлен. Нажмите «Перезапустить».",true);
        } else if(failure!=null)Status(failure,true);
        else if(receiver.Ready&&!receiver.Discovery)Status("Ожидаем регистрацию AirPlay в сети…");
        else if(receiver.Ready&&receiver.Discovery&&!receiver.HasFrame)Status("Готов к подключению",ready:true);
        if(!receiver.Exited&&receiver.Ready&&Stopwatch.GetTimestamp()-receiverStartedAt>30*Stopwatch.Frequency)recovery.Reset();
        bool playing=receiver.HasFrame&&receiver.Window!=IntPtr.Zero&&!receiver.Exited;
        if(playing!=connected){
            connected=playing;lastFrame=null;appliedOrientation=0;
            if(connected){if(!full)welcomeBounds=CurrentNormalBounds();preferredVideoEdge=Math.Max(region.ActualWidth,region.ActualHeight);}
            else RestoreWelcomeBounds();
            welcome.Visibility=connected?Visibility.Collapsed:Visibility.Visible;
            region.Background=connected?Ui.Brush(0,0,0):null;footer.Background=connected?Ui.Brush(0,0,0):null;
            footer.RequestedTheme=connected?ElementTheme.Dark:ElementTheme.Default;
            Native.ShowWindow(receiver.Window,connected?4:0);QueueLayout();
        }
        if(playing){
            Status("Экран iPhone подключён",ready:true);
            var frame=receiver.Frame;
            if(frame!=null&&(frame!=lastFrame||appliedOrientation==0)){lastFrame=frame;AdaptOrientation(frame);}
        }
        if(Program.SelfTest&&!testsStarted){
            if(receiver.Ready&&receiver.Discovery&&VideoAttached){testsStarted=true;_=RunTests();}
            else if(receiver.Exited||Stopwatch.GetTimestamp()-receiverStartedAt>25*Stopwatch.Frequency){testsStarted=true;_=RunTests();}
        }
    }
    internal void QueueLayout() {
        if(layoutQueued||closing)return;layoutQueued=true;
        Window.DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low,()=>{layoutQueued=false;PositionVideo();});
    }
    void PositionVideo() {
        if(hidden||Native.IsIconic(Handle))return;
        if(!connected){Native.ShowWindow(videoHost,0);return;}
        var point=region.TransformToVisual(root).TransformPoint(new Point(0,0));double scale=root.XamlRoot?.RasterizationScale??Scale;
        int width=Math.Max(1,(int)Math.Round(region.ActualWidth*scale)),height=Math.Max(1,(int)Math.Round(region.ActualHeight*scale));
        Native.SetWindowPos(videoHost,IntPtr.Zero,(int)Math.Round(point.X*scale),(int)Math.Round(point.Y*scale),width,height,0x4054);
        if(receiver!=null&&Native.IsWindow(receiver.Window))Native.SetWindowPos(receiver.Window,IntPtr.Zero,0,0,width,height,0x4054);
    }
    Native.MONITORINFO MonitorInfo() {
        var info=new Native.MONITORINFO { Size=(uint)System.Runtime.InteropServices.Marshal.SizeOf<Native.MONITORINFO>() };
        if(!Native.GetMonitorInfo(Native.MonitorFromWindow(Handle,2),ref info))throw new InvalidOperationException("Не удалось определить экран.");return info;
    }
    RectInt32 CurrentNormalBounds() {
        if(normalBounds.HasValue&&(hidden||Native.IsZoomed(Handle)||Native.IsIconic(Handle)))return normalBounds.Value;
        return new RectInt32(Window.AppWindow.Position.X,Window.AppWindow.Position.Y,Window.AppWindow.Size.Width,Window.AppWindow.Size.Height);
    }
    void RestoreWelcomeBounds() {
        if(full||hidden||Native.IsIconic(Handle))return;
        if(welcomeBounds.HasValue&&!Native.IsZoomed(Handle))Window.AppWindow.MoveAndResize(welcomeBounds.Value);
        welcomeBounds=null;
    }
    internal void AdaptOrientation(FrameSize frame) {
        if(full||hidden||Native.IsIconic(Handle)||Native.IsZoomed(Handle)||frame.Orientation==appliedOrientation)return;
        double scale=Scale;var work=MonitorInfo().Work;var current=Window.AppWindow;
        var chrome=new Size(current.Size.Width/scale-region.ActualWidth,current.Size.Height/scale-region.ActualHeight);
        var target=StreamLayout.Fit(frame,chrome,new Size((work.Right-work.Left)/scale,(work.Bottom-work.Top)/scale),preferredVideoEdge);
        int width=(int)Math.Round(target.Width*scale),height=(int)Math.Round(target.Height*scale);
        int x=Math.Clamp(current.Position.X+(current.Size.Width-width)/2,work.Left,Math.Max(work.Left,work.Right-width));
        int y=Math.Clamp(current.Position.Y+(current.Size.Height-height)/2,work.Top,Math.Max(work.Top,work.Bottom-height));
        automaticResizeUntil=Stopwatch.GetTimestamp()+Stopwatch.Frequency/2;appliedOrientation=frame.Orientation;
        Window.AppWindow.MoveAndResize(new RectInt32(x,y,width,height));QueueLayout();
    }
    internal async Task Shutdown() {
        if(closing)return;closing=true;timer.Stop();tray.Dispose();CloseControls();
        if(settingsWindow!=null)settingsWindow.Close();
        if(receiver!=null){try{await receiver.Stop();}catch{receiver.Dispose();}receiver=null;}
        hook.Dispose();Native.DestroyWindow(videoHost);videoHost=IntPtr.Zero;
        allowClose=true;Window.Close();app.Finish();
    }
    async Task RunTests() {
        try {await Verification.Run(this);}
        catch(Exception ex){File.WriteAllText(Paths.Report,"FAIL\n"+ex);Environment.ExitCode=1;}
        await Shutdown();
    }
}
