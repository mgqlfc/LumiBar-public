using LumiBar.Extensibility;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Storage.Streams;
using System.Text.Json;
namespace LumiBar;
internal sealed partial class SettingsWindow:SurfaceWindow
{
    private readonly List<AutoSuggestBox> _fontPickers=[];
    private ScrollViewer _pageScroll=null!;
    private Microsoft.UI.Dispatching.DispatcherQueueTimer? _rangeSaveTimer;
    private bool _rangeSavePending;
    private int _pageRevision,_pageSyncDepth;
    private Preferences? _renderedPreferences;
    private bool _diagnosticBusy;
    private readonly StackPanel _page=Ui.Stack(22),_nav=Ui.Stack(6);private readonly List<Button> _navButtons=[];private WeatherGlyph? _previewWeatherGlyph;private TextBlock? _previewWeatherTemperature;private MusicCapsuleContent? _previewMusic;private string _selected="外观";
    internal SettingsWindow():base("LumiBar · 设置")
    {
        Body.RowDefinitions.Add(new(){Height=new GridLength(54)});Body.RowDefinitions.Add(new(){Height=new GridLength(1,GridUnitType.Star)});
        var header=new Grid{Padding=new Thickness(20,8,12,8),BorderThickness=new Thickness(0,0,0,1),BorderBrush=Ui.Brush(ColorHelper.FromArgb(24,128,128,128))};var brand=Ui.Row(10);var mark=new Border{Width=26,Height=26,CornerRadius=new CornerRadius(8),Background=new LinearGradientBrush{StartPoint=new(0,0),EndPoint=new(1,1),GradientStops={new GradientStop{Color=ColorHelper.FromArgb(255,184,170,255)},new GradientStop{Color=ColorHelper.FromArgb(255,115,110,218),Offset=1}}},Child=new Border{Width=15,Height=11,CornerRadius=new CornerRadius(5),Background=Ui.Brush(ColorHelper.FromArgb(180,255,255,255)),Margin=new Thickness(-3,-3,0,0)}};brand.Children.Add(mark);var name=Ui.Text("LumiBar",21);name.FontWeight=Microsoft.UI.Text.FontWeights.SemiBold;brand.Children.Add(name);header.Children.Add(brand);var close=Ui.IconButton("\uE8BB","关闭设置",(_,_)=>{if(App.Instance.Prefs.CloseToTray)HideSurface();else App.Instance.Quit();});close.HorizontalAlignment=HorizontalAlignment.Right;header.Children.Add(close);Body.Children.Add(header);
        var layout=new Grid();layout.ColumnDefinitions.Add(new(){Width=new GridLength(170)});layout.ColumnDefinitions.Add(new(){Width=new GridLength(1,GridUnitType.Star)});Grid.SetRow(layout,1);Body.Children.Add(layout);_nav.Padding=new Thickness(14,24,12,0);layout.Children.Add(_nav);
        foreach(var (label,icon) in new[]{("常规","\uE713"),("外观","\uE70F"),("任务栏","\uE737"),("天气","\uE706"),("音乐与歌词","\uE8D6"),("插件","\uEA86"),("关于","\uE946")}){var row=Ui.Row(12);row.Children.Add(Ui.Icon(icon,18));row.Children.Add(Ui.Text(label,15));var button=new Button{Content=row,Tag=label,HorizontalAlignment=HorizontalAlignment.Stretch,HorizontalContentAlignment=HorizontalAlignment.Left,Height=42,Padding=new Thickness(12,0,8,0),CornerRadius=new CornerRadius(9),BorderThickness=new Thickness(0)};button.Click+=(_,_)=>Render(label);_navButtons.Add(button);_nav.Children.Add(button);}
        layout.Children.Remove(_nav);layout.Children.Add(new ScrollViewer{Content=_nav,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled,VerticalScrollBarVisibility=ScrollBarVisibility.Auto});_nav.Children.Add(_pluginNavigation);
        var scroll=_pageScroll=new ScrollViewer{Content=_page,Padding=new Thickness(22,24,24,22),HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled};Grid.SetColumn(scroll,1);layout.Children.Add(scroll);Root.AddHandler(UIElement.PointerPressedEvent,new Microsoft.UI.Xaml.Input.PointerEventHandler(ClearFontFocus),true);Render("外观");SurfaceVisibilityChanged+=open=>{_previewMusic?.SetActive(open);if(open)FocusSelectedNavigation();else FlushPendingSettings();};
        Activated+=(_,e)=>{if(e.WindowActivationState==WindowActivationState.Deactivated)FlushPendingSettings();};
        AppWindow.Closing+=(_,_)=>FlushPendingSettings();
    }
    internal void ShowQuickMenu(){switch(Native.TrayMenu(this)){case 1:App.Instance.OpenWeather();break;case 2:App.Instance.OpenMusic();break;case 3:App.Instance.OpenSettings();break;case 4:App.Instance.Quit();break;}}
    private void Heading(string title,string description){var group=Ui.Stack(7);var text=Ui.Text(title,28);text.FontWeight=Microsoft.UI.Text.FontWeights.SemiBold;group.Children.Add(text);group.Children.Add(Ui.Text(description,13,.8));_page.Children.Add(group);}
    private void Separator()=>_page.Children.Add(new Border{Height=1,Background=Ui.Brush(ColorHelper.FromArgb(25,128,128,128))});
    private bool CanCommitControl(FrameworkElement control,int revision)=>revision==_pageRevision&&_pageSyncDepth==0&&IsOpen&&control.IsLoaded;
    internal void PrepareToShow()
    {
        if(IsOpen)return;
        if(_renderedPreferences!=null&&!PreferenceChanges.Between(_renderedPreferences,App.Instance.Prefs).Any)return;
        var scroll=_pageScroll.VerticalOffset;Render(_selected);_pageScroll.ChangeView(null,scroll,null,true);
    }
    private void Toggle(string title,string detail,bool value,Action<bool> action)
    {
        var revision=_pageRevision;var row=new Grid{ColumnSpacing=14};row.ColumnDefinitions.Add(new(){Width=new GridLength(1,GridUnitType.Star)});row.ColumnDefinitions.Add(new(){Width=GridLength.Auto});var words=Ui.Stack(5);var label=Ui.Text(title,16);label.FontWeight=Microsoft.UI.Text.FontWeights.SemiBold;words.Children.Add(label);words.Children.Add(Ui.Text(detail,12,.75));row.Children.Add(words);var toggle=new ToggleSwitch{IsOn=value,OnContent="",OffContent="",MinWidth=0,VerticalAlignment=VerticalAlignment.Center};Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(toggle,title);
        toggle.Toggled+=(_,_)=>{if(!CanCommitControl(toggle,revision))return;action(toggle.IsOn);App.Instance.Save();};Grid.SetColumn(toggle,1);row.Children.Add(toggle);_page.Children.Add(row);
    }
    private void Range(string title,double value,double min,double max,double step,Action<double> action,bool percent=false,System.Action? preview=null)
    {
        var revision=_pageRevision;var group=Ui.Stack(8);var name=Ui.Text(title,16);name.FontWeight=Microsoft.UI.Text.FontWeights.SemiBold;group.Children.Add(name);var row=new Grid{ColumnSpacing=12};row.ColumnDefinitions.Add(new(){Width=new GridLength(1,GridUnitType.Star)});row.ColumnDefinitions.Add(new(){Width=GridLength.Auto});var number=Ui.Text(percent?$"{value*100:0}%":$"{value:0.#}",12);number.MinWidth=45;number.TextWrapping=TextWrapping.NoWrap;Grid.SetColumn(number,1);row.Children.Add(number);var slider=new Slider{Minimum=min,Maximum=max,StepFrequency=step,Value=value};Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(slider,title);
        slider.ValueChanged+=(_,e)=>{if(!CanCommitControl(slider,revision)||Math.Abs(e.NewValue-e.OldValue)<1e-8)return;GlassDiagnostics.Slider(title,e.OldValue,e.NewValue);number.Text=percent?$"{slider.Value*100:0}%":$"{slider.Value:0.#}";action(slider.Value);QueueRangeSave();if(preview!=null)preview();else App.Instance.PreviewSettings();};slider.LostFocus+=(_,_)=>FlushPendingSettings();slider.AddHandler(UIElement.PointerReleasedEvent,new Microsoft.UI.Xaml.Input.PointerEventHandler((_,_)=>FlushPendingSettings()),true);row.Children.Add(slider);group.Children.Add(row);_page.Children.Add(group);
    }
    private void QueueRangeSave()
    {
        _rangeSavePending=true;
        if(_rangeSaveTimer==null){_rangeSaveTimer=DispatcherQueue.CreateTimer();_rangeSaveTimer.Interval=TimeSpan.FromMilliseconds(250);_rangeSaveTimer.IsRepeating=false;_rangeSaveTimer.Tick+=(_,_)=>FlushPendingSettings();}
        _rangeSaveTimer.Stop();_rangeSaveTimer.Start();
    }
    internal void FlushPendingSettings()
    {
        _rangeSaveTimer?.Stop();if(!_rangeSavePending)return;
        _rangeSavePending=false;App.Instance.PersistSettings();
    }
    private void Choice(string title,string[] labels,int selected,Action<int> action){var group=Ui.Stack(9);var name=Ui.Text(title,16);name.FontWeight=Microsoft.UI.Text.FontWeights.SemiBold;group.Children.Add(name);var row=new Grid{Padding=new Thickness(3),CornerRadius=new CornerRadius(17),Background=Ui.Brush(ColorHelper.FromArgb(24,145,154,180))};var buttons=new List<Button>();for(int i=0;i<labels.Length;i++){int index=i;row.ColumnDefinitions.Add(new(){Width=new GridLength(1,GridUnitType.Star)});var button=new Button{Content=Ui.Text(labels[i],13),FontSize=13,Padding=new Thickness(6,4,6,4),MinHeight=28,HorizontalAlignment=HorizontalAlignment.Stretch,BorderThickness=new Thickness(0),CornerRadius=new CornerRadius(14)};Ui.SelectionState(button,i==selected);button.Click+=(_,_)=>{foreach(var b in buttons)Ui.SelectionState(b,b==button);action(index);App.Instance.Save();};buttons.Add(button);Grid.SetColumn(button,i);row.Children.Add(button);}group.Children.Add(row);_page.Children.Add(group);}
    private void Action(string title,System.Action action){var button=new Button{Content=Ui.Text(title),HorizontalAlignment=HorizontalAlignment.Left};button.Click+=(_,_)=>action();_page.Children.Add(button);}
    private static void NavigationState(Button button,bool selected)
    {
        Ui.SelectionState(button,selected);
    }
    private void Render(string page)
    {
        FlushPendingSettings();
        ++_pageRevision;++_pageSyncDepth;
        try{
        _accentOptions.Clear();_accentName=null;_selected=page;_fontPickers.Clear();_pageScroll.ChangeView(null,0,null,true);_previewWeatherGlyph=null;_previewWeatherTemperature=null;_previewMusic=null;_page.Children.Clear();foreach(var b in _navButtons)NavigationState(b,(string)b.Tag==page);var a=App.Instance;var p=a.Prefs;
        switch(page){
        case "外观":
            Heading("外观与光效","让界面通透起来，让音乐染上颜色。");Choice("主题",["跟随系统","浅色","深色"],Array.IndexOf(new[]{"System","Light","Dark"},p.Theme),i=>p.Theme=new[]{"System","Light","Dark"}[i]);AddAccentPicker();Range("玻璃密度",p.GlassOpacity,.35,.95,.01,v=>p.GlassOpacity=v,true,a.PreviewGlass);Separator();Toggle("封面着色","根据当前播放的封面，在界面上融入柔和的色彩",p.CoverGlow,v=>p.CoverGlow=v);Separator();Toggle("动态光效","随着音乐播放呈现轻柔的流动光效",p.Animations,v=>p.Animations=v);AddPreview();Range("着色浓度",p.Glow,.08,.55,.01,v=>p.Glow=v,true);Range("封面饱和度",p.CoverSaturation,0,1,.01,v=>p.CoverSaturation=v,true);AddFontPicker("中文字体",p.ChineseFontFamily,v=>p.ChineseFontFamily=v);AddFontPicker("西文字体",p.WesternFontFamily,v=>p.WesternFontFamily=v);break;
        case "常规":
            Heading("常规","轻轻留在任务栏，随时从系统托盘找到它。");
            using(var key=Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run"))p.AutoStart=key?.GetValue("LumiBar")!=null;
            Toggle("登录后启动","使用当前用户的 Windows 启动项",p.AutoStart,v=>{using var key=Microsoft.Win32.Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run");if(v)key.SetValue("LumiBar",$"\"{Environment.ProcessPath}\"");else key.DeleteValue("LumiBar",false);p.AutoStart=v;});
            Toggle("关闭设置后留在托盘","详情浮窗关闭后只收起浮窗",p.CloseToTray,v=>p.CloseToTray=v);
            Action("打开数据目录",()=>{Directory.CreateDirectory(Preferences.DataPath);_=Windows.System.Launcher.LaunchFolderPathAsync(Preferences.DataPath);});
            AddDiagnosticExport();
            Action("退出 LumiBar",a.Quit);break;
        case "任务栏":
            Heading("任务栏","音乐在左，天气在右。");Toggle("天气胶囊","保留天气图标与温度",p.ShowWeather,v=>p.ShowWeather=v);Toggle("音乐胶囊","暂停保留，关闭会话后淡出",p.ShowMusic,v=>p.ShowMusic=v);
            Action("Windows 任务栏设置",()=>_=Windows.System.Launcher.LaunchUriAsync(new Uri("ms-settings:taskbar")));
            Toggle("全屏时隐藏","退出全屏后恢复胶囊",p.HideInFullscreen,v=>p.HideInFullscreen=v);
            Range("音乐胶囊宽度",p.MusicWidth,180,340,2,v=>p.MusicWidth=(int)v);Range("胶囊间距",p.Gap,4,24,1,v=>p.Gap=(int)v);
            _page.Children.Add(Ui.Text("任务栏拥挤时，自动切换精简样式，保留天气和音乐入口。",12,.75));
            _page.Children.Add(Ui.Text(a.Reservation?.Snapshot.Reserved==true?"任务栏布局已就绪":"正在适配任务栏布局…",12,.75));break;
        case "天气":
            Heading("天气","预报、空气与一点阳光。");Toggle("自动定位城市","使用 Windows 定位，手动切换城市后暂停自动定位",p.AutoLocateCity,v=>{if(v)_=a.UseSystemLocation();else a.DisableAutoLocation();});
            Choice("天气来源",["和风天气","备用来源"],p.WeatherProvider=="Public"?1:0,i=>{p.WeatherProvider=i==1?"Public":"Auto";a.ReloadWeatherProvider();});
            AddWeatherProxySettings();
            Choice("温度单位",["摄氏度 °C","华氏度 °F"],p.Fahrenheit?1:0,i=>{p.Fahrenheit=i==1;a.WeatherWindow.Render();});
            Choice("更新间隔",["10 分钟","20 分钟","30 分钟"],Math.Max(0,Array.IndexOf(new[]{10,20,30},p.WeatherRefreshMinutes)),i=>p.WeatherRefreshMinutes=new[]{10,20,30}[i]);
            Choice("风速单位",["千米 / 小时","米 / 秒"],p.WindMetersPerSecond?1:0,i=>{p.WindMetersPerSecond=i==1;a.WeatherWindow.Render();});
            Action("立即更新天气",()=>_=a.RefreshWeather());Action("选择城市 / 自动定位",a.OpenWeather);
            foreach(var city in p.Favorites.ToArray())Action($"移除收藏 · {city.Name}",()=>{a.RemoveSavedCity(city);Render(page);});
            _page.Children.Add(Ui.Text("天气更新失败时，自动尝试备用来源。\n自动定位跟随 Windows 定位。",12,.75));break;
        case "音乐与歌词":
            Heading("音乐与歌词","以歌词为主角，让控制触手可及。");Toggle("背景频谱","随系统输出声音变化，不读取麦克风",p.Spectrum,v=>p.Spectrum=v);
            Toggle("自动查找歌词","用歌曲信息匹配 LRCLIB 同步歌词",p.AutoLyrics,v=>{p.AutoLyrics=v;_=a.LoadLyrics();});
            Range("频谱响应",p.Sensitivity,.5,2.5,.1,v=>p.Sensitivity=v);Range("歌词字号",p.LyricsFontSize,18,36,1,v=>{p.LyricsFontSize=(int)v;a.MusicWindow.RenderLyrics();});Action("打开音乐浮窗",a.OpenMusic);
            _page.Children.Add(Ui.Text("播放器需开启系统媒体控制；浏览器支持情况取决于播放网站。",12,.75));break;
        case "插件":
            Heading("插件","让界面拥有更多可能。");_page.Children.Add(Ui.Text("天气与音乐 · 内置组件",18));Separator();
            _page.Children.Add(Ui.Text("查看已安装的扩展，选择喜欢的任务栏界面。",13,.75));
            _page.Children.Add(Ui.Text(a.ExternalRendererActive?$"当前界面 · {a.RendererLeases.Current!.RendererId}":"当前界面 · LumiBar 内置",16));
            Action("恢复内置界面",()=>{a.RestoreBuiltinRenderer();Render("插件");});
            Action("刷新列表",()=>Render("插件"));
            foreach(var frontend in ExternalFrontendCatalog.Scan())
            {
                _page.Children.Add(Ui.Text($"{frontend.Name} · {frontend.Version}",15));
                if(frontend.Error!=null)_page.Children.Add(Ui.Text(frontend.Error,12,.7));
                else {var id=frontend.Id;Action("打开界面",()=>{try{ExternalFrontendCatalog.Launch(id,Path.Combine(Preferences.DataPath,"frontend-endpoint.json"));}catch(Exception ex){_page.Children.Add(Ui.Text(ex.Message,12,.8));}});}
            }
            var entries=PluginCatalog.Scan();foreach(var (manifest,path,error) in entries)_page.Children.Add(Ui.Text(error??$"{manifest!.Name} · {manifest.Version}",14));
            if(entries.Count==0)_page.Children.Add(Ui.Text("尚未添加其他插件",16));
            _page.Children.Add(Ui.Text("目前支持独立界面扩展。",12,.75));Action("打开插件目录",()=>_=Windows.System.Launcher.LaunchFolderPathAsync(PluginCatalog.DirectoryPath));break;
        case "关于":
            Heading("LumiBar","天气、音乐与歌词，留在任务栏。");
            _page.Children.Add(Ui.Text($"版本 {typeof(App).Assembly.GetName().Version?.ToString(3)}",13,.75));
            Separator();
            AboutItem("制作人","Ma Guanqun with Codex");
            AboutItem("前端",a.ExternalRendererActive?$"外部前端 · {a.RendererLeases.Current!.RendererId}":"内置 WinUI 3 · Windows App SDK 1.8");
            AboutItem("后端","C# · .NET 8\nC++ 原生任务栏组件 · 本地命名管道");
            AboutItem("天气与空气","和风天气（云端代理）· Open-Meteo · MET Norway\n空气质量按数据源标注 CN AQI / US AQI · 定位：Windows 系统定位");
            AboutItem("音乐与歌词","Windows 系统媒体会话 · WASAPI 音频频谱\nLRCLIB 同步歌词 · 本地 LRC");
            AboutItem("默认字体",$"中文：{FontDefaults.ChineseDisplayName} · 西文：{FontDefaults.Western}\n字体随应用提供，也可选用电脑上的字体");break;
        default:if(page.StartsWith(PluginPagePrefix,StringComparison.Ordinal))RenderPluginMenuPage(page);break;
        }
        _renderedPreferences=JsonSerializer.Deserialize<Preferences>(JsonSerializer.Serialize(p));
        if(IsOpen)FocusSelectedNavigation();
        }finally{
            // WinUI can dispatch initialization ValueChanged/Toggled callbacks
            // after construction. Drain normal work before accepting edits.
            if(!DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low,()=>{if(_pageSyncDepth>0)--_pageSyncDepth;}))--_pageSyncDepth;
        }
    }
    private void AboutItem(string title,string value){var group=Ui.Stack(6);var label=Ui.Text(title,12,.7);group.Children.Add(label);var text=Ui.Text(value,14);group.Children.Add(text);_page.Children.Add(group);}
    private void AddDiagnosticExport()
    {
        var group=Ui.Stack(7);var button=new Button{Content="导出诊断",HorizontalAlignment=HorizontalAlignment.Left,IsEnabled=!_diagnosticBusy};
        var status=Ui.Text("将运行状态和最近日志保存到你选择的位置。",12,.75);group.Children.Add(button);group.Children.Add(status);_page.Children.Add(group);
        button.Click+=async(_,_)=>{
            if(_diagnosticBusy)return;_diagnosticBusy=true;button.IsEnabled=false;
            var previousKeepOpen=KeepOpen;KeepOpen=true;
            try{
                FlushPendingSettings();var picker=new Windows.Storage.Pickers.FileSavePicker{SuggestedFileName=$"LumiBar-诊断-{DateTime.Now:yyyyMMdd-HHmmss}",SuggestedStartLocation=Windows.Storage.Pickers.PickerLocationId.Desktop};
                picker.FileTypeChoices.Add("诊断压缩包",new List<string>{".zip"});WinRT.Interop.InitializeWithWindow.Initialize(picker,WinRT.Interop.WindowNative.GetWindowHandle(this));
                var file=await picker.PickSaveFileAsync();if(file==null){status.Text="已取消导出。";return;}
                status.Text="正在整理诊断…";var snapshot=Diagnostics.Capture(App.Instance);await Diagnostics.Export(file.Path,snapshot);
                status.Text=$"已保存：{file.Path}";
            }catch(Exception ex){App.Log("diagnostic-export",ex);status.Text="导出失败，请检查保存位置后重试。";}
            finally{KeepOpen=previousKeepOpen;_diagnosticBusy=false;button.IsEnabled=true;}
        };
    }
    private void AddWeatherProxySettings()
    {
        var app=App.Instance;var group=Ui.Stack(7);group.Children.Add(Ui.Text("天气服务地址",14));
        var input=new TextBox{Text=app.Prefs.WeatherProxyUrl,PlaceholderText="https://服务器/lumibar-weather/"};Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(input,"天气服务地址");group.Children.Add(input);
        var status=Ui.Text("使用默认天气服务，获取预报与预警。",11,.7);var apply=new Button{Content="保存并连接",HorizontalAlignment=HorizontalAlignment.Left};var reset=new Button{Content="恢复默认",HorizontalAlignment=HorizontalAlignment.Left};
        async Task Connect(string? url){if(url==null){status.Text="请检查服务地址。";return;}apply.IsEnabled=reset.IsEnabled=false;app.Prefs.WeatherProxyUrl=url;app.Save();status.Text="正在连接…";try{using var response=await WeatherService.Http.GetAsync(url+"healthz");using var doc=System.Text.Json.JsonDocument.Parse(await response.Content.ReadAsStringAsync());status.Text=response.IsSuccessStatusCode&&doc.RootElement.TryGetProperty("configured",out var configured)&&configured.ValueKind==System.Text.Json.JsonValueKind.True?"连接成功 · 和风天气":"已连接，天气暂不可用。";app.ReloadWeatherProvider();}catch{status.Text="暂时无法连接，自动使用备用来源。";}finally{apply.IsEnabled=reset.IsEnabled=true;}}
        apply.Click+=async(_,_)=>await Connect(WeatherProxyAddress.Normalize(input.Text));
        reset.Click+=async(_,_)=>{input.Text=WeatherProxyAddress.Default;await Connect(WeatherProxyAddress.Default);};
        var actions=Ui.Row(8);actions.Children.Add(apply);actions.Children.Add(reset);group.Children.Add(actions);group.Children.Add(status);_page.Children.Add(group);
    }
    private void AddPreview()
    {
        var group=Ui.Stack(12);var label=Ui.Text("效果预览",16);label.FontWeight=Microsoft.UI.Text.FontWeights.SemiBold;group.Children.Add(label);
        var surface=new Grid{Height=82,CornerRadius=new CornerRadius(20),Padding=new Thickness(10,10,10,10),ColumnSpacing=6,Background=Ui.Brush(ColorHelper.FromArgb(18,138,142,185))};Ui.ClipRounded(surface,20);surface.ColumnDefinitions.Add(new(){Width=new GridLength(1,GridUnitType.Star)});surface.ColumnDefinitions.Add(new(){Width=GridLength.Auto});
        _previewMusic=new MusicCapsuleContent(true);surface.Children.Add(_previewMusic);var weather=Ui.Row(4);_previewWeatherGlyph=new WeatherGlyph(App.Instance.Forecast?.Code??0,App.Instance.Forecast?.IsDay,26);_previewWeatherTemperature=Ui.Text(App.Instance.Forecast is {} forecast?App.Instance.Temperature(forecast.Temperature):"—",15);weather.Children.Add(_previewWeatherGlyph);weather.Children.Add(_previewWeatherTemperature);var button=new Button{Content=weather,CornerRadius=new CornerRadius(20),BorderThickness=new Thickness(0),Padding=new Thickness(7,4,7,4),Background=Ui.Brush(ColorHelper.FromArgb(30,255,255,255)),VerticalAlignment=VerticalAlignment.Center};button.Click+=(_,_)=>App.Instance.OpenWeather();Grid.SetColumn(button,1);surface.Children.Add(button);group.Children.Add(surface);_page.Children.Add(group);
    }
    internal void ApplyPreview(){_previewMusic?.Refresh();var f=App.Instance.Forecast;_previewWeatherGlyph?.Update(f?.Code??0,f?.IsDay);if(_previewWeatherTemperature!=null)_previewWeatherTemperature.Text=f==null?"—":App.Instance.Temperature(f.Temperature);}
    internal void UpdatePreview(){if(IsOpen)_previewMusic?.UpdateSpectrum();}
    private void AddFontPicker(string title,string? selected,Action<string?> update)
    {
        var defaultName=title=="中文字体"?FontDefaults.ChineseDisplayName:FontDefaults.Western;
        var group=Ui.Stack(8);group.Children.Add(Ui.Text(title,16));var input=new AutoSuggestBox{PlaceholderText=$"默认：{defaultName}（内置），也可搜索电脑字体",Text=selected??""};Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(input,title);InstalledFont[] fonts=[];
        void Suggest(){var query=input.Text.Trim();var matches=fonts.Where(f=>f.DisplayName.Contains(query,StringComparison.OrdinalIgnoreCase)||f.Family.Contains(query,StringComparison.OrdinalIgnoreCase)).Take(80).Select(f=>f.Family).ToArray();input.ItemsSource=matches;input.IsSuggestionListOpen=query.Length>0&&matches.Length>0;}
        input.Loaded+=async(_,_)=>{fonts=await App.Instance.Fonts.Get();};
        _fontPickers.Add(input);
        input.TextChanged+=(_,e)=>{if(e.Reason==AutoSuggestionBoxTextChangeReason.UserInput)Suggest();};
        input.AddHandler(UIElement.PointerPressedEvent,new Microsoft.UI.Xaml.Input.PointerEventHandler((_,_)=>{if(fonts.Length>0)Suggest();}),true);
        input.KeyDown+=(_,e)=>{if(e.Key==Windows.System.VirtualKey.Down)Suggest();if(e.Key==Windows.System.VirtualKey.Escape)input.IsSuggestionListOpen=false;};
        void Apply(string family){update(family);App.Instance.Save();input.IsSuggestionListOpen=false;}
        input.SuggestionChosen+=(_,e)=>{if(e.SelectedItem is string family)Apply(family);};
        input.QuerySubmitted+=async(_,e)=>{if(await App.Instance.Fonts.Resolve(e.QueryText) is {} family)Apply(family);};
        group.Children.Add(input);var reset=new Button{Content=Ui.Text($"恢复默认：{defaultName}",12),HorizontalAlignment=HorizontalAlignment.Left};reset.Click+=(_,_)=>{update(null);input.Text="";input.IsSuggestionListOpen=false;App.Instance.Save();};group.Children.Add(reset);_page.Children.Add(group);
    }
    private void ClearFontFocus(object sender,Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
    {
        for(var node=e.OriginalSource as DependencyObject;node!=null&&node!=Root;node=VisualTreeHelper.GetParent(node))
            if(node is AutoSuggestBox or TextBox or Microsoft.UI.Xaml.Controls.Primitives.ButtonBase or Slider or ToggleSwitch or ComboBox or Microsoft.UI.Xaml.Controls.Primitives.ScrollBar)return;
        FocusSelectedNavigation();
    }
    private void FocusSelectedNavigation()
    {
        foreach(var picker in _fontPickers)picker.IsSuggestionListOpen=false;
        var selected=_navButtons.FirstOrDefault(b=>(string)b.Tag==_selected);
        if(selected?.Focus(FocusState.Programmatic)==true)return;
        // The first presentation may reach here before its visual tree loads.
        var revision=VisibilityRevision;var page=_selected;
        DispatcherQueue.TryEnqueue(()=>{if(IsOpen&&VisibilityRevision==revision&&_selected==page)selected?.Focus(FocusState.Programmatic);});
    }
}
