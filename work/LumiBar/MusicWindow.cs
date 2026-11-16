using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Storage.Streams;
namespace LumiBar;
internal sealed class MusicWindow:SurfaceWindow
{
    private readonly LinearGradientBrush _progressColor=new(){StartPoint=new(0,0),EndPoint=new(1,0),GradientStops={new GradientStop{Offset=0},new GradientStop{Offset=1}}};
    private readonly MarqueeTitle _titleMarquee;
    private readonly Button _lyricsSlower,_lyricsFaster,_moreButton;
    private readonly FadingLyricsHost _lyricsHost;private readonly StackPanel _lyrics=Ui.Stack(32);private readonly ScrollViewer _scroll=new();private readonly TextBlock _status=Ui.Text("歌词",11,.7),_title=Ui.Text("",19),_artist=Ui.Text("",13,.8),_albumName=Ui.Text("",11,.65),_time=Ui.Text("",12,.8);private readonly Image _cover=new(){Width=58,Height=58,Stretch=Stretch.UniformToFill};private readonly Slider _seek=new(){Minimum=0,Maximum=222,MinHeight=24};private readonly Button _play,_previous,_next;private readonly AlbumAtmosphere _atmosphere=new();private readonly SpectrumBackdrop _spectrum=new(){Height=150,VerticalAlignment=VerticalAlignment.Bottom,Margin=new Thickness(24,0,24,100),Opacity=.30};private readonly List<TextBlock> _lineViews=[];private bool _updating,_dragging;private int _active=-2;private bool _needsCenter=true,_allLyrics;
    internal MusicWindow():base("LumiBar · 音乐")
    {
        foreach(var key in new[]{"SliderTrackValueFill","SliderTrackValueFillPointerOver","SliderTrackValueFillPressed","SliderThumbBackground","SliderThumbBackgroundPointerOver","SliderThumbBackgroundPressed"})_seek.Resources[key]=_progressColor;
        Root.ActualThemeChanged+=(_,_)=>UpdateProgressColor();
        _lyricsHost=new(_scroll);_lyricsHost.SetFontSize(App.Instance.Prefs.LyricsFontSize);LightHost.Children.Add(_atmosphere);Body.Padding=new Thickness(22,14,22,16);Body.RowDefinitions.Add(new(){Height=GridLength.Auto});Body.RowDefinitions.Add(new(){Height=new GridLength(1,GridUnitType.Star)});Body.RowDefinitions.Add(new(){Height=GridLength.Auto});
        var header=new Grid{ColumnSpacing=8};header.ColumnDefinitions.Add(new(){Width=new GridLength(1,GridUnitType.Star)});header.ColumnDefinitions.Add(new(){Width=GridLength.Auto});header.ColumnDefinitions.Add(new(){Width=GridLength.Auto});_status.TextWrapping=TextWrapping.NoWrap;_status.TextTrimming=TextTrimming.CharacterEllipsis;_status.Margin=new Thickness(12,0,0,0);header.Children.Add(_status);
        var timing=Ui.Row(2);_lyricsSlower=TimingButton("慢 0.5s","歌词慢 0.5 秒",-.5);_lyricsFaster=TimingButton("快 0.5s","歌词快 0.5 秒",.5);timing.Children.Add(_lyricsSlower);timing.Children.Add(_lyricsFaster);Grid.SetColumn(timing,1);header.Children.Add(timing);_moreButton=CreateMoreButton();Grid.SetColumn(_moreButton,2);header.Children.Add(_moreButton);Body.Children.Add(header);
        _lyrics.Padding=new Thickness(12,16,8,16);_scroll.Margin=new Thickness(0);_scroll.SizeChanged+=(_,_)=>{_active=-2;UpdatePosition();};_scroll.Content=_lyrics;_scroll.VerticalScrollBarVisibility=ScrollBarVisibility.Hidden;_scroll.HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled;_scroll.HorizontalScrollMode=ScrollMode.Disabled;_scroll.HorizontalContentAlignment=HorizontalAlignment.Stretch;Grid.SetRow(_lyricsHost,1);Body.Children.Add(_lyricsHost);
        var footer=Ui.Stack(6);var bottom=new Grid{ColumnSpacing=12};bottom.ColumnDefinitions.Add(new(){Width=new GridLength(58)});bottom.ColumnDefinitions.Add(new(){Width=new GridLength(1,GridUnitType.Star)});bottom.ColumnDefinitions.Add(new(){Width=GridLength.Auto});var art=new Border{Child=_cover,Width=58,Height=58,CornerRadius=new CornerRadius(11),Background=Ui.Brush(ColorHelper.FromArgb(25,128,128,128))};Ui.ClipRounded(art,11);bottom.Children.Add(art);
        _title.FontWeight=Microsoft.UI.Text.FontWeights.SemiBold;_title.TextWrapping=TextWrapping.NoWrap;_artist.TextTrimming=TextTrimming.CharacterEllipsis;_artist.TextWrapping=TextWrapping.NoWrap;var names=Ui.Stack(4);_titleMarquee=new(_title);_titleMarquee.SetActive(false);names.Children.Add(_titleMarquee);_artist.Margin=_albumName.Margin=new Thickness(8,0,0,0);names.Children.Add(_artist);_albumName.TextWrapping=TextWrapping.NoWrap;_albumName.TextTrimming=TextTrimming.CharacterEllipsis;names.Children.Add(_albumName);Grid.SetColumn(names,1);bottom.Children.Add(names);
        var controls=Ui.Row(2);_previous=Ui.IconButton("\uE892","上一首",async(_,_)=>await App.Instance.PreviousTrack());_play=Ui.IconButton("\uE768","播放 / 暂停",async(_,_)=>await App.Instance.TogglePlayback());_next=Ui.IconButton("\uE893","下一首",async(_,_)=>await App.Instance.NextTrack());_play.Width=42;_play.Height=42;_play.CornerRadius=new CornerRadius(21);_play.Background=Ui.Brush(ColorHelper.FromArgb(42,192,195,220));controls.Children.Add(_previous);controls.Children.Add(_play);controls.Children.Add(_next);Grid.SetColumn(controls,2);bottom.Children.Add(controls);footer.Children.Add(bottom);
        var progress=new Grid{ColumnSpacing=12};progress.ColumnDefinitions.Add(new(){Width=new GridLength(1,GridUnitType.Star)});progress.ColumnDefinitions.Add(new(){Width=GridLength.Auto});progress.Children.Add(_seek);Grid.SetColumn(_time,1);progress.Children.Add(_time);footer.Children.Add(progress);Grid.SetRow(footer,2);Body.Children.Add(footer);
        // Share a stacking context: lyrics < spectrum < source / controls.
        // Compensate for Body.Padding to retain the spectrum's old root bounds.
        _spectrum.Margin=new Thickness(2,0,2,84);Grid.SetRowSpan(_spectrum,3);Body.Children.Add(_spectrum);
        Canvas.SetZIndex(_lyricsHost,0);Canvas.SetZIndex(_spectrum,1);Canvas.SetZIndex(header,2);Canvas.SetZIndex(footer,2);
        _seek.AddHandler(UIElement.PointerPressedEvent,new Microsoft.UI.Xaml.Input.PointerEventHandler((_,_)=>_dragging=true),true);_seek.AddHandler(UIElement.PointerReleasedEvent,new Microsoft.UI.Xaml.Input.PointerEventHandler(async(_,_)=>{_dragging=false;await App.Instance.SeekPlayback(_seek.Value);}),true);_seek.PointerCaptureLost+=(_,_)=>_dragging=false;_seek.ValueChanged+=async(_,_)=>{if(!_updating&&!_dragging&&_seek.FocusState==FocusState.Keyboard)await App.Instance.SeekPlayback(_seek.Value);};SurfaceVisibilityChanged+=open=>{_atmosphere.SetActive(open);_titleMarquee.SetActive(open);if(open){_active=-2;UpdatePosition();}};
    }
    private Button CreateMoreButton()
    {
        var button=Ui.IconButton("\uE712","更多",(_,_)=>{});var menu=new MenuFlyout();button.Flyout=menu;
        void Item(string label,Action action){var item=new MenuFlyoutItem{Text=label};item.Click+=(_,_)=>action();menu.Items.Add(item);}
        Item("切换歌词聚焦",()=>{_allLyrics=!_allLyrics;_active=-2;UpdatePosition();});
        var sync=new MenuFlyoutSubItem{Text="歌词同步"};menu.Items.Add(sync);
        void TimingItem(string label,Action action){var item=new MenuFlyoutItem{Text=label};item.Click+=(_,_)=>action();sync.Items.Add(item);}
        string? timingKey=null;
        TimingItem("提前 0.5 秒",()=>AdjustLyrics(App.Instance.CurrentLyricsOffset+.5,timingKey));
        TimingItem("延后 0.5 秒",()=>AdjustLyrics(App.Instance.CurrentLyricsOffset-.5,timingKey));
        TimingItem("输入偏移秒数…",()=>DispatcherQueue.TryEnqueue(()=>ShowLyricTiming(button)));
        TimingItem("恢复原始时间",()=>AdjustLyrics(0,timingKey));
        Item("导入 LRC 歌词…",()=>_=ImportLyrics());
        menu.Opened+=(_,_)=>{timingKey=App.Instance.Track?.Key;sync.IsEnabled=timingKey!=null;var offset=App.Instance.CurrentLyricsOffset;sync.Text=offset==0?"本曲歌词同步 · 原始时间":$"本曲歌词同步 · {(offset>0?"提前":"延后")} {Math.Abs(offset):0.##} 秒";};
        Item("从头播放",()=>_=App.Instance.SeekPlayback(0));Item("音乐设置",App.Instance.OpenSettings);menu.Items.Add(new MenuFlyoutSeparator());Item("关闭音乐浮窗",HideSurface);return button;
    }
    private Button TimingButton(string text,string label,double delta)
    {
        var button=new Button{Width=30,Height=30,Padding=new Thickness(5),MinHeight=0,MinWidth=0,CornerRadius=new CornerRadius(8),Background=Ui.Brush(Colors.Transparent),BorderThickness=new Thickness(0)};
        var geometry="M2,8 L14,8"+(delta>0?" M8,2 L8,14":"");
        var icon=(Microsoft.UI.Xaml.Shapes.Path)Microsoft.UI.Xaml.Markup.XamlReader.Load($"<Path xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' Data='{geometry}' StrokeThickness='1.5' StrokeStartLineCap='Round' StrokeEndLineCap='Round'/>");
        icon.Width=icon.Height=16;icon.SetBinding(Microsoft.UI.Xaml.Shapes.Shape.StrokeProperty,new Microsoft.UI.Xaml.Data.Binding{Source=button,Path=new PropertyPath("Foreground")});button.Content=icon;
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(button,label);ToolTipService.SetToolTip(button,label+" · 仅保存到本曲");Ui.SoftAccentHover(button);Ui.CoverButtonHover(button);Ui.PressFeedback(button);
        button.Click+=(_,_)=>AdjustLyrics(App.Instance.CurrentLyricsOffset+delta,App.Instance.Track?.Key);return button;
    }
    private bool AdjustLyrics(double seconds,string? trackKey)
    {
        return App.Instance.SetLyricsOffset(seconds,trackKey);
    }
    internal void RefreshLyricsTiming(){_active=-2;_needsCenter=true;UpdatePlayback();UpdatePosition();}
    private async Task ImportLyrics()
    {
        var a=App.Instance;var track=a.Track;if(track==null)return;var pinned=KeepOpen;KeepOpen=true;
        try{
            var picker=new Windows.Storage.Pickers.FileOpenPicker();picker.FileTypeFilter.Add(".lrc");
            WinRT.Interop.InitializeWithWindow.Initialize(picker,WinRT.Interop.WindowNative.GetWindowHandle(this));
            var file=await picker.PickSingleFileAsync();if(file==null)return;
            if((await file.GetBasicPropertiesAsync()).Size>1024*1024)throw new InvalidOperationException("歌词文件超过 1 MB");
            await a.Lyrics.Import(track.Key,await Windows.Storage.FileIO.ReadTextAsync(file));
            if(a.Track?.Key==track.Key)await a.LoadLyrics();
        }catch(Exception ex){a.MediaStatus=ex is InvalidOperationException?ex.Message:"歌词导入失败，请检查文件";App.Log("lyrics-import",ex);UpdatePlayback();}
        finally{KeepOpen=pinned;}
    }
    private void ShowLyricTiming(FrameworkElement anchor)
    {
        var trackKey=App.Instance.Track?.Key;if(trackKey==null)return;
        var panel=Ui.Stack(10);panel.Width=224;panel.Children.Add(Ui.Text("本曲歌词同步",18));
        var input=new NumberBox{Value=App.Instance.CurrentLyricsOffset,Minimum=-60,Maximum=60,SmallChange=.5,LargeChange=5,SpinButtonPlacementMode=NumberBoxSpinButtonPlacementMode.Inline};Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(input,"本曲歌词偏移秒数");panel.Children.Add(input);
        var status=Ui.Text("",12,.8);void RefreshStatus(){var n=App.Instance.CurrentLyricsOffset;status.Text=n==0?"原始时间":$"{(n>0?"提前":"延后")} {Math.Abs(n):0.##} 秒";}RefreshStatus();panel.Children.Add(status);panel.Children.Add(Ui.Text("正数提前，负数延后 · 仅保存到这首歌",11,.65));
        input.ValueChanged+=(_,e)=>{if(AdjustLyrics(e.NewValue,trackKey))RefreshStatus();else{input.IsEnabled=false;status.Text="歌曲已切换，请重新打开";}};
        var reset=new Button{Content="归零",HorizontalAlignment=HorizontalAlignment.Left};reset.Click+=(_,_)=>{if(AdjustLyrics(0,trackKey)){input.Value=0;RefreshStatus();}else{input.IsEnabled=false;status.Text="歌曲已切换，请重新打开";}};panel.Children.Add(reset);
        var flyout=new Flyout{Content=panel};var previous=KeepOpen;flyout.Opened+=(_,_)=>KeepOpen=true;flyout.Closed+=(_,_)=>KeepOpen=previous;flyout.ShowAt(anchor);
    }
    internal void RenderTrack(Track? t)
    {
        _title.Text=t?.Title??"暂无音乐";_artist.Text=t?.Artist??"打开播放器，开始播放喜欢的歌";
        _albumName.Text=t==null?"":string.IsNullOrWhiteSpace(t.Album)?"未知专辑":t.Album;
        _seek.IsEnabled=t?.Controls?.Seek==true;RenderArtwork(t);UpdatePlayback();RenderLyrics();
    }
    private int _artworkRevision;
    internal async void RenderArtwork(Track? t)
    {
        var revision=++_artworkRevision;_cover.Source=null;ApplyGlow();
        if(t?.Cover is not {} bytes)return;
        try{
            using var stream=new InMemoryRandomAccessStream();using(var writer=new DataWriter(stream)){writer.WriteBytes(bytes);await writer.StoreAsync();writer.DetachStream();}
            stream.Seek(0);var bitmap=new BitmapImage{DecodePixelWidth=116};await bitmap.SetSourceAsync(stream);
            if(revision==_artworkRevision&&App.Instance.Track?.Key==t.Key)_cover.Source=bitmap;
        }catch(Exception ex){App.Log("music cover",ex);}
    }

    internal void ApplyGlow(){_atmosphere.Refresh();UpdateProgressColor();_titleMarquee.SetEnabled(true);foreach(var button in new[]{_previous,_play,_next,_lyricsSlower,_lyricsFaster,_moreButton})Ui.CoverButtonHover(button);_play.Background=Ui.CoverBrush(20,Root.ActualTheme);_spectrum.Visibility=App.Instance.Prefs.Spectrum?Visibility.Visible:Visibility.Collapsed;}
    private void UpdateProgressColor()
    {
        var track=App.Instance.Track;
        Windows.UI.Color Color(Windows.UI.Color sample)
        {
            var (r,g,b)=AlbumLightColor.Soften(sample.R,sample.G,sample.B,false,App.Instance.Prefs.CoverSaturation);
            var gain=Root.ActualTheme==ElementTheme.Light?.85:1.12;
            return Windows.UI.Color.FromArgb(255,(byte)Math.Min(255,r*gain),(byte)Math.Min(255,g*gain),(byte)Math.Min(255,b*gain));
        }
        _progressColor.GradientStops[0].Color=Color(track?.Accent??ColorHelper.FromArgb(255,137,153,217));
        _progressColor.GradientStops[1].Color=Color(track?.SecondaryAccent??ColorHelper.FromArgb(255,166,148,221));
    }
    internal void UpdatePlayback(){var a=App.Instance;var controls=a.Track?.Controls;_play.IsEnabled=controls?.Toggle==true;_previous.IsEnabled=controls?.Previous==true;_next.IsEnabled=controls?.Next==true;_seek.IsEnabled=controls?.Seek==true;_status.Text=$"{a.Media.PlayerName(a.Track?.Source)} · {MediaLabels.Lyrics(a.LyricsStatus,a.Lines.Count>0)}";_status.MaxWidth=double.PositiveInfinity;ToolTipService.SetToolTip(_status,string.Join("\n",new[]{_status.Text,a.LyricsStatus,a.MediaStatus}.Where(s=>!string.IsNullOrWhiteSpace(s))));var timed=a.Track!=null&&a.Lines.Any(line=>double.IsFinite(line.Seconds));_lyricsSlower.IsEnabled=timed&&a.CurrentLyricsOffset>-60;_lyricsFaster.IsEnabled=timed&&a.CurrentLyricsOffset<60;_status.MaxLines=1;_status.TextTrimming=TextTrimming.CharacterEllipsis;if(_play.Content is FontIcon icon)icon.Glyph=App.Instance.Track?.Playing==true?"\uE769":"\uE768";}
    internal void RenderLyrics(){UpdatePlayback();_active=-2;_needsCenter=true;_lyricsHost.SetFontSize(App.Instance.Prefs.LyricsFontSize);_lyrics.Spacing=Math.Clamp(App.Instance.Prefs.LyricsFontSize*.5,9,18);_lyricsHost.ResetScroll();_lyrics.Children.Clear();_lineViews.Clear();if(App.Instance.Track==null){_lyrics.Children.Add(Ui.Text("把喜欢的歌\n留在这一刻",30));return;}if(App.Instance.Lines.Count==0){var empty=Ui.Text(App.Instance.LyricsStatus.Contains("正在")?"正在匹配歌词…":"暂无同步歌词",22,.85);empty.TextAlignment=TextAlignment.Center;empty.Margin=new Thickness(0,56,0,0);_lyrics.Children.Add(empty);var hint=Ui.Text("更多 · 导入 LRC 歌词",12,.6);hint.TextAlignment=TextAlignment.Center;_lyrics.Children.Add(hint);return;}foreach(var line in App.Instance.Lines){var text=Ui.Text(line.Text,App.Instance.Prefs.LyricsFontSize,.5);text.FontWeight=Microsoft.UI.Text.FontWeights.SemiBold;_lyrics.Children.Add(text);_lineViews.Add(text);}DispatcherQueue.TryEnqueue(()=>{UpdatePosition();});}
    internal void UpdatePosition()
    {
        var a=App.Instance;var t=a.Track;if(t==null){_time.Text="0:00 / 0:00";return;}
        _updating=true;if(!_dragging){_seek.Maximum=t.Duration;_seek.Value=t.Position;}
        _time.Text=$"{Ui.Time(t.Position)} / {Ui.Time(t.Duration)}";_updating=false;
        var timed=a.Lines.Any(line=>double.IsFinite(line.Seconds));
        var viewport=_scroll.ViewportHeight;
        var contentWidth=_scroll.ViewportWidth;
        if(contentWidth>0&&(!double.IsFinite(_lyrics.Width)||Math.Abs(_lyrics.Width-contentWidth)>.5)){_lyrics.Width=contentWidth;_needsCenter=true;}
        var anchor=LyricLayout.ForFont(a.Prefs.LyricsFontSize).FocusTop;
        var top=timed?anchor:Math.Max(24,viewport*.24+8);
        var bottom=timed?Math.Max(16,viewport-anchor):Math.Max(24,viewport*.24+8);
        if(Math.Abs(_lyrics.Padding.Top-top)>.5||Math.Abs(_lyrics.Padding.Bottom-bottom)>.5)
        {_lyrics.Padding=new Thickness(12,top,8,bottom);_needsCenter=true;}
        var index=Lrc.Active(a.Lines,t.Position+a.CurrentLyricsOffset);
        var smoothFollow=_active>=0&&index==_active+1;
        if(index!=_active)
        {
            _active=index;_needsCenter=true;
            for(int i=0;i<_lineViews.Count;i++)
            {
                // Layout decides how many lines fit. Never collapse the surrounding
                // lyrics: small fonts and short lines can use the entire viewport.
                _lineViews[i].Opacity=1;var opacity=!timed?.78:i==index?1:_allLyrics?.64:Math.Abs(i-Math.Max(0,index))<=2?.62:.30;Ui.Fade(_lineViews[i],opacity,a.Prefs.Animations,.52);
                _lineViews[i].Visibility=Visibility.Visible;
            }
        }
        if(timed&&_needsCenter&&IsOpen&&index>=0&&index<_lineViews.Count&&_lineViews[index].ActualHeight>0&&viewport>0)
        {
            var point=_lineViews[index].TransformToVisual(_lyrics).TransformPoint(new());
            var target=Math.Clamp(point.Y-anchor,0,_scroll.ScrollableHeight);
            _lyricsHost.SetFocusSpan(point.Y-target,_lineViews[index].ActualHeight);
            if(_lyricsHost.ScrollTo(target,a.Prefs.Animations&&smoothFollow)||Math.Abs(_scroll.VerticalOffset-target)<1)_needsCenter=false;
        }
    }
    internal void UpdateSpectrum(){if(IsOpen&&App.Instance.Prefs.Spectrum)_spectrum.Update(App.Instance.Spectrum.Levels(),App.Instance.Track?.Playing==true,App.Instance.Prefs.Sensitivity,Root.ActualTheme==ElementTheme.Dark?ColorHelper.FromArgb(255,255,222,226):App.Instance.Track?.Accent??Ui.Teal);}
}





