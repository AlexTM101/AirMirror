// GPL-3.0. All application controls use Microsoft.UI.Xaml (WinUI 3).
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace AirMirror;

internal static class Ui {
    internal static SolidColorBrush Brush(byte r,byte g,byte b) => new(Color.FromArgb(255,r,g,b));
    internal static Brush Theme(string key) => (Brush)Application.Current.Resources[key];
    internal static TextBlock Text(string text,double size=14,bool muted=false) => new() {
        Text=text,FontSize=size,TextWrapping=TextWrapping.Wrap,
        Foreground=Theme(muted?"TextFillColorSecondaryBrush":"TextFillColorPrimaryBrush")
    };
    internal static Button Icon(string glyph,string name) {
        var button=new Button { Content=new FontIcon { Glyph=glyph,FontSize=13 },Width=30,Height=28,
            MinWidth=0,MinHeight=0,Padding=new Thickness(0),Background=new SolidColorBrush(Colors.Transparent),BorderThickness=new Thickness(0) };
        ToolTipService.SetToolTip(button,name);AutomationProperties.SetName(button,name);return button;
    }
    internal static Border Card(UIElement child,Thickness padding) => new() {
        Child=child,Padding=padding,CornerRadius=new CornerRadius(16),
        Background=Theme("CardBackgroundFillColorDefaultBrush"),BorderBrush=Theme("CardStrokeColorDefaultBrush"),BorderThickness=new Thickness(1)
    };
    internal static void Heading(StackPanel panel,string title,double top=16) {
        var label=Text(title);label.Margin=new Thickness(0,top,0,6);panel.Children.Add(label);
    }
    internal static TextBlock Description(string text,double indent=0) {
        var label=Text(text,12,true);label.Margin=new Thickness(indent,5,0,0);return label;
    }
    internal static async Task Message(XamlRoot root,string title,string text) {
        var dialog=new ContentDialog { XamlRoot=root,Title=title,Content=Text(text),CloseButtonText="Понятно" };
        await dialog.ShowAsync();
    }
}
