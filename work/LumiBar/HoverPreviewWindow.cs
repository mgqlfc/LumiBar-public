using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Storage.Streams;
namespace LumiBar;
internal sealed class HoverPreviewWindow:SurfaceWindow
{
    private readonly bool _layoutInspection=Environment.GetCommandLineArgs().Contains("--inspect-hover-layout");
    private readonly bool _music;private readonly AlbumAtmosphere? _album;private readonly WeatherAtmosphere? _weather;private readonly Grid _weatherHost=new(){Width=240,Height=180,HorizontalAlignment=HorizontalAlignment.Left,VerticalAlignment=VerticalAlignment.Top,Margin=new Thickness(-50,-30,0,0)};private int _request;
    internal HoverPreviewWindow(bool music):base(music?"LumiBar · 音乐速览":"LumiBar · 天气速览")
    {
        _music=music;KeepOpen=true;
        // Balance the weather glyph's transparent edge against the temperature
        // and three metric columns. Preserve the content width and avoid a
        // one-pixel rounding bias at fractional desktop scales.
        Body.Padding=music?new Thickness(16):new Thickness(14.5,16,17.5,16);
        if(!music)Body.UseLayoutRounding=false;
        Body.IsHitTestVisible=true;
        Root.PointerEntered+=(_,_)=>{_pointer=true;++_request;if(RecoverableFade&&_owner is {} owner&&(!_music||App.Instance.Track!=null))ShowAt(owner);};
        Root.PointerExited+=(_,_)=>{_pointer=false;LeaveOwner();};
        SurfaceVisibilityChanged+=open=>{if(!open){++_request;_pointer=false;}};
        var h=WinRT.Interop.WindowNative.GetWindowHandle(this);Native.SetWindowLong(h,-20,(nint)((long)Native.GetWindowLong(h,-20)|0x08000000L));
        if(music)
        {
            Body.RowDefinitions.Add(new(){Height=new GridLength(1,GridUnitType.Star)});
            Body.RowDefinitions.Add(new(){Height=GridLength.Auto});Body.RowSpacing=8;
            _controls.HorizontalAlignment=HorizontalAlignment.Center;
            _controls.Visibility=Visibility.Collapsed;Grid.SetRow(_controls,1);Body.Children.Add(_controls);
            var previous=Ui.IconButton("\uE892","上一首",async(_,_)=>await App.Instance.PreviousTrack());
            _play=Ui.IconButton("\uE768","播放",async(_,_)=>await App.Instance.TogglePlayback());
            var next=Ui.IconButton("\uE893","下一首",async(_,_)=>await App.Instance.NextTrack());
            foreach(var button in new[]{previous,_play,next}){button.Width=36;button.Height=36;button.CornerRadius=new CornerRadius(18);ToolTipService.SetToolTip(button,null);_controls.Children.Add(button);}
            _play.Background=Ui.Brush(ColorHelper.FromArgb(35,175,169,205));
        }
        if(music){_album=new AlbumAtmosphere(true);LightHost.Children.Add(_album);SurfaceVisibilityChanged+=open=>_album.SetActive(open);}else{LightHost.Children.Add(_weatherHost);_weather=new WeatherAtmosphere(_weatherHost);SurfaceVisibilityChanged+=open=>_weather.SetActive(open);}
    }
    private CapsuleWindow? _owner;
    private bool _pointer, _musicControls; private long _recoverUntil,_outsideSince,_lastPointerCheck; private bool RecoverableFade=>Environment.TickCount64<_recoverUntil;
    private Grid? _musicInfo;
    private readonly StackPanel _controls=Ui.Row(12);
    private Button? _play;
    protected override int ExitDurationMs=>120;
    private const int LeaveDelayMs=350;
    internal async void Request(CapsuleWindow owner)
    {
        var request=++_request;
        if(RecoverableFade&&_owner==owner){ShowAt(owner);return;}
        if(IsOpen&&_owner==owner){if(_musicControls!=(_music&&owner.IsLite))ShowAt(owner);return;}
        await Task.Delay(320);
        if(request==_request&&owner.WantsHover&&Native.PointerWithin(owner))ShowAt(owner);
    }
    internal async void Demonstrate(CapsuleWindow owner)
    {
        var request=++_request;ShowAt(owner);
        await Task.Delay(5000);
        if(request==_request&&!_pointer&&!owner.WantsHover)Cancel();
    }
    internal async void LeaveOwner()
    {
        if(_layoutInspection)return;
        var request=++_request;
        if(!IsOpen)return;
        await Task.Delay(LeaveDelayMs);
        if(request==_request&&!PointerWithinPreviewOrOwner())BeginLeave();
    }
    private bool PointerWithinPreviewOrOwner()=>Native.PointerWithin(this)||_owner is {IsOpen:true} owner&&Native.PointerWithin(owner);
    private void BeginLeave(){_outsideSince=0;_recoverUntil=App.Instance.Prefs.Animations?Environment.TickCount64+140:0;HideSurface();}
    internal void CheckPointer()
    {
        if(_layoutInspection)return;
        if(!IsOpen){_outsideSince=0;return;}
        var now=Environment.TickCount64;if(now-_lastPointerCheck<50)return;_lastPointerCheck=now;
        // Crossing native windows can omit PointerExited. Use physical bounds
        // only while a preview is open, with the same grace as normal events.
        if(PointerWithinPreviewOrOwner()){_outsideSince=0;return;}
        if(_outsideSince==0)_outsideSince=now;
        else if(now-_outsideSince>=LeaveDelayMs)BeginLeave();
    }
    internal void OwnerModeChanged(CapsuleWindow owner){if(_music&&!owner.IsLite){Cancel();return;}if(IsOpen&&_owner==owner)ShowAt(owner);}
    private void ShowAt(CapsuleWindow owner)
    {
        if(_music&&!owner.IsLite){Cancel();return;}
        _recoverUntil=0;_outsideSince=0;_owner=owner;_musicControls=_music&&owner.IsLite;Render();
        var scale=Native.ReadTaskbar()?.Scale??1;var width=_music?300:264;var height=_music?(_musicControls?164:124):160;width+=PopupShadow.Gutter*2;height+=PopupShadow.Gutter*2;
        Native.GetWindowRect(WinRT.Interop.WindowNative.GetWindowHandle(owner),out var rect);var bar=Native.ReadTaskbar();
        var x=Math.Clamp(rect.Left+(rect.Width-(int)(width*scale))/2,bar?.Bar.Left??0,Math.Max(bar?.Bar.Left??0,(bar?.Bar.Right??1920)-(int)(width*scale)));
        Root.RequestedTheme=owner.Root.ActualTheme;(SystemBackdrop as GlassBackdrop)?.Refresh();
        Native.Place(this,x,rect.Top-(int)((height+8-PopupShadow.Gutter)*scale),(int)(width*scale),(int)(height*scale));
        if(!IsOpen)ShowSurface(false);
    }
    internal void Cancel(){++_request;_pointer=false;_recoverUntil=0;HideSurface();}
    internal async void Render()
    {
        if(!_music)Body.Children.Clear();var a=App.Instance;
        if(_music){var t=a.Track;if(t==null){Cancel();return;}foreach(var button in _controls.Children.OfType<Button>())Ui.CoverButtonHover(button);_play!.Background=Ui.CoverBrush(20,Root.ActualTheme);_controls.Visibility=_musicControls?Visibility.Visible:Visibility.Collapsed;_controls.Children.OfType<Button>().ElementAt(0).IsEnabled=t.Controls?.Previous==true;_controls.Children.OfType<Button>().ElementAt(2).IsEnabled=t.Controls?.Next==true;_play!.IsEnabled=t.Controls?.Toggle==true;_play.Content=new FontIcon{Glyph=t.Playing?"\uE769":"\uE768",FontSize=16};Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(_play,t.Playing?"暂停":"播放");if(_musicInfo!=null)Body.Children.Remove(_musicInfo);_album!.Refresh();var row=new Grid{ColumnSpacing=13};row.ColumnDefinitions.Add(new(){Width=new GridLength(62)});row.ColumnDefinitions.Add(new(){Width=new GridLength(1,GridUnitType.Star)});var image=new Image{Stretch=Stretch.UniformToFill,Width=62,Height=62};var art=new Border{Child=image,CornerRadius=new CornerRadius(12),Width=62,Height=62};Ui.ClipRounded(art,12);row.Children.Add(art);var names=Ui.Stack(5);names.VerticalAlignment=VerticalAlignment.Center;var title=Ui.Text(t.Title,16);title.FontWeight=Microsoft.UI.Text.FontWeights.SemiBold;title.MaxLines=2;title.TextTrimming=TextTrimming.CharacterEllipsis;names.Children.Add(title);var artist=Ui.Text(t.Artist,12,.8);artist.TextTrimming=TextTrimming.CharacterEllipsis;artist.MaxLines=1;names.Children.Add(artist);var album=Ui.Text(string.IsNullOrWhiteSpace(t.Album)?"未知专辑":t.Album,11,.7);album.TextTrimming=TextTrimming.CharacterEllipsis;album.MaxLines=1;names.Children.Add(album);Grid.SetColumn(names,1);row.Children.Add(names);_musicInfo=row;Body.Children.Add(row);if(t.Cover is {} bytes){using var stream=new InMemoryRandomAccessStream();using(var writer=new DataWriter(stream)){writer.WriteBytes(bytes);await writer.StoreAsync();writer.DetachStream();}stream.Seek(0);var bitmap=new BitmapImage();await bitmap.SetSourceAsync(stream);if(a.Track?.Key==t.Key)image.Source=bitmap;}}
        else{var f=a.Forecast;if(f==null){var loading=Ui.Stack(10);loading.VerticalAlignment=VerticalAlignment.Center;loading.Children.Add(Ui.Text(a.Prefs.City?.Name??"选择城市",18));loading.Children.Add(Ui.Text(a.WeatherStatus,12,.75));Body.Children.Add(loading);return;}_weather!.Update(f.Code,f.IsDay,a.Prefs.Animations);var content=Ui.Stack(11);var top=new Grid{ColumnSpacing=8,HorizontalAlignment=HorizontalAlignment.Center,Height=40};top.ColumnDefinitions.Add(new(){Width=new GridLength(30)});for(var i=0;i<3;i++)top.ColumnDefinitions.Add(new(){Width=GridLength.Auto});top.Children.Add(new WeatherGlyph(f.Code,f.IsDay,30));var city=Ui.Text(a.Prefs.City?.Name??"天气",15);city.MaxWidth=68;city.MaxLines=1;city.TextTrimming=TextTrimming.CharacterEllipsis;city.FontWeight=Microsoft.UI.Text.FontWeights.SemiBold;Grid.SetColumn(city,1);top.Children.Add(city);var condition=Ui.Text(f.ConditionText??WeatherNames.Describe(f.Code),11,.75);condition.TextWrapping=TextWrapping.NoWrap;Grid.SetColumn(condition,2);top.Children.Add(condition);var temp=Ui.Text(a.Temperature(f.Temperature)+(a.Prefs.Fahrenheit?"F":"C"),24);temp.TextWrapping=TextWrapping.NoWrap;temp.FontWeight=Microsoft.UI.Text.FontWeights.SemiBold;Grid.SetColumn(temp,3);top.Children.Add(temp);content.Children.Add(top);
            var metrics=new Grid{ColumnSpacing=10};for(int i=0;i<3;i++)metrics.ColumnDefinitions.Add(new(){Width=new GridLength(1,GridUnitType.Star)});
            Grid Metric(string title,string value,string note,FrameworkElement icon){var stack=new Grid{RowSpacing=3};stack.RowDefinitions.Add(new(){Height=new GridLength(18)});stack.RowDefinitions.Add(new(){Height=new GridLength(31)});stack.RowDefinitions.Add(new(){Height=new GridLength(17)});var head=Ui.Row(4);head.HorizontalAlignment=HorizontalAlignment.Center;head.Children.Add(icon);head.Children.Add(Ui.Text(title,11,.8));stack.Children.Add(head);var number=Ui.Text(value,22);number.TextAlignment=TextAlignment.Center;number.TextWrapping=TextWrapping.NoWrap;number.LineHeight=29;number.LineStackingStrategy=LineStackingStrategy.BlockLineHeight;number.FontWeight=Microsoft.UI.Text.FontWeights.SemiBold;Grid.SetRow(number,1);stack.Children.Add(number);var caption=Ui.Text(note,10,.75);caption.TextAlignment=TextAlignment.Center;caption.MaxLines=1;caption.TextTrimming=TextTrimming.CharacterEllipsis;Grid.SetRow(caption,2);stack.Children.Add(caption);return stack;}
            FrameworkElement Symbol(string geometry){var path=(Microsoft.UI.Xaml.Shapes.Path)Microsoft.UI.Xaml.Markup.XamlReader.Load($"<Path xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' Data='{geometry}' StrokeThickness='1.6' StrokeStartLineCap='Round' StrokeEndLineCap='Round'/>");path.Stroke=Ui.AccentBrush();return new Viewbox{Child=path,Width=13,Height=13};}
            var feels=Metric("体感",a.Temperature(f.FeelsLike),"当前",Symbol("M9,3 C9,0 15,0 15,3 L15,14 A5,5 0 1 1 9,14 Z M12,7 L12,17"));var rain=Metric("降水",f.Rain is {} rainPercent?$"{rainPercent:0}%":"—","未来 1 小时",new WeatherGlyph(61,true,14));var air=Metric("空气",f.Aqi?.ToString("0")??"—",WeatherAir.Caption(f),Symbol("M3,15 C2,7 8,3 20,2 C20,13 15,20 7,18 M4,20 L16,7"));metrics.Children.Add(feels);Grid.SetColumn(rain,1);metrics.Children.Add(rain);Grid.SetColumn(air,2);metrics.Children.Add(air);content.Children.Add(metrics);Body.Children.Add(content);}
    }
}


