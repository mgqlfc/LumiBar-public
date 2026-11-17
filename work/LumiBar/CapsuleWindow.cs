using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Storage.Streams;
namespace LumiBar;
// Shared by the actual preview capsule and Appearance > effect preview.
internal sealed class MusicCapsuleContent:Grid
{
    // Move both text rows left through layout so marquee measurement follows
    // the available width while the cover and control rail stay anchored.
    private const double NameColumnSpacing=4;
    private readonly Border _controlRail=new(){Width=84,HorizontalAlignment=HorizontalAlignment.Right,VerticalAlignment=VerticalAlignment.Center,Margin=new Thickness(0,0,4,0),CornerRadius=new CornerRadius(13),Opacity=0,IsHitTestVisible=false};
    private readonly Button _previous,_play,_next;
    private readonly Border _coverFrame;
    private Microsoft.UI.Composition.InsetClip? _artistClip;private bool _hoverControls;
    private readonly AlbumAtmosphere _album=new(true);private readonly SpectrumBackdrop _spectrum=new();private readonly Image _cover;private readonly TextBlock _title,_artist;private readonly MarqueeTitle _marquee;private readonly StackPanel _names=Ui.Stack(0);private readonly Button _open;private readonly Grid _row=new(){ColumnSpacing=NameColumnSpacing};private bool _lite;private string? _key;private byte[]? _coverBytes;private int _artworkRevision;private readonly bool _large;
    internal MusicCapsuleContent(bool large=false)
    {
        _large=large;var size=large?40:28;_cover=new(){Width=size,Height=size,Stretch=Stretch.UniformToFill};_title=Ui.Text("",large?15:13);_artist=Ui.Text("",large?11:10,.85);_title.FontWeight=Microsoft.UI.Text.FontWeights.SemiBold;foreach(var t in new[]{_title,_artist}){t.TextWrapping=TextWrapping.NoWrap;t.TextTrimming=TextTrimming.CharacterEllipsis;}_artist.Margin=new Thickness(8,0,0,0);_marquee=new(_title);_names.Children.Add(_marquee);_names.Children.Add(_artist);
        Children.Add(_album);_spectrum.Margin=new Thickness(size+20,large?8:4,6,2);_spectrum.Opacity=.32;Children.Add(_spectrum);_row.ColumnDefinitions.Add(new(){Width=new GridLength(size)});_row.ColumnDefinitions.Add(new(){Width=new GridLength(1,GridUnitType.Star)});var art=_coverFrame=new Border{Child=_cover,CornerRadius=new CornerRadius(large?9:7),Width=size,Height=size};Ui.ClipRounded(art,large?9:7);art.RenderTransform=new TranslateTransform{X=2};_row.Children.Add(art);Grid.SetColumn(_names,1);_row.Children.Add(_names);
        _open=new(){Content=_row,Padding=new Thickness(large?5:9,0,8,0),Background=Ui.Brush(Colors.Transparent),BorderThickness=new Thickness(0),HorizontalContentAlignment=HorizontalAlignment.Stretch,VerticalContentAlignment=VerticalAlignment.Center,CornerRadius=new CornerRadius(17)};PopupToggle.Bind(_open,()=>App.Instance.MusicWindow,()=>App.Instance.OpenMusic());Children.Add(_open);
        var buttons=Ui.Row(2);buttons.HorizontalAlignment=HorizontalAlignment.Right;
        _previous=Ui.IconButton("\uE892","上一首",async(_,_)=>await App.Instance.PreviousTrack());
        _play=Ui.IconButton("\uE768","播放 / 暂停",async(_,_)=>await App.Instance.TogglePlayback());
        _next=Ui.IconButton("\uE893","下一首",async(_,_)=>await App.Instance.NextTrack());
        foreach(var b in new[]{_previous,_play,_next}){b.Width=25;b.Height=26;b.MinWidth=b.MinHeight=0;b.CornerRadius=new CornerRadius(13);if(b.Content is FontIcon glyph)glyph.FontSize=12;ToolTipService.SetToolTip(b,null);buttons.Children.Add(b);}
        _controlRail.Child=buttons;Children.Add(_controlRail);
        PointerEntered+=(_,_)=>SetControls(true);PointerExited+=(_,_)=>SetControls(false);
        ActualThemeChanged+=(_,_)=>StyleControls();Loaded+=(_,_)=>{StyleControls();_controlRail.Opacity=1;Ui.Fade(_controlRail,_hoverControls?1:0,false);};
        SizeChanged+=(_,_)=>UpdateRowWidth();Ui.ClipRounded(this,17);Refresh();
    }
    private void StyleControls(){_controlRail.Background=Ui.Brush(Colors.Transparent);foreach(var button in new[]{_previous,_play,_next})Ui.CoverButtonHover(button);_play.Background=Ui.CoverBrush(14,ActualTheme);}
    private void SetControls(bool hover)
    {
        var show=hover&&!_lite&&App.Instance.Track!=null;if(show==_hoverControls)return;_hoverControls=show;
        _controlRail.IsHitTestVisible=show;Ui.Fade(_controlRail,show?1:0,App.Instance.Prefs.Animations,.18);
        _marquee.SetReserved(show?84:0,App.Instance.Prefs.Animations);
        var visual=Microsoft.UI.Xaml.Hosting.ElementCompositionPreview.GetElementVisual(_artist);
        if(_artistClip==null){_artistClip=visual.Compositor.CreateInsetClip();visual.Clip=_artistClip;}
        if(!App.Instance.Prefs.Animations){_artistClip.StopAnimation("RightInset");_artistClip.RightInset=show?84:0;}
        else{var clip=visual.Compositor.CreateScalarKeyFrameAnimation();clip.InsertKeyFrame(1,show?84:0);clip.Duration=TimeSpan.FromMilliseconds(260);_artistClip.StartAnimation("RightInset",clip);}
    }
    private void UpdateRowWidth(){if(ActualWidth>0)_row.Width=Math.Max(0,ActualWidth-_open.Padding.Left-_open.Padding.Right);}
    internal void SetLite(bool lite){if(lite)SetControls(false);_lite=lite;if(_coverFrame.RenderTransform is TranslateTransform shift)shift.X=lite?0:2;_row.ColumnSpacing=lite?0:NameColumnSpacing;_row.ColumnDefinitions[1].Width=lite?new GridLength(0):new GridLength(1,GridUnitType.Star);_names.Visibility=lite?Visibility.Collapsed:Visibility.Visible;_open.Padding=new Thickness(lite?10:_large?5:9,0,lite?10:8,0);UpdateRowWidth();_marquee.SetEnabled(!lite);}
    internal async void Refresh(){var a=App.Instance;var t=a.Track;StyleControls();_previous.IsEnabled=t?.Controls?.Previous==true;_play.IsEnabled=t?.Controls?.Toggle==true;_next.IsEnabled=t?.Controls?.Next==true;if(_play.Content is FontIcon playIcon)playIcon.Glyph=t?.Playing==true?"\uE769":"\uE768";if(t==null)SetControls(false);_title.Text=t?.Title??"暂无音乐";_artist.Text=t?.Artist??"打开播放器开始播放";_marquee.SetEnabled(!_lite);Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(_open,t==null?"暂无音乐":$"{t.Title} · {t.Artist}");_album.Refresh();_spectrum.Visibility=a.Prefs.Spectrum&&!_lite?Visibility.Visible:Visibility.Collapsed;if(_key==t?.Key&&ReferenceEquals(_coverBytes,t?.Cover))return;_key=t?.Key;_coverBytes=t?.Cover;var revision=++_artworkRevision;_cover.Source=null;if(t?.Cover is {} bytes){using var stream=new InMemoryRandomAccessStream();using(var writer=new DataWriter(stream)){writer.WriteBytes(bytes);await writer.StoreAsync();writer.DetachStream();}stream.Seek(0);var bitmap=new BitmapImage{DecodePixelWidth=_large?80:56};await bitmap.SetSourceAsync(stream);if(revision==_artworkRevision&&_key==t.Key)_cover.Source=bitmap;}}
    internal void SetActive(bool active){_marquee.SetActive(active);_album.SetActive(active);}
    internal void UpdateSpectrum(){if(App.Instance.Prefs.Spectrum&&!_lite)_spectrum.Update(App.Instance.Spectrum.Levels(),App.Instance.Track?.Playing==true,App.Instance.Prefs.Sensitivity,ActualTheme==ElementTheme.Dark?ColorHelper.FromArgb(255,240,221,236):App.Instance.Track?.Accent??Ui.Teal);}
}
internal sealed class CapsuleWindow:SurfaceWindow
{
    private readonly WeatherAtmosphere? _weatherLight;private readonly bool _music;private readonly MusicCapsuleContent? _musicContent;private readonly WeatherGlyph _glyph=new(size:26);private readonly TextBlock _temperature=Ui.Text("",15),_rainValue=Ui.Text("",11,.85);private readonly StackPanel _weatherRow=Ui.Row(4),_rainGroup=Ui.Row(2);private readonly Viewbox _weatherArt=new(){Width=24,Height=24};private readonly Border _surface=new(){CornerRadius=new CornerRadius(CapsuleGeometry.Radius),IsHitTestVisible=false};private bool _present,_lite,_pointer;internal bool Fading;internal bool IsLite=>_lite;internal bool WantsHover=>_pointer&&(!_music||_lite&&App.Instance.Track!=null);private HoverPreviewWindow Preview=>_music?App.Instance.MusicHover:App.Instance.WeatherHover;
    internal CapsuleWindow(bool music):base(music?"LumiBar · 音乐胶囊":"LumiBar · 天气胶囊",true){_music=music;_surface.Margin=new Thickness(CapsuleGeometry.Gutter,0,CapsuleGeometry.Gutter,0);_surface.Height=CapsuleGeometry.Height;_surface.VerticalAlignment=VerticalAlignment.Center;_surface.UseLayoutRounding=false;Root.Children.Insert(0,_surface);Root.Children.Add(new CapsuleEdge());Root.ActualThemeChanged+=(_,_)=>Surface();Surface();Body.PointerEntered+=(_,_)=>{_pointer=true;if(WantsHover)Preview.Request(this);};Body.PointerExited+=(_,_)=>{_pointer=false;Preview.LeaveOwner();};Body.PointerPressed+=(_,_)=>Preview.Cancel();if(music){_musicContent=new();SurfaceVisibilityChanged+=open=>_musicContent.SetActive(open);Body.Children.Add(_musicContent);}else{_weatherLight=new WeatherAtmosphere(LightHost,true);SurfaceVisibilityChanged+=open=>_weatherLight.SetActive(open);var row=_weatherRow;row.HorizontalAlignment=HorizontalAlignment.Center;row.VerticalAlignment=VerticalAlignment.Center;row.Height=28;_temperature.TextWrapping=_rainValue.TextWrapping=TextWrapping.NoWrap;_temperature.Height=20;_temperature.LineHeight=20;_temperature.LineStackingStrategy=LineStackingStrategy.BlockLineHeight;_rainValue.Height=16;_rainValue.LineHeight=16;_rainValue.LineStackingStrategy=LineStackingStrategy.BlockLineHeight;_weatherArt.Child=_glyph;row.Children.Add(_weatherArt);row.Children.Add(_temperature);_rainGroup.Margin=new Thickness(4,0,0,0);_rainGroup.Children.Add(new WeatherGlyph(61,true,14));_rainGroup.Children.Add(_rainValue);row.Children.Add(_rainGroup);_temperature.FontWeight=Microsoft.UI.Text.FontWeights.SemiBold;var button=new Button{Content=row,Padding=new Thickness(3,0,3,0),BorderThickness=new Thickness(0),Background=Ui.Brush(Colors.Transparent),CornerRadius=new CornerRadius(CapsuleGeometry.Radius),HorizontalContentAlignment=HorizontalAlignment.Center,VerticalContentAlignment=VerticalAlignment.Center,Height=34,MinHeight=0,HorizontalAlignment=HorizontalAlignment.Stretch};PopupToggle.Bind(button,()=>App.Instance.WeatherWindow,()=>App.Instance.OpenWeather());Body.Children.Add(button);}Refresh();}
    private void Surface()=>_surface.Background=Ui.Brush(Root.ActualTheme==ElementTheme.Light?ColorHelper.FromArgb(55,240,246,255):ColorHelper.FromArgb(30,188,199,224));
    internal void Refresh(){if(_music)_musicContent!.Refresh();else{_weatherLight?.Update(App.Instance.Forecast?.Code??0,App.Instance.Forecast?.IsDay,App.Instance.Prefs.Animations);_glyph.Visibility=App.Instance.Forecast==null?Visibility.Collapsed:Visibility.Visible;_glyph.Update(App.Instance.Forecast?.Code??0,App.Instance.Forecast?.IsDay);_temperature.Text=App.Instance.Forecast==null?"—":App.Instance.Temperature(App.Instance.Forecast.Temperature);_rainValue.Text=App.Instance.Forecast?.Rain is {} probability?$"{probability:0}%":"—";}}
    internal void SetLite(bool lite){var changed=_lite!=lite;_lite=lite;if(!_music){_rainGroup.Visibility=lite?Visibility.Collapsed:Visibility.Visible;_weatherArt.Width=_weatherArt.Height=lite?22:24;_weatherRow.Spacing=lite?3:4;}_musicContent?.SetLite(lite);if(changed&&App.Instance.MusicHover!=null)Preview.OwnerModeChanged(this);}
    private (double Width,double Height,double Scale)? _extent;
    internal void SetExtent(double width,double? knownScale=null){var scale=knownScale??Native.ReadTaskbar()?.Scale??1;var w=Math.Round((width+CapsuleGeometry.Gutter*2)*scale)/scale;var h=Math.Round(CapsuleGeometry.OuterHeight*scale)/scale;if(_extent==(w,h,scale))return;_extent=(w,h,scale);Root.UseLayoutRounding=false;Root.Width=w;Root.Height=h;Native.RefreshChrome(this);Native.RoundCapsule(this,(int)(width*scale),(int)(34*scale));}
    internal double PreferredWeatherWidth(bool? liteMode=null)
    {
        if(_music)return App.Instance.Prefs.MusicWidth;
        var lite=liteMode??_lite;
        _temperature.Measure(new Windows.Foundation.Size(double.PositiveInfinity,28));
        _rainValue.Measure(new Windows.Foundation.Size(double.PositiveInfinity,28));
        var width=(lite?22+3:24+4)+_temperature.DesiredSize.Width+12;
        if(!lite)width+=4+4+14+2+_rainValue.DesiredSize.Width;
        return Math.Max(lite?64:104,Math.Ceiling(width));
    }    internal void ApplyGlow(){if(_music)_musicContent?.Refresh();else Refresh();}
    internal void UpdateSpectrum(){if(IsOpen)_musicContent?.UpdateSpectrum();}
    internal async void SetPresent(bool visible){_musicContent?.SetActive(visible);if(_present==visible)return;_present=visible;Fading=!visible;Ui.Fade(Root,visible?1:0,App.Instance.Prefs.Animations);if(!visible){await Task.Delay(320);if(!_present)HideSurface();Fading=false;}}
}



