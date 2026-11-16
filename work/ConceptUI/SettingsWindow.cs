using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Storage.Streams;
namespace LumiBar;
internal sealed class SettingsWindow:SurfaceWindow
{
    private readonly StackPanel _page=Ui.Stack(22),_nav=Ui.Stack(6);private readonly List<Button> _navButtons=[];private MusicCapsuleContent? _previewMusic;private string _selected="外观";
    internal SettingsWindow():base("LumiBar · 设置")
    {
        Body.RowDefinitions.Add(new(){Height=new GridLength(54)});Body.RowDefinitions.Add(new(){Height=new GridLength(1,GridUnitType.Star)});
        var header=new Grid{Padding=new Thickness(20,8,12,8),BorderThickness=new Thickness(0,0,0,1),BorderBrush=Ui.Brush(ColorHelper.FromArgb(24,128,128,128))};var brand=Ui.Row(10);var mark=new Border{Width=26,Height=26,CornerRadius=new CornerRadius(8),Background=new LinearGradientBrush{StartPoint=new(0,0),EndPoint=new(1,1),GradientStops={new GradientStop{Color=ColorHelper.FromArgb(255,184,170,255)},new GradientStop{Color=ColorHelper.FromArgb(255,115,110,218),Offset=1}}},Child=new Border{Width=15,Height=11,CornerRadius=new CornerRadius(5),Background=Ui.Brush(ColorHelper.FromArgb(180,255,255,255)),Margin=new Thickness(-3,-3,0,0)}};brand.Children.Add(mark);var name=Ui.Text("LumiBar",21);name.FontWeight=Microsoft.UI.Text.FontWeights.SemiBold;brand.Children.Add(name);header.Children.Add(brand);var close=Ui.IconButton("\uE8BB","关闭设置",(_,_)=>HideSurface());close.HorizontalAlignment=HorizontalAlignment.Right;header.Children.Add(close);Body.Children.Add(header);
        var layout=new Grid();layout.ColumnDefinitions.Add(new(){Width=new GridLength(170)});layout.ColumnDefinitions.Add(new(){Width=new GridLength(1,GridUnitType.Star)});Grid.SetRow(layout,1);Body.Children.Add(layout);_nav.Padding=new Thickness(14,24,12,0);layout.Children.Add(_nav);
        foreach(var (label,icon) in new[]{("常规","\uE713"),("外观","\uE70F"),("任务栏","\uE737"),("天气","\uE706"),("音乐与歌词","\uE8D6"),("插件","\uEA86")}){var row=Ui.Row(12);row.Children.Add(Ui.Icon(icon,18));row.Children.Add(Ui.Text(label,15));var button=new Button{Content=row,Tag=label,HorizontalAlignment=HorizontalAlignment.Stretch,HorizontalContentAlignment=HorizontalAlignment.Left,Height=42,Padding=new Thickness(12,0,8,0),CornerRadius=new CornerRadius(9),BorderThickness=new Thickness(0)};button.Click+=(_,_)=>Render(label);_navButtons.Add(button);_nav.Children.Add(button);}
        var scroll=new ScrollViewer{Content=_page,Padding=new Thickness(22,24,24,22),HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled};Grid.SetColumn(scroll,1);layout.Children.Add(scroll);Render("外观");SurfaceVisibilityChanged+=open=>_previewMusic?.SetActive(open);
    }
    internal void ShowQuickMenu()=>ShowSurface();
    private void Heading(string title,string description){var group=Ui.Stack(7);var text=Ui.Text(title,28);text.FontWeight=Microsoft.UI.Text.FontWeights.SemiBold;group.Children.Add(text);group.Children.Add(Ui.Text(description,13,.8));_page.Children.Add(group);}
    private void Separator()=>_page.Children.Add(new Border{Height=1,Background=Ui.Brush(ColorHelper.FromArgb(25,128,128,128))});
    private void Toggle(string title,string detail,bool value,Action<bool> action){var row=new Grid{ColumnSpacing=14};row.ColumnDefinitions.Add(new(){Width=new GridLength(1,GridUnitType.Star)});row.ColumnDefinitions.Add(new(){Width=GridLength.Auto});var words=Ui.Stack(5);var label=Ui.Text(title,16);label.FontWeight=Microsoft.UI.Text.FontWeights.SemiBold;words.Children.Add(label);words.Children.Add(Ui.Text(detail,12,.75));row.Children.Add(words);var toggle=new ToggleSwitch{IsOn=value,OnContent="",OffContent="",MinWidth=0,VerticalAlignment=VerticalAlignment.Center};Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(toggle,title);toggle.Toggled+=(_,_)=>{action(toggle.IsOn);App.Instance.Save();};Grid.SetColumn(toggle,1);row.Children.Add(toggle);_page.Children.Add(row);}
    private void Range(string title,double value,double min,double max,double step,Action<double> action,bool percent=false){var group=Ui.Stack(8);var name=Ui.Text(title,16);name.FontWeight=Microsoft.UI.Text.FontWeights.SemiBold;group.Children.Add(name);var row=new Grid{ColumnSpacing=12};row.ColumnDefinitions.Add(new(){Width=new GridLength(1,GridUnitType.Star)});row.ColumnDefinitions.Add(new(){Width=GridLength.Auto});var number=Ui.Text(percent?$"{value*100:0}%":$"{value:0.#}",12);number.MinWidth=45;number.TextWrapping=TextWrapping.NoWrap;Grid.SetColumn(number,1);row.Children.Add(number);var slider=new Slider{Minimum=min,Maximum=max,StepFrequency=step,Value=value};Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(slider,title);slider.ValueChanged+=(_,_)=>{number.Text=percent?$"{slider.Value*100:0}%":$"{slider.Value:0.#}";action(slider.Value);App.Instance.Save();};row.Children.Add(slider);group.Children.Add(row);_page.Children.Add(group);}
    private void Choice(string title,string[] labels,int selected,Action<int> action){var group=Ui.Stack(9);var name=Ui.Text(title,16);name.FontWeight=Microsoft.UI.Text.FontWeights.SemiBold;group.Children.Add(name);var row=new Grid{Padding=new Thickness(3),CornerRadius=new CornerRadius(17),Background=Ui.Brush(ColorHelper.FromArgb(24,145,154,180))};var buttons=new List<Button>();for(int i=0;i<labels.Length;i++){int index=i;row.ColumnDefinitions.Add(new(){Width=new GridLength(1,GridUnitType.Star)});var button=new Button{Content=Ui.Text(labels[i],13),FontSize=13,Padding=new Thickness(6,4,6,4),MinHeight=28,HorizontalAlignment=HorizontalAlignment.Stretch,BorderThickness=new Thickness(0),CornerRadius=new CornerRadius(14),Background=i==selected?Ui.AccentBrush(65):Ui.Brush(Colors.Transparent)};button.Click+=(_,_)=>{foreach(var b in buttons)b.Background=Ui.Brush(Colors.Transparent);button.Background=Ui.AccentBrush(65);action(index);App.Instance.Save();};buttons.Add(button);Grid.SetColumn(button,i);row.Children.Add(button);}group.Children.Add(row);_page.Children.Add(group);}
    private void Action(string title,System.Action action){var button=new Button{Content=Ui.Text(title),HorizontalAlignment=HorizontalAlignment.Left};button.Click+=(_,_)=>action();_page.Children.Add(button);}
    private void Render(string page)
    {
        _selected=page;_previewMusic=null;_page.Children.Clear();foreach(var b in _navButtons)b.Background=(string)b.Tag==page?Ui.AccentBrush(60):Ui.Brush(Colors.Transparent);var a=App.Instance;var p=a.Prefs;
        switch(page){
        case "外观":
            Heading("外观与光效","让界面通透起来，让音乐染上颜色。");Choice("主题",["跟随系统","浅色","深色"],Array.IndexOf(new[]{"System","Light","Dark"},p.Theme),i=>p.Theme=new[]{"System","Light","Dark"}[i]);Range("玻璃密度",p.GlassOpacity,.35,.95,.01,v=>p.GlassOpacity=v,true);Separator();Toggle("封面着色","根据当前播放的封面，在界面上融入柔和的色彩",p.CoverGlow,v=>p.CoverGlow=v);Separator();Toggle("动态光效","随着音乐播放呈现轻柔的流动光效",p.Animations,v=>p.Animations=v);AddPreview();Range("着色浓度",p.Glow,.08,.55,.01,v=>p.Glow=v,true);AddFontPicker("中文字体",p.ChineseFontFamily,v=>p.ChineseFontFamily=v);AddFontPicker("西文字体",p.WesternFontFamily,v=>p.WesternFontFamily=v);_page.Children.Add(Ui.Text("微光 LumiBar · −20° · 100%",18));break;
        case "常规":Heading("常规","每次打开，都保持熟悉的样子。");Toggle("登录后启动","原型只演示开关，不修改 Windows 启动项",p.AutoStart,v=>p.AutoStart=v);Separator();Toggle("关闭后留在托盘","原型窗口可从预览控制台重新打开",p.CloseToTray,v=>p.CloseToTray=v);Action("显示全部预览",()=>{a.OpenWeather();a.OpenMusic();a.OpenSettings();});_page.Children.Add(Ui.Text("关闭原型控制台即可退出全部预览。",13,.75));break;
        case "任务栏":Heading("任务栏","音乐在左，天气在右。");Toggle("天气胶囊","保留天气图标与温度",p.ShowWeather,v=>p.ShowWeather=v);Toggle("音乐胶囊","暂停保留，关闭会话后淡出",p.ShowMusic,v=>p.ShowMusic=v);Range("音乐胶囊宽度",p.MusicWidth,180,340,2,v=>p.MusicWidth=(int)v);Range("胶囊间距",p.Gap,4,24,1,v=>p.Gap=(int)v);Action("切换双 Lite 预览",a.ToggleLite);_page.Children.Add(Ui.Text("此处展示独立胶囊，不占用真实任务栏。",12,.75));break;
        case "天气":Heading("天气","预报、空气与一点阳光。");Choice("温度单位",["摄氏度 °C","华氏度 °F"],p.Fahrenheit?1:0,i=>{p.Fahrenheit=i==1;a.WeatherWindow.Render();});Choice("更新间隔",["10 分钟","20 分钟","30 分钟"],1,i=>p.WeatherRefreshMinutes=new[]{10,20,30}[i]);Action("切换天气示例",a.CycleWeather);Action("打开天气浮窗",a.OpenWeather);_page.Children.Add(Ui.Text("原型天气和降水均为示例。正式数据稍后接入。",13,.75));break;
        case "音乐与歌词":Heading("音乐与歌词","以歌词为主角，让控制触手可及。");Toggle("背景频谱","原型使用演示动画，不采集系统声音",p.Spectrum,v=>p.Spectrum=v);Range("频谱响应",p.Sensitivity,.5,2.5,.1,v=>p.Sensitivity=v);Range("歌词字号",p.LyricsFontSize,18,36,1,v=>{p.LyricsFontSize=(int)v;a.MusicWindow.RenderLyrics();});Range("歌词时间微调",p.LyricsOffset,-60,60,.5,v=>p.LyricsOffset=v);Action("打开音乐浮窗",a.OpenMusic);break;
        case "插件":Heading("插件","让界面拥有更多可能。");Toggle("天气与音乐","内置组件预览",true,_=>{});Separator();_page.Children.Add(Ui.Text("尚未添加其他插件",18));_page.Children.Add(Ui.Text("插件安装、后端通信和独立浮窗接管会在界面确认后接入。",13,.75));break;
        }
    }
    private void AddPreview()
    {
        var group=Ui.Stack(12);var label=Ui.Text("效果预览",16);label.FontWeight=Microsoft.UI.Text.FontWeights.SemiBold;group.Children.Add(label);
        var surface=new Grid{Height=82,CornerRadius=new CornerRadius(20),Padding=new Thickness(10,10,10,10),ColumnSpacing=6,Background=Ui.Brush(ColorHelper.FromArgb(18,138,142,185))};Ui.ClipRounded(surface,20);surface.ColumnDefinitions.Add(new(){Width=new GridLength(1,GridUnitType.Star)});surface.ColumnDefinitions.Add(new(){Width=GridLength.Auto});
        _previewMusic=new MusicCapsuleContent(true);surface.Children.Add(_previewMusic);var weather=Ui.Row(4);weather.Children.Add(new WeatherGlyph(0,true,26));weather.Children.Add(Ui.Text("12°",15));var button=new Button{Content=weather,CornerRadius=new CornerRadius(20),BorderThickness=new Thickness(0),Padding=new Thickness(7,4,7,4),Background=Ui.Brush(ColorHelper.FromArgb(30,255,255,255)),VerticalAlignment=VerticalAlignment.Center};button.Click+=(_,_)=>App.Instance.OpenWeather();Grid.SetColumn(button,1);surface.Children.Add(button);group.Children.Add(surface);_page.Children.Add(group);
    }
    internal void ApplyPreview()=>_previewMusic?.Refresh();
    internal void UpdatePreview(){if(IsOpen)_previewMusic?.UpdateSpectrum();}
    private void AddFontPicker(string title,string? selected,Action<string?> update)
    {
        var group=Ui.Stack(8);group.Children.Add(Ui.Text(title,16));var input=new AutoSuggestBox{PlaceholderText="搜索电脑上的字体",Text=selected??""};Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(input,title);InstalledFont[] fonts=[];
        void Suggest(){var query=input.Text.Trim();var matches=fonts.Where(f=>f.DisplayName.Contains(query,StringComparison.OrdinalIgnoreCase)||f.Family.Contains(query,StringComparison.OrdinalIgnoreCase)).Take(80).Select(f=>f.Family).ToArray();input.ItemsSource=matches;input.IsSuggestionListOpen=query.Length>0&&matches.Length>0;}
        input.Loaded+=async(_,_)=>{fonts=await App.Instance.Fonts.Get();};
        input.TextChanged+=(_,e)=>{if(e.Reason!=AutoSuggestionBoxTextChangeReason.SuggestionChosen)Suggest();};
        void Apply(string family){update(family);App.Instance.Save();input.IsSuggestionListOpen=false;}
        input.SuggestionChosen+=(_,e)=>{if(e.SelectedItem is string family)Apply(family);};
        input.QuerySubmitted+=async(_,e)=>{if(await App.Instance.Fonts.Resolve(e.QueryText) is {} family)Apply(family);};
        group.Children.Add(input);var reset=new Button{Content=Ui.Text("恢复系统默认",12),HorizontalAlignment=HorizontalAlignment.Left};reset.Click+=(_,_)=>{update(null);input.Text="";input.IsSuggestionListOpen=false;App.Instance.Save();};group.Children.Add(reset);_page.Children.Add(group);
    }
}
