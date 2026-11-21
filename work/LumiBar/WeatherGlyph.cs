using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Foundation;
using Windows.Graphics.Imaging;
using Windows.Storage.Streams;

namespace LumiBar;
/// <summary>The user-approved 4×4 sculpted icon atlas. Lighting lives in the weather UI, not here.</summary>
internal sealed class WeatherGlyph : Canvas
{
    private static BitmapImage? _atlas;
    private sealed record AtlasGeometry(WeatherArtworkBounds[] Bounds,double CellWidth,double CellHeight);
    private static Task<AtlasGeometry?>? _geometryTask;
    private static AtlasGeometry? _geometry;
    private readonly Image _image;
    private readonly RectangleGeometry _cellClip=new();
    private readonly double _size;
    private int _cell;
    internal WeatherGlyph(int code=0,bool? isDay=true,double size=24)
    {
        _size=size;Width=size;Height=size;IsHitTestVisible=false;
        Clip=new RectangleGeometry{Rect=new Rect(0,0,size,size)};
        _atlas??=new BitmapImage(new Uri("ms-appx:///Assets/weather-icons-clean.png"));
        _image=new Image{Source=_atlas,Width=size*4,Height=size*4,Stretch=Stretch.Fill};
        _image.Clip=_cellClip;
        Children.Add(_image);Update(code,isDay);
        if(_geometry==null){_image.Opacity=0;_=LoadGeometry();}
    }
    internal void Update(int code,bool? isDay)
    {
        _cell=code switch{
            0 or 1=>isDay==false?1:0,
            2=>isDay==false?3:2,
            3=>5,
            45 or 48 or WeatherNames.Haze=>12,
            WeatherNames.DustStorm=>13,
            56 or 57 or 66 or 67=>10,
            71 or 73 or 75 or 77 or 85 or 86=>9,
            95 or 96 or 99=>11,
            65 or 82=>7,
            >=51 and <=82=>6,
            _=>4
        };
        PositionArtwork();
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(this,WeatherNames.Describe(code));
    }
    private void PositionArtwork()
    {
        var col=_cell%4;var row=_cell/4;
        var offset=_geometry is {} geometry?geometry.Bounds[_cell].CenterOffset(geometry.CellWidth,geometry.CellHeight,_size):(X:0d,Y:0d);
        // Clip in the atlas image's local coordinates before shifting its cell.
        _cellClip.Rect=new Rect(col*_size,row*_size,_size,_size);
        SetLeft(_image,-col*_size+offset.X);SetTop(_image,-row*_size+offset.Y);
    }
    private async Task LoadGeometry()
    {
        _geometry=await(_geometryTask??=ReadGeometry());PositionArtwork();_image.Opacity=1;
    }
    private static async Task<AtlasGeometry?> ReadGeometry()
    {
        try{
            var bytes=await File.ReadAllBytesAsync(Path.Combine(AppContext.BaseDirectory,"Assets","weather-icons-clean.png"));
            using var stream=new InMemoryRandomAccessStream();
            using(var writer=new DataWriter(stream)){writer.WriteBytes(bytes);await writer.StoreAsync();writer.DetachStream();}
            stream.Seek(0);var decoder=await BitmapDecoder.CreateAsync(stream);
            var pixels=(await decoder.GetPixelDataAsync(BitmapPixelFormat.Bgra8,BitmapAlphaMode.Premultiplied,new BitmapTransform(),ExifOrientationMode.IgnoreExifOrientation,ColorManagementMode.DoNotColorManage)).DetachPixelData();
            var width=checked((int)decoder.PixelWidth);var height=checked((int)decoder.PixelHeight);
            var bounds=await Task.Run(()=>WeatherArtworkGeometry.Measure(pixels,width,height));
            return new(bounds,width/4d,height/4d);
        }catch(Exception ex){App.Log("weather-artwork",ex);return null;}
    }
}
