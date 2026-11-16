using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Markup;
namespace LumiBar;
internal sealed partial class SettingsWindow
{
    private readonly List<RadioButton> _accentOptions=[];
    private TextBlock? _accentName;
    private static ControlTemplate? _accentTemplate;
    private void AddAccentPicker()
    {
        var group=Ui.Stack(9);var heading=new Grid();var title=Ui.Text("强调色",16);title.FontWeight=Microsoft.UI.Text.FontWeights.SemiBold;heading.Children.Add(title);
        _accentName=Ui.Text(AccentPalettes.Get(App.Instance.Prefs.AccentColor).Name,12,.75);_accentName.HorizontalAlignment=HorizontalAlignment.Right;heading.Children.Add(_accentName);group.Children.Add(heading);
        var row=new Grid{ColumnSpacing=4};Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(row,"强调色");
        _accentTemplate??=(ControlTemplate)XamlReader.Load("""<ControlTemplate xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" TargetType="RadioButton"><Grid Background="Transparent"><ContentPresenter Content="{TemplateBinding Content}" HorizontalAlignment="Center" VerticalAlignment="Center" /></Grid></ControlTemplate>""");
        for(var i=0;i<AccentPalettes.All.Count;i++){
            var palette=AccentPalettes.All[i];row.ColumnDefinitions.Add(new(){Width=new GridLength(1,GridUnitType.Star)});
            var option=new RadioButton{Tag=palette.Id,GroupName="LumiBarAccent",Width=34,Height=36,MinWidth=0,MinHeight=0,Padding=new Thickness(0),Template=_accentTemplate,HorizontalAlignment=HorizontalAlignment.Center,UseSystemFocusVisuals=true,IsChecked=App.Instance.Prefs.AccentColor==palette.Id};
            var face=new Grid{Width=34,Height=34};face.Children.Add(new Border{Width=28,Height=28,CornerRadius=new CornerRadius(9),Background=AccentTheme.Create(palette)});
            var ring=new Border{CornerRadius=new CornerRadius(12),BorderThickness=new Thickness(1.5),IsHitTestVisible=false};face.Children.Add(ring);
            var marker=new Border{Width=15,Height=15,CornerRadius=new CornerRadius(8),Background=Ui.Brush(Windows.UI.Color.FromArgb(232,255,255,255)),IsHitTestVisible=false,Child=new FontIcon{Glyph="\uE73E",FontSize=9,Foreground=Ui.Brush(Windows.UI.Color.FromArgb(255,35,48,58))}};face.Children.Add(marker);option.Content=face;
            void Update(bool animate){var selected=option.IsChecked==true;ring.BorderBrush=Ui.Brush(Root.ActualTheme==ElementTheme.Dark?Windows.UI.Color.FromArgb(220,226,232,240):Windows.UI.Color.FromArgb(180,67,77,89));Ui.Fade(ring,selected?1:0,animate,.14);Ui.Fade(marker,selected?1:0,animate,.14);Microsoft.UI.Xaml.Automation.AutomationProperties.SetHelpText(option,selected?"已选中":"切换强调色");}
            option.Checked+=(_,_)=>{Update(App.Instance.Prefs.Animations);if(App.Instance.Prefs.AccentColor!=palette.Id)App.Instance.SetAccent(palette.Id);};option.Unchecked+=(_,_)=>Update(App.Instance.Prefs.Animations);option.Loaded+=(_,_)=>Update(false);option.ActualThemeChanged+=(_,_)=>Update(false);
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(option,palette.Name);ToolTipService.SetToolTip(option,palette.Name);Ui.PressFeedback(option);Grid.SetColumn(option,i);row.Children.Add(option);_accentOptions.Add(option);
        }
        group.Children.Add(row);_page.Children.Add(group);
    }
    internal void UpdateAccentSelection()
    {
        if(_accentName!=null)_accentName.Text=AccentPalettes.Get(App.Instance.Prefs.AccentColor).Name;
        foreach(var option in _accentOptions)option.IsChecked=(string)option.Tag==App.Instance.Prefs.AccentColor;
    }
}
