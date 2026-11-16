using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Foundation;

namespace LumiBar;
/// <summary>The user-approved 4×4 sculpted icon atlas. Lighting lives in the weather UI, not here.</summary>
internal sealed class WeatherGlyph : Canvas
{
    private static BitmapImage? _atlas;
    private readonly Image _image;
    private readonly double _size;
    internal WeatherGlyph(int code=0,bool? isDay=true,double size=24)
    {
        _size=size;Width=size;Height=size;IsHitTestVisible=false;
        Clip=new RectangleGeometry{Rect=new Rect(0,0,size,size)};
        _atlas??=new BitmapImage(new Uri("ms-appx:///Assets/weather-icons-clean.png"));
        _image=new Image{Source=_atlas,Width=size*4,Height=size*4,Stretch=Stretch.Fill};
        Children.Add(_image);Update(code,isDay);
    }
    internal void Update(int code,bool? isDay)
    {
        var cell=code switch{
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
        SetLeft(_image,-cell%4*_size);SetTop(_image,-(cell/4)*_size);
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(this,WeatherNames.Describe(code));
    }
}
