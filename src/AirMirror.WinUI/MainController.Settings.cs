// GPL-3.0. Native WinUI 3 settings with existing, compatible receiver preferences.
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System.Diagnostics;
using System.Text;
using Windows.Graphics;

namespace AirMirror;

internal sealed partial class MainController {
    Window settingsWindow;
    internal async Task Settings() {
        if(closing||busy||preferences==null)return;
        if(settingsWindow!=null){settingsWindow.Activate();return;}
        var dialog=new Window { Title="Настройки AirMirror 1.5.2" };settingsWindow=dialog;
        var handle=WinRT.Interop.WindowNative.GetWindowHandle(dialog);Native.SetWindowLongPtr(handle,-8,Handle);
        dialog.AppWindow.IsShownInSwitchers=false;dialog.AppWindow.SetIcon(iconPath);
        var presenter=(OverlappedPresenter)dialog.AppWindow.Presenter;
        presenter.IsResizable=false;presenter.IsMaximizable=false;presenter.IsMinimizable=false;
        var panel=new StackPanel { Padding=new Thickness(28,23,28,24),Spacing=0,Background=Ui.Theme("ApplicationPageBackgroundThemeBrush") };
        dialog.Content=new ScrollViewer { Content=panel,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled,VerticalScrollBarVisibility=ScrollBarVisibility.Auto };
        var heading=Ui.Text("Настройки",24);heading.FontWeight=Microsoft.UI.Text.FontWeights.SemiBold;heading.Margin=new Thickness(0,0,0,5);panel.Children.Add(heading);
        Ui.Heading(panel,"Имя в списке «Повтор экрана»",14);
        var input=new TextBox { Text=preferences.Name,MaxLength=30,HorizontalAlignment=HorizontalAlignment.Stretch };panel.Children.Add(input);
        ComboBox Choice(string title,string[] choices,int selected) {
            Ui.Heading(panel,title);var box=new ComboBox { HorizontalAlignment=HorizontalAlignment.Stretch };
            foreach(string choice in choices)box.Items.Add(choice);box.SelectedIndex=selected;panel.Children.Add(box);return box;
        }
        var quality=Choice("Качество изображения",new[]{"1080p · сбалансированное","1440p · чётче текст"},preferences.Width>1920?1:0);
        var fps=Choice("Частота кадров",new[]{"30 FPS","60 FPS"},preferences.Fps==30?0:1);
        var playback=Choice("Режим просмотра",new[]{"Плавнее","Быстрее"},preferences.Smooth?0:1);
        panel.Children.Add(Ui.Description("Плавнее — небольшой буфер кадров.\nБыстрее — минимальная задержка."));
        var gpu=new CheckBox { Content="Использовать видеокарту",IsChecked=preferences.Gpu,Margin=new Thickness(0,16,0,0) };panel.Children.Add(gpu);
        panel.Children.Add(Ui.Description("Обрабатывает видео на видеокарте и снижает нагрузку на процессор.",32));
        var sync=new CheckBox { Content="Синхронизация с монитором",IsChecked=preferences.Vsync,Margin=new Thickness(0,10,0,0) };panel.Children.Add(sync);
        panel.Children.Add(Ui.Description("Подстраивает показ кадров под частоту монитора, уменьшая разрывы изображения. Может немного увеличить задержку.",32));
        var reconnect=Ui.Description("После сохранения подключите iPhone заново.");reconnect.Margin=new Thickness(0,18,0,14);panel.Children.Add(reconnect);
        var buttons=new StackPanel { Orientation=Orientation.Horizontal,HorizontalAlignment=HorizontalAlignment.Right,Spacing=8 };
        var cancel=new Button { Content="Отмена" };var save=new Button { Content="Сохранить" };
        save.Style=(Style)Application.Current.Resources["AccentButtonStyle"];
        buttons.Children.Add(cancel);buttons.Children.Add(save);panel.Children.Add(buttons);
        var firewall=new HyperlinkButton { Content="Нет AirMirror в списке? Разрешить доступ к сети",Margin=new Thickness(-10,14,0,0),FontSize=12 };
        panel.Children.Add(firewall);
        var done=new TaskCompletionSource<bool>();
        cancel.Click+=(_,_)=>dialog.Close();
        save.Click+=async(_,_)=>{
            string clean=new(input.Text.Trim().Where(c=>char.IsLetterOrDigit(c)||c==' '||c=='-'||c=='_').ToArray());
            if(clean.Length==0||Encoding.UTF8.GetByteCount(clean)>48){await Ui.Message(panel.XamlRoot,"Имя приёмника","Введите имя до 48 байт: буквы, цифры, пробелы и дефисы.");return;}
            preferences.Name=clean;preferences.Width=quality.SelectedIndex==0?1920:2560;preferences.Height=quality.SelectedIndex==0?1080:1440;
            preferences.Fps=fps.SelectedIndex==0?30:60;preferences.Smooth=playback.SelectedIndex==0;preferences.Gpu=gpu.IsChecked==true;preferences.Vsync=sync.IsChecked==true;
            done.TrySetResult(true);dialog.Close();
        };
        firewall.Click+=async(_,_)=>{
            firewall.IsEnabled=false;
            try {
                using var helper=Process.Start(new ProcessStartInfo(Paths.EngineExe,"--configure-firewall") { UseShellExecute=true,Verb="runas",WorkingDirectory=Paths.Engine });
                await helper.WaitForExitAsync();
                if(!closing&&settingsWindow==dialog&&panel.XamlRoot!=null)
                    await Ui.Message(panel.XamlRoot,"Подключение AirPlay",helper.ExitCode==0?"Доступ к локальной сети разрешён. Откройте «Повтор экрана» на iPhone заново.":"Проверьте доступ AirMirror в брандмауэре Windows.");
            } catch(System.ComponentModel.Win32Exception) { }
            finally {if(!closing&&settingsWindow==dialog)firewall.IsEnabled=true;}
        };
        dialog.Closed+=(_,_)=>{settingsWindow=null;done.TrySetResult(false);Native.EnableWindow(Handle,true);if(!closing)Window.Activate();};
        var work=MonitorInfo().Work;double scale=Scale;
        int width=(int)(475*scale),height=Math.Min((int)(835*scale),work.Bottom-work.Top-24);
        dialog.AppWindow.MoveAndResize(new RectInt32(work.Left+(work.Right-work.Left-width)/2,work.Top+(work.Bottom-work.Top-height)/2,width,height));
        Native.EnableWindow(Handle,false);dialog.Activate();
        bool saved=await done.Task;
        if(saved&&!closing){
            try{preferences.Save();await Restart();}
            catch(Exception ex){await Ui.Message(root.XamlRoot,"Не удалось сохранить настройки",ex.Message);}
        }
    }
}
