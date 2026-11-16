using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Storage.Streams;
namespace LumiBar;
internal sealed class MusicWindow:SurfaceWindow
{
    private readonly FadingLyricsHost _lyricsHost;private readonly StackPanel _lyrics=Ui.Stack(32);private readonly ScrollViewer _scroll=new();private readonly TextBlock _status=Ui.Text("示例歌词",11,.7),_title=Ui.Text("",19),_artist=Ui.Text("",13,.8),_albumName=Ui.Text("",11,.65),_time=Ui.Text("",12,.8);private readonly Image _cover=new(){Width=58,Height=58,Stretch=Stretch.UniformToFill};private readonly Slider _seek=new(){Minimum=0,Maximum=222,MinHeight=24};private readonly Button _play,_previous,_next;private readonly AlbumAtmosphere _atmosphere=new();private readonly SpectrumBackdrop _spectrum=new(){Height=150,VerticalAlignment=VerticalAlignment.Bottom,Margin=new Thickness(24,0,24,100),Opacity=.30};private readonly List<TextBlock> _lineViews=[];private bool _updating,_dragging;private int _active=-2;private bool _needsCenter=true,_allLyrics;
    internal MusicWindow():base("LumiBar · 音乐")
    {
        _lyricsHost=new(_scroll);_lyricsHost.SetFontSize(App.Instance.Prefs.LyricsFontSize);LightHost.Children.Add(_atmosphere);LightHost.Children.Add(_spectrum);Body.Padding=new Thickness(22,14,22,16);Body.RowDefinitions.Add(new(){Height=GridLength.Auto});Body.RowDefinitions.Add(new(){Height=new GridLength(1,GridUnitType.Star)});Body.RowDefinitions.Add(new(){Height=GridLength.Auto});
        var header=new Grid();header.Children.Add(_status);var tools=Ui.Row(0);tools.HorizontalAlignment=HorizontalAlignment.Right;tools.Children.Add(CreateMoreButton());tools.Children.Add(Ui.IconButton("\uE8BB","关闭音乐浮窗",(_,_)=>HideSurface()));header.Children.Add(tools);Body.Children.Add(header);
        _lyrics.Padding=new Thickness(12,16,8,16);_scroll.Margin=new Thickness(0);_scroll.SizeChanged+=(_,_)=>{_active=-2;UpdatePosition();};_scroll.Content=_lyrics;_scroll.VerticalScrollBarVisibility=ScrollBarVisibility.Hidden;_scroll.HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled;Grid.SetRow(_lyricsHost,1);Body.Children.Add(_lyricsHost);
        var footer=Ui.Stack(6);var bottom=new Grid{ColumnSpacing=12};bottom.ColumnDefinitions.Add(new(){Width=new GridLength(58)});bottom.ColumnDefinitions.Add(new(){Width=new GridLength(1,GridUnitType.Star)});bottom.ColumnDefinitions.Add(new(){Width=GridLength.Auto});var art=new Border{Child=_cover,Width=58,Height=58,CornerRadius=new CornerRadius(11),Background=Ui.Brush(ColorHelper.FromArgb(25,128,128,128))};Ui.ClipRounded(art,11);bottom.Children.Add(art);
        _title.FontWeight=Microsoft.UI.Text.FontWeights.SemiBold;_title.TextTrimming=TextTrimming.CharacterEllipsis;_title.TextWrapping=TextWrapping.NoWrap;_artist.TextTrimming=TextTrimming.CharacterEllipsis;_artist.TextWrapping=TextWrapping.NoWrap;var names=Ui.Stack(4);names.Children.Add(_title);names.Children.Add(_artist);_albumName.TextWrapping=TextWrapping.NoWrap;_albumName.TextTrimming=TextTrimming.CharacterEllipsis;names.Children.Add(_albumName);Grid.SetColumn(names,1);bottom.Children.Add(names);
        var controls=Ui.Row(2);_previous=Ui.IconButton("\uE892","上一首",async(_,_)=>await App.Instance.DemoNext());_play=Ui.IconButton("\uE768","播放 / 暂停",async(_,_)=>await App.Instance.DemoToggle());_next=Ui.IconButton("\uE893","下一首",async(_,_)=>await App.Instance.DemoNext());_play.Width=42;_play.Height=42;_play.CornerRadius=new CornerRadius(21);_play.Background=Ui.Brush(ColorHelper.FromArgb(42,192,195,220));controls.Children.Add(_previous);controls.Children.Add(_play);controls.Children.Add(_next);Grid.SetColumn(controls,2);bottom.Children.Add(controls);footer.Children.Add(bottom);
        var progress=new Grid{ColumnSpacing=12};progress.ColumnDefinitions.Add(new(){Width=new GridLength(1,GridUnitType.Star)});progress.ColumnDefinitions.Add(new(){Width=GridLength.Auto});progress.Children.Add(_seek);Grid.SetColumn(_time,1);progress.Children.Add(_time);footer.Children.Add(progress);Grid.SetRow(footer,2);Body.Children.Add(footer);
        _seek.AddHandler(UIElement.PointerPressedEvent,new Microsoft.UI.Xaml.Input.PointerEventHandler((_,_)=>_dragging=true),true);_seek.AddHandler(UIElement.PointerReleasedEvent,new Microsoft.UI.Xaml.Input.PointerEventHandler(async(_,_)=>{_dragging=false;await App.Instance.DemoSeek(_seek.Value);}),true);_seek.PointerCaptureLost+=(_,_)=>_dragging=false;_seek.ValueChanged+=async(_,_)=>{if(!_updating&&!_dragging&&_seek.FocusState==FocusState.Keyboard)await App.Instance.DemoSeek(_seek.Value);};SurfaceVisibilityChanged+=open=>{_atmosphere.SetActive(open);if(open){_active=-2;UpdatePosition();}};
    }
    private Button CreateMoreButton()
    {
        var button=Ui.IconButton("\uE712","更多",(_,_)=>{});var menu=new MenuFlyout();button.Flyout=menu;
        void Item(string label,Action action){var item=new MenuFlyoutItem{Text=label};item.Click+=(_,_)=>action();menu.Items.Add(item);}
        Item("切换歌词聚焦",()=>{_allLyrics=!_allLyrics;_active=-2;UpdatePosition();});
        var sync=new MenuFlyoutSubItem{Text="歌词同步"};menu.Items.Add(sync);
        void TimingItem(string label,Action action){var item=new MenuFlyoutItem{Text=label};item.Click+=(_,_)=>action();sync.Items.Add(item);}
        TimingItem("提前 0.5 秒",()=>AdjustLyrics(App.Instance.Prefs.LyricsOffset+.5));
        TimingItem("延后 0.5 秒",()=>AdjustLyrics(App.Instance.Prefs.LyricsOffset-.5));
        TimingItem("输入偏移秒数…",()=>DispatcherQueue.TryEnqueue(()=>ShowLyricTiming(button)));
        TimingItem("恢复原始时间",()=>AdjustLyrics(0));
        menu.Opened+=(_,_)=>{var offset=App.Instance.Prefs.LyricsOffset;sync.Text=offset==0?"歌词同步 · 原始时间":$"歌词同步 · {(offset>0?"提前":"延后")} {Math.Abs(offset):0.##} 秒";};
        Item("重新播放示例",()=>_=App.Instance.DemoSeek(0));Item("音乐设置",App.Instance.OpenSettings);return button;
    }
    private void AdjustLyrics(double seconds)
    {
        if(!double.IsFinite(seconds))return;App.Instance.Prefs.LyricsOffset=Math.Round(Math.Clamp(seconds,-60,60),2);App.Instance.Save();_active=-2;_needsCenter=true;UpdatePosition();
    }
    private void ShowLyricTiming(FrameworkElement anchor)
    {
        var panel=Ui.Stack(10);panel.Width=224;panel.Children.Add(Ui.Text("歌词同步",18));
        var input=new NumberBox{Value=App.Instance.Prefs.LyricsOffset,Minimum=-60,Maximum=60,SmallChange=.5,LargeChange=5,SpinButtonPlacementMode=NumberBoxSpinButtonPlacementMode.Inline};Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(input,"歌词偏移秒数");panel.Children.Add(input);
        var status=Ui.Text("",12,.8);void RefreshStatus(){var n=App.Instance.Prefs.LyricsOffset;status.Text=n==0?"原始时间":$"{(n>0?"提前":"延后")} {Math.Abs(n):0.##} 秒";}RefreshStatus();panel.Children.Add(status);panel.Children.Add(Ui.Text("正数提前，负数延后 · 即时生效",11,.65));
        input.ValueChanged+=(_,e)=>{AdjustLyrics(e.NewValue);RefreshStatus();};
        var reset=new Button{Content="归零",HorizontalAlignment=HorizontalAlignment.Left};reset.Click+=(_,_)=>{input.Value=0;AdjustLyrics(0);RefreshStatus();};panel.Children.Add(reset);
        var flyout=new Flyout{Content=panel};var previous=KeepOpen;flyout.Opened+=(_,_)=>KeepOpen=true;flyout.Closed+=(_,_)=>KeepOpen=previous;flyout.ShowAt(anchor);
    }
    internal async void RenderTrack(Track? t){_title.Text=t?.Title??"暂无音乐";_artist.Text=t?.Artist??"在预览控制台恢复音乐";_albumName.Text=t==null?"":string.IsNullOrWhiteSpace(t.Album)?"未知专辑":t.Album;_cover.Source=null;_seek.IsEnabled=t!=null;_play.IsEnabled=_previous.IsEnabled=_next.IsEnabled=t!=null;if(t?.Cover is {} bytes){using var stream=new InMemoryRandomAccessStream();using(var writer=new DataWriter(stream)){writer.WriteBytes(bytes);await writer.StoreAsync();writer.DetachStream();}stream.Seek(0);var bitmap=new BitmapImage();await bitmap.SetSourceAsync(stream);if(App.Instance.Track?.Key==t.Key)_cover.Source=bitmap;}UpdatePlayback();ApplyGlow();RenderLyrics();}
    internal void ApplyGlow(){_atmosphere.Refresh();_spectrum.Visibility=App.Instance.Prefs.Spectrum?Visibility.Visible:Visibility.Collapsed;}
    internal void UpdatePlayback(){if(_play.Content is FontIcon icon)icon.Glyph=App.Instance.Track?.Playing==true?"\uE769":"\uE768";}
    internal void RenderLyrics(){_active=-2;_needsCenter=true;_lyricsHost.SetFontSize(App.Instance.Prefs.LyricsFontSize);_lyrics.Spacing=Math.Clamp(App.Instance.Prefs.LyricsFontSize*.5,9,18);_scroll.ChangeView(null,0,null,true);_lyrics.Children.Clear();_lineViews.Clear();if(App.Instance.Track==null){_lyrics.Children.Add(Ui.Text("把喜欢的歌\n留在这一刻",30));return;}foreach(var line in App.Instance.Lines){var text=Ui.Text(line.Text,App.Instance.Prefs.LyricsFontSize,.5);text.FontWeight=Microsoft.UI.Text.FontWeights.SemiBold;_lyrics.Children.Add(text);_lineViews.Add(text);}DispatcherQueue.TryEnqueue(()=>{UpdatePosition();});}
    internal void UpdatePosition()
    {
        var a=App.Instance;var t=a.Track;if(t==null){_time.Text="0:00 / 0:00";return;}
        _updating=true;if(!_dragging){_seek.Maximum=t.Duration;_seek.Value=t.Position;}
        _time.Text=$"{Ui.Time(t.Position)} / {Ui.Time(t.Duration)}";_updating=false;
        var timed=a.Lines.Any(line=>double.IsFinite(line.Seconds));
        var viewport=_scroll.ViewportHeight;
        var anchor=Math.Max(viewport*.16+8,viewport*.42-(a.Prefs.LyricsFontSize*1.3+_lyrics.Spacing));
        var top=timed?anchor:Math.Max(24,viewport*.24+8);
        var bottom=timed?Math.Max(16,viewport-anchor):Math.Max(24,viewport*.24+8);
        if(Math.Abs(_lyrics.Padding.Top-top)>.5||Math.Abs(_lyrics.Padding.Bottom-bottom)>.5)
        {_lyrics.Padding=new Thickness(12,top,8,bottom);_needsCenter=true;}
        var index=Lrc.Active(a.Lines,t.Position+a.Prefs.LyricsOffset);
        if(index!=_active)
        {
            _active=index;_needsCenter=true;
            for(int i=0;i<_lineViews.Count;i++)
            {
                // Layout decides how many lines fit. Never collapse the surrounding
                // lyrics: small fonts and short lines can use the entire viewport.
                _lineViews[i].Opacity=1;var opacity=!timed?.78:i==index?1:_allLyrics?.64:Math.Abs(i-Math.Max(0,index))<=2?.62:.30;Ui.Fade(_lineViews[i],opacity,a.Prefs.Animations,.24);
                _lineViews[i].Visibility=Visibility.Visible;
            }
        }
        if(timed&&_needsCenter&&IsOpen&&index>=0&&index<_lineViews.Count&&_lineViews[index].ActualHeight>0&&viewport>0)
        {
            var point=_lineViews[index].TransformToVisual(_lyrics).TransformPoint(new());
            var target=Math.Clamp(point.Y-anchor,0,_scroll.ScrollableHeight);
            if(_scroll.ChangeView(null,target,null,!a.Prefs.Animations)||Math.Abs(_scroll.VerticalOffset-target)<1)_needsCenter=false;
        }
    }
    internal void UpdateSpectrum(){if(IsOpen&&App.Instance.Prefs.Spectrum)_spectrum.Update(App.Instance.DemoLevels(),App.Instance.Track?.Playing==true,App.Instance.Prefs.Sensitivity,Root.ActualTheme==ElementTheme.Dark?ColorHelper.FromArgb(255,255,222,226):App.Instance.Track?.Accent??Ui.Teal);}
}





