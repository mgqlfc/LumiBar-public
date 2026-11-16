using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Windows.Foundation;

namespace LumiBar;
internal sealed class WeatherWindow : SurfaceWindow
{
    private readonly Grid _weatherLight=new(){Width=300,Height=220,HorizontalAlignment=HorizontalAlignment.Left,VerticalAlignment=VerticalAlignment.Top,IsHitTestVisible=false};
    private readonly WeatherAtmosphere _atmosphere;private FrameworkElement? _currentWeather;
    private readonly StackPanel _content=Ui.Stack(8);private readonly StackPanel _searchArea=Ui.Stack(10);private readonly TextBox _search=new(){PlaceholderText="搜索城市，例如：天津 / Tianjin"};private readonly StackPanel _results=Ui.Stack(6);private readonly TextBlock _searchStatus=Ui.Text("",12,.7);private int _searchVersion;
    internal WeatherWindow():base("LumiBar · 天气")
    {
        // Atmospheric colour belongs to the glass surface, never to icon pixels.
        LightHost.Children.Add(_weatherLight);_atmosphere=new WeatherAtmosphere(_weatherLight);
        SurfaceVisibilityChanged+=open=>_atmosphere.SetActive(open);
        _searchArea.Visibility=Visibility.Collapsed;
        var scroll=new ScrollViewer{Content=_content,Padding=new Thickness(22,16,22,16),HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled,VerticalScrollBarVisibility=ScrollBarVisibility.Auto};scroll.ViewChanged+=(_,_)=>PositionLight();Body.Children.Add(scroll);
        var searchRow=new Grid{ColumnSpacing=8};searchRow.ColumnDefinitions.Add(new(){Width=new GridLength(1,GridUnitType.Star)});searchRow.ColumnDefinitions.Add(new(){Width=GridLength.Auto});searchRow.Children.Add(_search);
        var go=new Button{Content="搜索"};go.Click+=async(_,_)=>await Search();Grid.SetColumn(go,1);searchRow.Children.Add(go);_search.KeyDown+=async(_,e)=>{if(e.Key==Windows.System.VirtualKey.Enter)await Search();};
        _searchArea.Children.Add(searchRow);var gps=new Button{Content="使用 GPS 卫星定位",HorizontalAlignment=HorizontalAlignment.Left};gps.Click+=async(_,_)=>{
            gps.IsEnabled=false;KeepOpen=true;_searchStatus.Text="正在等待 GPS 卫星位置…";
            try{await Task.CompletedTask;_searchStatus.Text="交互原型不读取位置，请使用示例城市";}catch(Exception ex){_searchStatus.Text=ex is InvalidOperationException?ex.Message:"GPS 暂不可用，请手动选择城市。";App.Log("GPS",ex);}finally{KeepOpen=false;gps.IsEnabled=true;}
        };_searchArea.Children.Add(gps);_searchArea.Children.Add(_searchStatus);_searchArea.Children.Add(_results);
    }
    private async Task Search(){var query=_search.Text.Trim();if(query.Length<2){_searchStatus.Text="请输入至少两个字符";return;}var version=++_searchVersion;_searchStatus.Text="正在查找城市…";_results.Children.Clear();try{await Task.CompletedTask;var cities=new List<City>{new(query,36.7,119.1,"示例城市")};if(version!=_searchVersion)return;_searchStatus.Text=cities.Count==0?"没有找到城市，试试完整名称或拼音":"请选择城市";foreach(var city in cities){var b=new Button{Content=$"{city.Name}  {city.Region}",HorizontalAlignment=HorizontalAlignment.Stretch,HorizontalContentAlignment=HorizontalAlignment.Left};b.Click+=(_,_)=>{App.Instance.SelectCity(city);_searchArea.Visibility=Visibility.Collapsed;};_results.Children.Add(b);}}catch(Exception ex){_searchStatus.Text="城市搜索失败，请检查网络后重试";App.Log("city search",ex);}}
    internal void Render()
    {
        var app=App.Instance;var f=app.Forecast;_currentWeather=null;_weatherLight.Visibility=Visibility.Collapsed;_content.Children.Clear();
        var header=new Grid();header.ColumnDefinitions.Add(new(){Width=new GridLength(1,GridUnitType.Star)});header.ColumnDefinitions.Add(new(){Width=GridLength.Auto});
        var titles=Ui.Stack(3);var title=Ui.Text(app.Prefs.City?.Name??"你的天气",24);title.FontWeight=Microsoft.UI.Text.FontWeights.SemiBold;titles.Children.Add(title);titles.Children.Add(Ui.Text(app.WeatherStatus,12,.65));header.Children.Add(titles);
        var tools=Ui.Row(2);
        var refresh=Ui.IconButton("\uE72C","更新天气",async(sender,_)=>{if(sender is Button b)b.IsEnabled=false;await app.RefreshWeather();});refresh.IsEnabled=app.Prefs.City!=null&&!app.WeatherStatus.Contains("正在更新");tools.Children.Add(refresh);
        tools.Children.Add(Ui.More(("选择城市",(_,_)=>_searchArea.Visibility=_searchArea.Visibility==Visibility.Visible?Visibility.Collapsed:Visibility.Visible),(KeepOpen?"取消固定":"固定浮窗",(_,_)=>{KeepOpen=!KeepOpen;Render();}),("设置",(_,_)=>app.OpenSettings()),("关闭",(_,_)=>HideSurface())));Grid.SetColumn(tools,1);header.Children.Add(tools);_content.Children.Add(header);
        if(app.Prefs.City is {} selected){
            var cities=Ui.Row(6);
            var currentCity=Ui.Row(5);currentCity.Children.Add(new Viewbox{Width=12,Height=12,Child=new SymbolIcon(Symbol.MapPin)});currentCity.Children.Add(Ui.Text(selected.Name,12));
            var selectedButton=new Button{Content=currentCity,CornerRadius=new CornerRadius(16),BorderThickness=new Thickness(0),Background=Ui.Brush(ColorHelper.FromArgb(22,92,151,160)),Padding=new Thickness(10,5,10,5)};
            ToolTipService.SetToolTip(selectedButton,"选择城市");selectedButton.Click+=(_,_)=>ToggleSearch();cities.Children.Add(selectedButton);
            foreach(var city in app.Prefs.Favorites.Where(c=>c!=selected).Take(6)){var b=new Button{Content=city.Name,CornerRadius=new CornerRadius(16),BorderThickness=new Thickness(0),Padding=new Thickness(10,5,10,5)};b.Click+=(_,_)=>app.SelectCity(city);cities.Children.Add(b);}
            var add=Ui.IconButton("\uE710","添加城市",(_,_)=>ToggleSearch());add.Width=30;add.Height=30;cities.Children.Add(add);
            _content.Children.Add(new ScrollViewer{Content=cities,HorizontalScrollBarVisibility=ScrollBarVisibility.Hidden,VerticalScrollBarVisibility=ScrollBarVisibility.Disabled});
        }
        _searchArea.Visibility=app.Prefs.City==null?Visibility.Visible:_searchArea.Visibility;_content.Children.Add(_searchArea);
        if(app.Prefs.City==null){var welcome=Ui.Stack(14);welcome.Margin=new Thickness(8,44,8,44);var icon=Ui.Icon("\uE706",58);icon.Foreground=Ui.Brush(ColorHelper.FromArgb(255,224,169,43));welcome.Children.Add(icon);welcome.Children.Add(Ui.Text("抬眼看天气",28));welcome.Children.Add(Ui.Text("添加常用城市，温度就会留在任务栏。\n点开后查看逐时预报、风速与空气质量。",15,.7));_content.Children.Add(welcome);return;}
        if(f==null){_content.Children.Add(new ProgressRing{IsActive=app.WeatherStatus.Contains("正在更新"),Width=36,Height=36,Margin=new Thickness(0,36,0,36)});return;}
        if((DateTime.Now-f.Updated).TotalHours>2)_content.Children.Add(new InfoBar{IsOpen=true,Severity=InfoBarSeverity.Warning,Title="天气数据已过期",Message="当前显示上次保存的天气，请刷新。",IsClosable=false});
        var current=new Grid{ColumnSpacing=12};
        current.ColumnDefinitions.Add(new(){Width=new GridLength(68)});current.ColumnDefinitions.Add(new(){Width=GridLength.Auto});current.ColumnDefinitions.Add(new(){Width=new GridLength(1,GridUnitType.Star)});
        _currentWeather=current;current.Loaded+=(_,_)=>PositionLight();current.SizeChanged+=(_,_)=>PositionLight();_atmosphere.Update(f.Code,f.IsDay,app.Prefs.Animations);
        current.Children.Add(new WeatherGlyph(f.Code,f.IsDay,68){VerticalAlignment=VerticalAlignment.Center});
        var temperature=Ui.Text(app.Temperature(f.Temperature)+(app.Prefs.Fahrenheit?"F":"C"),52);temperature.TextWrapping=TextWrapping.NoWrap;Grid.SetColumn(temperature,1);current.Children.Add(temperature);
        var info=Ui.Stack(6);info.VerticalAlignment=VerticalAlignment.Center;info.Children.Add(Ui.Text(WeatherNames.Describe(f.Code),18));
        if(f.Days.Count>0)info.Children.Add(Ui.Text($"最高 {app.Temperature(f.Days[0].Max)}  最低 {app.Temperature(f.Days[0].Min)} · 体感 {app.Temperature(f.FeelsLike)}",12,.8));
        Grid.SetColumn(info,2);current.Children.Add(info);_content.Children.Add(current);
        _content.Children.Add(new RainPreview(Precipitation.NextHour(f.ShortTermPrecipitation,DateTimeOffset.UtcNow)));
        var hourly=Ui.Stack(5);var ht=Ui.Text("未来 24 小时",16);ht.FontWeight=Microsoft.UI.Text.FontWeights.SemiBold;hourly.Children.Add(ht);hourly.Children.Add(new HourlyForecastView(f.Hours));_content.Children.Add(Section(hourly));
        var daily=Ui.Stack(4);var dt=Ui.Text($"{f.Days.Count} 天天气预报",16);dt.FontWeight=Microsoft.UI.Text.FontWeights.SemiBold;var dayHeader=new Grid();dayHeader.Children.Add(dt);daily.Children.Add(dayHeader);var extraDays=Ui.Stack(6);extraDays.Visibility=Visibility.Collapsed;
        var min=f.Days.Min(d=>d.Min);var max=f.Days.Max(d=>d.Max);
        foreach(var d in f.Days){var row=new Grid{ColumnSpacing=10};foreach(var width in new[]{82d,30d,90d,1d,36d})row.ColumnDefinitions.Add(new(){Width=width==1?new GridLength(1,GridUnitType.Star):new GridLength(width)});
            var date=Ui.Text(d.Date==f.Days[0].Date?"今天":d.Date==f.Days[0].Date.AddDays(1)?"明天":d.Date.ToString("ddd",new System.Globalization.CultureInfo("zh-CN")),14);row.Children.Add(date);var ic=new WeatherGlyph(d.Code,true,24);Grid.SetColumn(ic,1);row.Children.Add(ic);var lo=Ui.Text(app.Temperature(d.Min),14,.65);lo.HorizontalAlignment=HorizontalAlignment.Right;Grid.SetColumn(lo,2);row.Children.Add(lo);
            var range=new Grid{Height=5,VerticalAlignment=VerticalAlignment.Center,Background=Ui.Brush(ColorHelper.FromArgb(28,125,125,125)),CornerRadius=new CornerRadius(3)};var span=Math.Max(1,max-min);range.ColumnDefinitions.Add(new(){Width=new GridLength(Math.Max(.01,d.Min-min),GridUnitType.Star)});range.ColumnDefinitions.Add(new(){Width=new GridLength(Math.Max(.1,d.Max-d.Min),GridUnitType.Star)});range.ColumnDefinitions.Add(new(){Width=new GridLength(Math.Max(.01,max-d.Max),GridUnitType.Star)});var fill=new Border{CornerRadius=new CornerRadius(3),Background=TemperaturePalette.Range((d.Min-min)/span,(d.Max-min)/span)};Grid.SetColumn(fill,1);range.Children.Add(fill);Grid.SetColumn(range,3);row.Children.Add(range);var hi=Ui.Text(app.Temperature(d.Max),14);Grid.SetColumn(hi,4);row.Children.Add(hi);if(daily.Children.Count<=4)daily.Children.Add(row);else extraDays.Children.Add(row);}
        if(extraDays.Children.Count>0){var expand=new Button{Content="展开",FontSize=12,Padding=new Thickness(8,2,8,2),MinHeight=24,Background=Ui.Brush(Colors.Transparent),BorderThickness=new Thickness(0),HorizontalAlignment=HorizontalAlignment.Right};expand.Click+=(_,_)=>{var show=extraDays.Visibility!=Visibility.Visible;extraDays.Visibility=show?Visibility.Visible:Visibility.Collapsed;expand.Content=show?"收起":"展开";};dayHeader.Children.Add(expand);daily.Children.Add(extraDays);}
        _content.Children.Add(Section(daily));
        var summary=new Grid{ColumnSpacing=14};for(var i=0;i<3;i++)summary.ColumnDefinitions.Add(new(){Width=new GridLength(1,GridUnitType.Star)});
        var humidity=Summary("湿度",$"{f.Humidity:0}%",2);summary.Children.Add(humidity);
        var wind=Summary("风速",$"{(app.Prefs.WindMetersPerSecond?f.Wind/3.6:f.Wind):0.#} {(app.Prefs.WindMetersPerSecond?"m/s":"km/h")}",0,Direction(f.WindDirection));Grid.SetColumn(wind,1);summary.Children.Add(wind);
        var air=Summary("空气 · US AQI",f.Aqi?.ToString("0")??"—",1);Grid.SetColumn(air,2);summary.Children.Add(air);_content.Children.Add(Section(summary));
    }
    internal int PreferredHeight(){_content.InvalidateMeasure();_content.Measure(new Size(436,double.PositiveInfinity));return (int)Math.Ceiling(_content.DesiredSize.Height+34);}
    internal void ApplyEffects(){if(App.Instance.Forecast is {} f)_atmosphere.Update(f.Code,f.IsDay,App.Instance.Prefs.Animations);}
    private void PositionLight()
    {
        if(_currentWeather?.IsLoaded!=true)return;
        var point=_currentWeather.TransformToVisual(Root).TransformPoint(new Point(34,34));
        _weatherLight.Margin=new Thickness(point.X-150,point.Y-110,0,0);_weatherLight.Visibility=point.Y < -40||point.Y>Root.ActualHeight+40?Visibility.Collapsed:Visibility.Visible;
    }
    private static Grid Summary(string label,string value,int kind,string? direction=null)
    {
        var block=new Grid{RowSpacing=4};block.RowDefinitions.Add(new(){Height=new GridLength(19)});block.RowDefinitions.Add(new(){Height=new GridLength(30)});
        var head=Ui.Row(5);head.HorizontalAlignment=HorizontalAlignment.Center;var geometry=kind==1?"M3,15 C2,7 8,3 20,2 C20,13 15,20 7,18 M4,20 L16,7":kind==2?"M12,2 C9,7 4,12 4,16 A8,8 0 0 0 20,16 C20,12 15,7 12,2 Z":"M2,7 L14,7 C22,7 21,1 17,2 M2,12 L19,12 C25,12 25,19 20,19 M2,17 L12,17";
        var path=(Microsoft.UI.Xaml.Shapes.Path)Microsoft.UI.Xaml.Markup.XamlReader.Load($"<Path xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' Data='{geometry}' StrokeThickness='1.5' StrokeStartLineCap='Round' StrokeEndLineCap='Round'/>");path.Stroke=Ui.AccentBrush();head.Children.Add(new Viewbox{Child=path,Width=16,Height=16});head.Children.Add(Ui.Text(label,10,.8));block.Children.Add(head);
        var number=Ui.Text(value,17);number.TextAlignment=TextAlignment.Center;number.TextWrapping=TextWrapping.NoWrap;number.TextTrimming=TextTrimming.CharacterEllipsis;number.FontWeight=Microsoft.UI.Text.FontWeights.SemiBold;number.VerticalAlignment=VerticalAlignment.Center;var values=Ui.Row(4);values.HorizontalAlignment=HorizontalAlignment.Center;values.Children.Add(number);if(direction!=null){var note=Ui.Text(direction,11,.8);note.Margin=new Thickness(0,3,0,0);values.Children.Add(note);}Grid.SetRow(values,1);block.Children.Add(values);return block;
    }
    private void ToggleSearch(){_searchArea.Visibility=_searchArea.Visibility==Visibility.Visible?Visibility.Collapsed:Visibility.Visible;if(_searchArea.Visibility==Visibility.Visible)_search.Focus(FocusState.Programmatic);}
    static Border Section(UIElement content)=>new(){Child=content,Padding=new Thickness(0,5,0,0),BorderThickness=new Thickness(0,1,0,0),BorderBrush=Ui.Brush(ColorHelper.FromArgb(20,128,128,128))};
    static Grid Detail(string name,string value){var g=new Grid();g.ColumnDefinitions.Add(new(){Width=new GridLength(1,GridUnitType.Star)});g.ColumnDefinitions.Add(new(){Width=GridLength.Auto});g.Children.Add(Ui.Text(name,13,.7));var v=Ui.Text(value,14);Grid.SetColumn(v,1);g.Children.Add(v);return g;}
    static string Direction(double degrees)=>new[]{"北风","东北风","东风","东南风","南风","西南风","西风","西北风"}[(int)Math.Round(degrees/45)%8];
}



