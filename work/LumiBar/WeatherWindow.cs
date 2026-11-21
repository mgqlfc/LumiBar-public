using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Windows.Foundation;

namespace LumiBar;
internal sealed class WeatherWindow : SurfaceWindow
{
    private readonly Grid _weatherLight=new(){Width=334,Height=244,HorizontalAlignment=HorizontalAlignment.Left,VerticalAlignment=VerticalAlignment.Top,IsHitTestVisible=false};
    private readonly WeatherAtmosphere _atmosphere;private FrameworkElement? _currentWeather;private AnimatedReveal? _dailyReveal;
    private readonly AnimatedReveal _searchReveal;private bool _renderPending,_rendering;private TextBlock? _status;private Button? _refresh;
    private readonly StackPanel _content=Ui.Stack(8);private readonly StackPanel _searchArea=Ui.Stack(10);private readonly TextBox _search=new(){PlaceholderText="搜索城市，例如：天津 / Tianjin"};private readonly StackPanel _results=Ui.Stack(6);private readonly TextBlock _searchStatus=Ui.Text("",12,.7);private int _searchVersion;
    internal WeatherWindow():base("LumiBar · 天气")
    {
        // Atmospheric colour belongs to the glass surface, never to icon pixels.
        LightHost.Children.Add(_weatherLight);_atmosphere=new WeatherAtmosphere(_weatherLight,expanded:true);
        SurfaceVisibilityChanged+=open=>_atmosphere.SetActive(open);
        _searchReveal=new AnimatedReveal(_searchArea);_searchReveal.LayoutFrame+=ResizeContent;
        var scroll=new ScrollViewer{Content=_content,Padding=new Thickness(22,16,22,16),HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled,VerticalScrollBarVisibility=ScrollBarVisibility.Auto};scroll.BringIntoViewOnFocusChange=false;scroll.ViewChanged+=(_,_)=>PositionLight();Body.Children.Add(scroll);
        var searchRow=new Grid{ColumnSpacing=8};searchRow.ColumnDefinitions.Add(new(){Width=new GridLength(1,GridUnitType.Star)});searchRow.ColumnDefinitions.Add(new(){Width=GridLength.Auto});searchRow.Children.Add(_search);
        var go=new Button{Content="搜索"};go.Click+=async(_,_)=>await Search();Grid.SetColumn(go,1);searchRow.Children.Add(go);_search.KeyDown+=async(_,e)=>{if(e.Key==Windows.System.VirtualKey.Enter)await Search();};
        _searchArea.Children.Add(searchRow);_searchArea.Children.Add(_searchStatus);_searchArea.Children.Add(new ScrollViewer{Content=_results,MaxHeight=210,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled,VerticalScrollBarVisibility=ScrollBarVisibility.Auto});
    }
    private async Task Search(){var version=++_searchVersion;var query=_search.Text.Trim();if(query.Length<2){_results.Children.Clear();_searchStatus.Text="请输入至少两个字符";_searchReveal.SetExpanded(true,App.Instance.Prefs.Animations);return;}_searchStatus.Text="正在查找城市…";_results.Children.Clear();_searchReveal.SetExpanded(true,App.Instance.Prefs.Animations);try{var cities=await App.Instance.Weather.Search(query);if(version!=_searchVersion)return;_searchStatus.Text=cities.Count==0?"没有找到城市，试试完整名称或拼音":"请选择城市";foreach(var city in cities){var b=new Button{Content=$"添加 · {city.Name}  {city.Region}",HorizontalAlignment=HorizontalAlignment.Stretch,HorizontalContentAlignment=HorizontalAlignment.Left};b.Click+=(_,_)=>{ConfirmCity(city);};_results.Children.Add(b);}_searchReveal.SetExpanded(true,App.Instance.Prefs.Animations);}catch(Exception ex){if(version!=_searchVersion)return;_searchStatus.Text="城市搜索失败，请检查网络后重试";App.Log("city search",ex);_searchReveal.SetExpanded(true,App.Instance.Prefs.Animations);}}
    internal void Render()
    {
        if(_renderPending)return;_renderPending=true;
        DispatcherQueue.TryEnqueue(()=>{if(!_renderPending||IsRevealing)return;_renderPending=false;RenderCore();});
    }
    internal void UpdateStatus()
    {
        if(_status==null){Render();return;}
        _status.Text=WeatherHeaderText.Status(App.Instance.WeatherStatus);
        if(_refresh!=null)_refresh.IsEnabled=App.Instance.Prefs.City!=null&&!App.Instance.WeatherStatus.Contains("正在更新");
    }
    private void RenderCore()
    {
        var started=System.Diagnostics.Stopwatch.GetTimestamp();
        _rendering=true;var expanded=_dailyReveal?.Expanded??false;_dailyReveal?.Finish();
        try{BuildContent(expanded);}finally{_rendering=false;}
        ResizeContent();
        if(IsOpen&&_content.IsLoaded&&App.Instance.Prefs.Animations){var visual=Microsoft.UI.Xaml.Hosting.ElementCompositionPreview.GetElementVisual(_content);visual.StopAnimation("Opacity");visual.Opacity=.72f;Ui.Fade(_content,1,true,.18);}
        App.Trace("weather-ui",$"render {System.Diagnostics.Stopwatch.GetElapsedTime(started).TotalMilliseconds:F1}ms");
    }
    private void BuildContent(bool expanded)
    {
        var app=App.Instance;var f=app.Forecast;_currentWeather=null;_dailyReveal=null;_weatherLight.Visibility=Visibility.Collapsed;_content.Children.Clear();
        var header=Ui.Stack(5);
        var top=new Grid{ColumnSpacing=7};top.ColumnDefinitions.Add(new(){Width=new GridLength(1,GridUnitType.Star)});top.ColumnDefinitions.Add(new(){Width=GridLength.Auto});top.Children.Add(CitySwitcher());
        var tools=Ui.Row(2);
        var refresh=_refresh=Ui.IconButton("\uE72C","更新天气",async(sender,_)=>{if(sender is Button b)b.IsEnabled=false;await app.RefreshWeather();});if(refresh.Content is FontIcon refreshIcon)refreshIcon.FontSize=15;refresh.IsEnabled=app.Prefs.City!=null&&!app.WeatherStatus.Contains("正在更新");tools.Children.Add(refresh);
        tools.Children.Add(Ui.More(("选择城市",(_,_)=>ToggleSearch()),(KeepOpen?"取消固定":"固定浮窗",(_,_)=>{KeepOpen=!KeepOpen;Render();}),("设置",(_,_)=>app.OpenSettings()),("关闭",(_,_)=>HideSurface())));
        foreach(var button in tools.Children.OfType<Button>()){button.Width=button.Height=28;button.MinWidth=button.MinHeight=0;}
        Grid.SetColumn(tools,1);top.Children.Add(tools);header.Children.Add(top);
        var metadata=new Grid{ColumnSpacing=0};metadata.ColumnDefinitions.Add(new(){Width=new GridLength(1,GridUnitType.Star)});for(var i=0;i<3;i++)metadata.ColumnDefinitions.Add(new(){Width=GridLength.Auto});
        if(f!=null){var sourceLabel=Ui.Text(f.Source,11,.7);sourceLabel.MaxWidth=140;sourceLabel.TextWrapping=TextWrapping.NoWrap;sourceLabel.TextTrimming=TextTrimming.CharacterEllipsis;
            var sources=new Button{Content=sourceLabel,Background=Ui.Brush(Colors.Transparent),BorderThickness=new Thickness(0),Padding=new Thickness(0),MinHeight=18,VerticalAlignment=VerticalAlignment.Center};ToolTipService.SetToolTip(sources,"数据来源 · "+f.Source);Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(sources,"数据来源 · "+f.Source);Ui.SoftAccentHover(sources);
            sources.Click+=(_,_)=>{var details=Ui.Stack(8);details.Children.Add(Ui.Text(f.Source,16));foreach(var note in (f.Attributions??[]).Concat(f.ProviderNote??[]))details.Children.Add(Ui.Text(note,12,.8));new Flyout{Content=new ScrollViewer{Content=details,Width=300,MaxHeight=260,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled}}.ShowAt(sources);};Grid.SetColumn(sources,1);metadata.Children.Add(sources);
            var separator=Ui.Text("·",11,.7);separator.VerticalAlignment=VerticalAlignment.Center;Grid.SetColumn(separator,2);metadata.Children.Add(separator);}
        _status=Ui.Text(WeatherHeaderText.Status(app.WeatherStatus),11,.7);_status.MaxWidth=288;_status.TextWrapping=TextWrapping.NoWrap;_status.TextTrimming=TextTrimming.CharacterEllipsis;_status.VerticalAlignment=VerticalAlignment.Center;ToolTipService.SetToolTip(_status,app.WeatherStatus);Grid.SetColumn(_status,3);metadata.Children.Add(_status);header.Children.Add(metadata);_content.Children.Add(header);
        if(!string.IsNullOrEmpty(app.LocationStatus))_content.Children.Add(Ui.Text(app.LocationStatus,12,.75));
        _content.Children.Add(_searchReveal);if(app.Prefs.City==null)_searchReveal.SetExpanded(true,false);
        if(app.Prefs.City==null){var welcome=Ui.Stack(14);welcome.Margin=new Thickness(8,44,8,44);var icon=Ui.Icon("\uE706",58);icon.Foreground=Ui.Brush(ColorHelper.FromArgb(255,224,169,43));welcome.Children.Add(icon);welcome.Children.Add(Ui.Text("抬眼看天气",28));welcome.Children.Add(Ui.Text("添加常用城市，温度就会留在任务栏。\n点开后查看逐时预报、风速与空气质量。",15,.7));_content.Children.Add(welcome);return;}
        if(f==null){_content.Children.Add(new ProgressRing{IsActive=app.WeatherStatus.Contains("正在更新"),Width=36,Height=36,Margin=new Thickness(0,36,0,36)});return;}
        if((DateTime.Now-f.Updated).TotalHours>2)_content.Children.Add(new InfoBar{IsOpen=true,Severity=InfoBarSeverity.Warning,Title="天气数据已过期",Message="当前显示上次保存的天气，请刷新。",IsClosable=false});
        // The compact city header no longer needs the previous double section gap.
        var current=new Grid{ColumnSpacing=12,Margin=new Thickness(0,-8,0,0)};
        current.ColumnDefinitions.Add(new(){Width=new GridLength(68)});current.ColumnDefinitions.Add(new(){Width=GridLength.Auto});current.ColumnDefinitions.Add(new(){Width=new GridLength(1,GridUnitType.Star)});
        _currentWeather=current;current.Loaded+=(_,_)=>PositionLight();current.SizeChanged+=(_,_)=>PositionLight();_atmosphere.Update(f.Code,f.IsDay,app.Prefs.Animations);
        current.Children.Add(new WeatherGlyph(f.Code,f.IsDay,68){VerticalAlignment=VerticalAlignment.Center,RenderTransform=new TranslateTransform{Y=-3}});
        var temperature=Ui.Text(app.Temperature(f.Temperature)+(app.Prefs.Fahrenheit?"F":"C"),52);temperature.TextWrapping=TextWrapping.NoWrap;Grid.SetColumn(temperature,1);current.Children.Add(temperature);
        var info=Ui.Stack(6);info.VerticalAlignment=VerticalAlignment.Center;
        var condition=new Grid{ColumnSpacing=0,HorizontalAlignment=HorizontalAlignment.Left};condition.ColumnDefinitions.Add(new(){Width=GridLength.Auto});var conditionName=Ui.Text(f.ConditionText??WeatherNames.Describe(f.Code),18);conditionName.Margin=new Thickness(0,0,5,0);condition.Children.Add(conditionName);
        var alerts=WeatherAlerts.Current(f.Alerts,DateTimeOffset.UtcNow);
        if(Environment.GetCommandLineArgs().Contains("--inspect-weather-alerts")){
            var now=DateTimeOffset.UtcNow;alerts=WeatherAlerts.Current(new[]{new WeatherAlert("preview-rain","暴雨","orange","界面测试：暴雨橙色预警","这条数据只用于检查预警浮窗的排布，不是当前城市的实况预警。","测试防御指引区域。","界面测试 · 非实况预警","测试地区",now.AddMinutes(-10),now.AddMinutes(-10),now.AddHours(2)),new WeatherAlert("preview-wind","大风","blue","界面测试：大风蓝色预警","测试多条预警的排序与阅读。","","界面测试 · 非实况预警","测试地区",now.AddMinutes(-5),now.AddMinutes(-5),now.AddHours(1))},now);
        }
        var visibleAlerts=alerts.Select((a,i)=>(Alert:a,Index:i)).DistinctBy(x=>(x.Alert.Event,x.Alert.Level)).Take(2).ToArray();
        for(var i=0;i<visibleAlerts.Length;i++){condition.ColumnDefinitions.Add(new(){Width=GridLength.Auto});var badge=WeatherAlertBadge.Create(alerts,visibleAlerts[i].Index,i==visibleAlerts.Length-1?Math.Max(0,alerts.Count-visibleAlerts.Length):0);badge.HorizontalAlignment=HorizontalAlignment.Left;if(i>0)badge.Margin=new Thickness(3,0,0,0);Grid.SetColumn(badge,i+1);condition.Children.Add(badge);}info.Children.Add(condition);
        if(f.Days.Count>0)info.Children.Add(Ui.Text($"最低 {app.Temperature(f.Days[0].Min)}  最高 {app.Temperature(f.Days[0].Max)} · 体感 {app.Temperature(f.FeelsLike)}",12,.8));
        Grid.SetColumn(info,2);current.Children.Add(info);_content.Children.Add(current);
        _content.Children.Add(new RainPreview(Precipitation.NextHour(f.ShortTermPrecipitation,DateTimeOffset.UtcNow)));
        var hourly=Ui.Stack(5);var ht=Ui.Text("未来 24 小时",16);ht.FontWeight=Microsoft.UI.Text.FontWeights.SemiBold;hourly.Children.Add(ht);hourly.Children.Add(new HourlyForecastView(f.Hours));_content.Children.Add(Section(hourly));
        var daily=Ui.Stack(4);var dt=Ui.Text($"{f.Days.Count} 天天气预报",16);dt.FontWeight=Microsoft.UI.Text.FontWeights.SemiBold;var dayHeader=new Grid();dayHeader.Children.Add(dt);daily.Children.Add(dayHeader);var extraDays=Ui.Stack(6);var reveal=_dailyReveal=new AnimatedReveal(extraDays);reveal.LayoutFrame+=ResizeContent;
        var min=f.Days.Count>0?f.Days.Min(d=>d.Min):0;var max=f.Days.Count>0?f.Days.Max(d=>d.Max):1;
        foreach(var d in f.Days){var row=new Grid{ColumnSpacing=10,MinHeight=28};foreach(var width in new[]{112d,32d,58d,1d,36d})row.ColumnDefinitions.Add(new(){Width=width==1?new GridLength(1,GridUnitType.Star):new GridLength(width)});
            var date=Ui.Row(0);var day=Ui.Text(d.Date==f.Days[0].Date?"今天":d.Date==f.Days[0].Date.AddDays(1)?"明天":d.Date.ToString("ddd",new System.Globalization.CultureInfo("zh-CN")),14);day.TextWrapping=TextWrapping.NoWrap;date.Children.Add(day);var calendar=Ui.Text(" "+d.Date.ToString("M/d",System.Globalization.CultureInfo.InvariantCulture),11,.65);calendar.TextWrapping=TextWrapping.NoWrap;date.Children.Add(calendar);row.Children.Add(date);var ic=new WeatherGlyph(d.Code,true,24){HorizontalAlignment=HorizontalAlignment.Center,VerticalAlignment=VerticalAlignment.Center};Grid.SetColumn(ic,1);row.Children.Add(ic);var lo=Ui.Text(app.Temperature(d.Min),14,.65);lo.HorizontalAlignment=HorizontalAlignment.Right;Grid.SetColumn(lo,2);row.Children.Add(lo);
            var range=new Grid{Height=5,VerticalAlignment=VerticalAlignment.Center,Background=Ui.Brush(ColorHelper.FromArgb(28,125,125,125)),CornerRadius=new CornerRadius(3)};var span=Math.Max(1,max-min);range.ColumnDefinitions.Add(new(){Width=new GridLength(Math.Max(.01,d.Min-min),GridUnitType.Star)});range.ColumnDefinitions.Add(new(){Width=new GridLength(Math.Max(.1,d.Max-d.Min),GridUnitType.Star)});range.ColumnDefinitions.Add(new(){Width=new GridLength(Math.Max(.01,max-d.Max),GridUnitType.Star)});var fill=new Border{CornerRadius=new CornerRadius(3),Background=TemperaturePalette.Range((d.Min-min)/span,(d.Max-min)/span)};Grid.SetColumn(fill,1);range.Children.Add(fill);Grid.SetColumn(range,3);row.Children.Add(range);var hi=Ui.Text(app.Temperature(d.Max),14);Grid.SetColumn(hi,4);row.Children.Add(hi);if(daily.Children.Count<=4)daily.Children.Add(row);else extraDays.Children.Add(row);}
        if(extraDays.Children.Count>0){var expand=new Button{Content=expanded?"收起":"展开",FontSize=12,Padding=new Thickness(8,2,8,2),MinHeight=24,Background=Ui.Brush(Colors.Transparent),BorderThickness=new Thickness(0),HorizontalAlignment=HorizontalAlignment.Right};expand.Click+=(_,_)=>{var show=!reveal.Expanded;reveal.SetExpanded(show,app.Prefs.Animations);expand.Content=show?"收起":"展开";};dayHeader.Children.Add(expand);daily.Children.Add(reveal);reveal.SetExpanded(expanded,false);}
        _content.Children.Add(Section(daily));
        var summary=new Grid{ColumnSpacing=12,Margin=new Thickness(0,4,0,2)};for(var i=0;i<5;i++)summary.ColumnDefinitions.Add(new(){Width=new GridLength(1,GridUnitType.Star)});
        var humidity=Summary("湿度 · 相对",$"{f.Humidity:0}%",2);summary.Children.Add(humidity);
        var wind=Summary($"风速 · {(app.Prefs.WindMetersPerSecond?"m/s":"km/h")}",$"{(app.Prefs.WindMetersPerSecond?f.Wind/3.6:f.Wind):0.#}",0,Direction(f.WindDirection));Grid.SetColumn(wind,1);summary.Children.Add(wind);
        var air=Summary($"空气 · {f.AqiStandard}",f.Aqi?.ToString("0")??"—",1);Grid.SetColumn(air,2);summary.Children.Add(air);
        var pressure=Summary("气压 · hPa",f.PressureHpa?.ToString("0")??"—",3);ToolTipService.SetToolTip(pressure,"海平面气压");Grid.SetColumn(pressure,3);summary.Children.Add(pressure);
        var ultraviolet=Summary("紫外线 · UVI",f.UvIndex?.ToString("0.#")??"—",4);Grid.SetColumn(ultraviolet,4);summary.Children.Add(ultraviolet);_content.Children.Add(Section(summary));
    }
    internal double PreferredHeight()
    {
        if(_renderPending&&!IsRevealing){_renderPending=false;RenderCore();}
        _content.Measure(new Size(436,double.PositiveInfinity));
        return _content.DesiredSize.Height+34;
    }
    internal bool IsRevealing=>_dailyReveal?.Animating==true||_searchReveal.Animating;
    private void ResizeContent()
    {
        if(_rendering)return;
        if(IsOpen)App.Instance.PositionFlyout(this,480,PreferredHeight());
    }
    private FrameworkElement CitySwitcher()
    {
        var app=App.Instance;var prefs=app.Prefs;
        var cities=Ui.Row(6);var saved=prefs.Favorites.ToList();
        var location=app.LocatedCity;var locationSelected=prefs.AutoLocateCity&&SavedCities.Same(location,prefs.City);var locationContent=Ui.Row(5);
        locationContent.Children.Add(Ui.Icon("\uE81D",12));if(location!=null){var name=Ui.Text(location.Name,12);name.TextWrapping=TextWrapping.NoWrap;locationContent.Children.Add(name);}
        var locate=new Button{Content=locationContent,Height=28,MinHeight=0,MinWidth=28,CornerRadius=new CornerRadius(14),Padding=location==null?new Thickness(0):new Thickness(10,4,10,4),BorderThickness=new Thickness(0),IsEnabled=!app.LocationBusy};
        Ui.SelectionState(locate,locationSelected);if(!locationSelected)locate.Background=Ui.Brush(ColorHelper.FromArgb(18,145,154,180));
        ToolTipService.SetToolTip(locate,app.LocationBusy?"正在读取系统位置":location==null?"定位当前城市":$"切换到定位城市 · {location.Name}");Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(locate,location==null?"定位当前城市":$"切换到定位城市：{location.Name}");
        locate.Click+=async(_,_)=>await app.UseSystemLocation();cities.Children.Add(locate);
        if(!prefs.AutoLocateCity&&prefs.City is {} current&&!saved.Any(c=>SavedCities.Same(c,current)))saved.Insert(0,current);
        foreach(var city in saved)
        {
            var selected=!prefs.AutoLocateCity&&SavedCities.Same(city,prefs.City);var content=Ui.Row(5);
            var name=Ui.Text(city.Name,12);name.TextWrapping=TextWrapping.NoWrap;content.Children.Add(name);
            var button=new Button{Content=content,Height=28,MinHeight=0,CornerRadius=new CornerRadius(15),BorderThickness=new Thickness(0),Padding=new Thickness(10,4,10,4),Background=selected?Ui.AccentBrush(46):Ui.Brush(ColorHelper.FromArgb(18,145,154,180))};
            if(selected){button.Resources["ButtonBackgroundPointerOver"]=Ui.AccentBrush(65);button.Resources["ButtonBackgroundPressed"]=Ui.AccentBrush(78);}
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(button,$"切换城市：{city.Name}");
            button.Click+=(_,_)=>{ConfirmCity(city);};
            if(prefs.Favorites.Any(c=>SavedCities.Same(c,city))){var menu=new MenuFlyout();var remove=new MenuFlyoutItem{Text="移除已保存城市"};remove.Click+=(_,_)=>app.RemoveSavedCity(city);menu.Items.Add(remove);button.ContextFlyout=menu;}
            cities.Children.Add(button);
        }
        var add=Ui.IconButton("\uE710","添加城市",(_,_)=>ToggleSearch());add.Width=add.Height=28;if(add.Content is FontIcon addIcon)addIcon.FontSize=15;cities.Children.Add(add);
        return new ScrollViewer{Content=cities,Height=28,HorizontalScrollMode=ScrollMode.Enabled,VerticalScrollMode=ScrollMode.Disabled,HorizontalScrollBarVisibility=ScrollBarVisibility.Auto,VerticalScrollBarVisibility=ScrollBarVisibility.Disabled};
    }
    internal void ApplyEffects(){if(App.Instance.Forecast is {} f)_atmosphere.Update(f.Code,f.IsDay,App.Instance.Prefs.Animations);}
    private void PositionLight()
    {
        if(_currentWeather?.IsLoaded!=true)return;
        var point=_currentWeather.TransformToVisual(Root).TransformPoint(new Point(34,34));
        _weatherLight.Margin=new Thickness(point.X+64-167,point.Y+13-122,0,0);_weatherLight.Visibility=point.Y < -40||point.Y>Root.ActualHeight+40?Visibility.Collapsed:Visibility.Visible;
    }
    private static Grid Summary(string label,string value,int kind,string? direction=null)
    {
        var block=new Grid{RowSpacing=4};block.RowDefinitions.Add(new(){Height=new GridLength(20)});block.RowDefinitions.Add(new(){Height=new GridLength(28)});
        var head=Ui.Row(4);head.HorizontalAlignment=HorizontalAlignment.Center;var geometry=kind switch{
            1=>"M3,15 C2,7 8,3 20,2 C20,13 15,20 7,18 M4,20 L16,7",
            2=>"M12,2 C9,7 4,12 4,16 A8,8 0 0 0 20,16 C20,12 15,7 12,2 Z",
            3=>"M4,19 A10,10 0 1 1 20,19 M6,17 L8,15 M5,10 L8,11 M12,4 L12,7 M19,10 L16,11 M12,16 L17,7 M10,16 A2,2 0 1 1 14,16 A2,2 0 1 1 10,16",
            4=>"M7,12 A5,5 0 1 1 17,12 A5,5 0 1 1 7,12 M12,1 L12,4 M12,20 L12,23 M1,12 L4,12 M20,12 L23,12 M4,4 L6,6 M18,18 L20,20 M4,20 L6,18 M18,6 L20,4",
            _=>"M2,7 L14,7 C22,7 21,1 17,2 M2,12 L19,12 C25,12 25,19 20,19 M2,17 L12,17"};
        var path=(Microsoft.UI.Xaml.Shapes.Path)Microsoft.UI.Xaml.Markup.XamlReader.Load($"<Path xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' Data='{geometry}' StrokeThickness='1.5' StrokeStartLineCap='Round' StrokeEndLineCap='Round'/>");path.Stroke=Ui.AccentBrush();head.Children.Add(new Viewbox{Child=path,Width=14,Height=14});var caption=Ui.Text(label,10,.8);caption.TextWrapping=TextWrapping.NoWrap;head.Children.Add(caption);block.Children.Add(new Viewbox{Child=head,Stretch=Stretch.Uniform,StretchDirection=StretchDirection.DownOnly});
        var number=Ui.Text(value,16);number.TextAlignment=TextAlignment.Center;number.TextWrapping=TextWrapping.NoWrap;number.FontWeight=Microsoft.UI.Text.FontWeights.SemiBold;number.VerticalAlignment=VerticalAlignment.Center;var values=Ui.Row(4);values.HorizontalAlignment=HorizontalAlignment.Center;values.Children.Add(number);if(direction!=null){var note=Ui.Text(direction,10,.8);note.Margin=new Thickness(0,2,0,0);note.TextWrapping=TextWrapping.NoWrap;values.Children.Add(note);}var valueHost=new Viewbox{Child=values,Stretch=Stretch.Uniform,StretchDirection=StretchDirection.DownOnly};Grid.SetRow(valueHost,1);block.Children.Add(valueHost);Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(block,$"{label}：{value} {direction}".TrimEnd());return block;
    }
    private void ConfirmCity(City city)
    {
        ++_searchVersion;
        _searchReveal.SetExpanded(false,App.Instance.Prefs.Animations);
        App.Instance.SelectCity(city);
    }
    private void ToggleSearch()
    {
        var show=!_searchReveal.Expanded;if(!show)++_searchVersion;
        _searchReveal.SetExpanded(show,App.Instance.Prefs.Animations);
        if(show)_search.Focus(FocusState.Programmatic);
    }

    static Border Section(UIElement content)=>new(){Child=content,Padding=new Thickness(0,5,0,0),BorderThickness=new Thickness(0,1,0,0),BorderBrush=Ui.Brush(ColorHelper.FromArgb(20,128,128,128))};
    static Grid Detail(string name,string value){var g=new Grid();g.ColumnDefinitions.Add(new(){Width=new GridLength(1,GridUnitType.Star)});g.ColumnDefinitions.Add(new(){Width=GridLength.Auto});g.Children.Add(Ui.Text(name,13,.7));var v=Ui.Text(value,14);Grid.SetColumn(v,1);g.Children.Add(v);return g;}
    static string Direction(double degrees)=>new[]{"北风","东北风","东风","东南风","南风","西南风","西风","西北风"}[(int)Math.Round(degrees/45)%8];
}



