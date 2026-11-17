using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Path=Microsoft.UI.Xaml.Shapes.Path;
using Windows.UI;

namespace LumiBar;
internal static class WeatherAlertBadge
{
    private static Color Color(string level)=>level switch{
        "red"=>ColorHelper.FromArgb(255,220,70,66),"orange"=>ColorHelper.FromArgb(255,226,125,35),
        "yellow"=>ColorHelper.FromArgb(255,204,157,27),"blue"=>ColorHelper.FromArgb(255,61,136,220),_=>Ui.Teal};
    private static FrameworkElement Icon(string level)
    {
        var tint=Color(level);var bright=ColorHelper.FromArgb(255,(byte)Math.Min(255,tint.R+30),(byte)Math.Min(255,tint.G+35),(byte)Math.Min(255,tint.B+35));
        var path=(Path)Microsoft.UI.Xaml.Markup.XamlReader.Load("<Path xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' Data='M10,2 Q12,-1 14,2 L23,18 Q25,22 21,22 L3,22 Q-1,22 1,18 Z' />");
        path.Fill=new LinearGradientBrush{StartPoint=new(0,0),EndPoint=new(1,1),GradientStops={new(){Color=bright,Offset=0},new(){Color=tint,Offset=1}}};
        var mark=(Path)Microsoft.UI.Xaml.Markup.XamlReader.Load("<Path xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' Data='M12,7 L12,13 M12,17 L12,17.3' Stroke='White' StrokeThickness='2' StrokeStartLineCap='Round' StrokeEndLineCap='Round'/>");
        var art=new Grid{Width=24,Height=24};art.Children.Add(path);art.Children.Add(mark);return new Viewbox{Width=15,Height=15,Child=art};
    }
    internal static Button Create(IReadOnlyList<WeatherAlert> alerts,int index,int extraCount)
    {
        var first=alerts[index];var row=new Grid{ColumnSpacing=4};row.ColumnDefinitions.Add(new(){Width=GridLength.Auto});row.ColumnDefinitions.Add(new(){Width=new GridLength(1,GridUnitType.Star)});row.ColumnDefinitions.Add(new(){Width=GridLength.Auto});row.Children.Add(Icon(first.Level));
        var text=Ui.Text(WeatherAlerts.Label(first),11);text.TextWrapping=TextWrapping.NoWrap;text.TextTrimming=TextTrimming.CharacterEllipsis;Grid.SetColumn(text,1);row.Children.Add(text);
        if(extraCount>0){var more=Ui.Text($"+{extraCount}",10,.75);Grid.SetColumn(more,2);row.Children.Add(more);}
        var button=new Button{Content=row,HorizontalContentAlignment=HorizontalAlignment.Stretch,Padding=new Thickness(6,3,6,3),MinHeight=26,MinWidth=0,MaxWidth=155,CornerRadius=new CornerRadius(9),BorderThickness=new Thickness(0),Background=Ui.Brush(ColorHelper.FromArgb(20,Color(first.Level).R,Color(first.Level).G,Color(first.Level).B)),VerticalAlignment=VerticalAlignment.Center};
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(button,$"{WeatherAlerts.Label(first)}{WeatherAlerts.LevelName(first.Level)}预警，共 {alerts.Count} 条天气预警，查看详情");
        ToolTipService.SetToolTip(button,"查看天气预警详情");
        button.Resources["ButtonBackgroundPointerOver"]=Ui.Brush(ColorHelper.FromArgb(38,Color(first.Level).R,Color(first.Level).G,Color(first.Level).B));button.Resources["ButtonBackgroundPressed"]=Ui.Brush(ColorHelper.FromArgb(55,Color(first.Level).R,Color(first.Level).G,Color(first.Level).B));
        button.Click+=(_,_)=>{
            var active=WeatherAlerts.Current(alerts,DateTimeOffset.UtcNow);if(active.Count==0){App.Instance.WeatherWindow.Render();return;}
            var content=Ui.Stack(14);var title=Ui.Text("天气预警",18);title.FontWeight=Microsoft.UI.Text.FontWeights.SemiBold;content.Children.Add(title);FrameworkElement? selected=null;
            foreach(var alert in active){
                var section=Ui.Stack(6);var head=Ui.Row(6);head.Children.Add(Icon(alert.Level));var name=Ui.Text(WeatherAlerts.Label(alert),14);name.FontWeight=Microsoft.UI.Text.FontWeights.SemiBold;head.Children.Add(name);section.Children.Add(head);
                if(!string.IsNullOrWhiteSpace(alert.Headline))section.Children.Add(Ui.Text(alert.Headline,13));
                if(!string.IsNullOrWhiteSpace(alert.Area))section.Children.Add(Ui.Text(alert.Area,11,.75));
                var times=new List<string>();if(alert.IssuedAt is {} sent)times.Add($"发布 {sent.ToLocalTime():MM/dd HH:mm}");if(alert.EndsAt is {} end)times.Add($"截至 {end.ToLocalTime():MM/dd HH:mm}");if(times.Count>0)section.Children.Add(Ui.Text(string.Join(" · ",times),11,.7));
                if(!string.IsNullOrWhiteSpace(alert.Description))section.Children.Add(Ui.Text(alert.Description,13));
                if(!string.IsNullOrWhiteSpace(alert.Instruction)){section.Children.Add(Ui.Text("防御指引",12,.8));section.Children.Add(Ui.Text(alert.Instruction,13));}
                if(!string.IsNullOrWhiteSpace(alert.Source))section.Children.Add(Ui.Text(alert.Source,11,.65));content.Children.Add(section);if(alert.Id==first.Id)selected=section;
            }
            var style=new Style(typeof(FlyoutPresenter));style.Setters.Add(new Setter(Control.CornerRadiusProperty,new CornerRadius(12)));style.Setters.Add(new Setter(Control.PaddingProperty,new Thickness(16)));style.Setters.Add(new Setter(FrameworkElement.MaxWidthProperty,360d));
            style.Setters.Add(new Setter(Control.BackgroundProperty,Ui.Brush(button.ActualTheme==ElementTheme.Dark?ColorHelper.FromArgb(250,29,33,43):ColorHelper.FromArgb(250,248,250,253))));
            var flyout=new Flyout{Content=new ScrollViewer{Content=content,Width=310,MaxHeight=360,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled,VerticalScrollBarVisibility=ScrollBarVisibility.Auto},FlyoutPresenterStyle=style};
            if(index>0&&selected is {} target)flyout.Opened+=(_,_)=>target.StartBringIntoView(new BringIntoViewOptions{AnimationDesired=false,VerticalAlignmentRatio=0});flyout.ShowAt(button);
        };
        return button;
    }
}
