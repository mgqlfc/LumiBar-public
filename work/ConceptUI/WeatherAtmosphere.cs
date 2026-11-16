using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.UI;
namespace LumiBar;
internal sealed class WeatherAtmosphere
{
    private readonly Grid _host;private readonly SoftLightTexture _texture=new();private readonly bool _capsule;private int _code;private bool? _day;private bool _animate;
    internal WeatherAtmosphere(Grid host,bool capsule=false){_host=host;_capsule=capsule;host.Children.Add(_texture);host.ActualThemeChanged+=(_,_)=>Paint();}
    internal void Update(int code,bool? day,bool animate){_code=code;_day=day;_animate=animate;Paint();}
    internal void SetActive(bool active)=>_texture.SetActive(active);
    private void Paint(){var(a,b)=_code switch{WeatherNames.Haze=>(Rgb(180,164,126),Rgb(151,162,165)),WeatherNames.DustStorm=>(Rgb(224,169,87),Rgb(183,125,69)),>=95=>(Rgb(165,121,234),Rgb(87,144,238)),71 or 73 or 75 or 77 or 85 or 86=>(Rgb(125,211,238),Rgb(171,184,234)),45 or 48=>(Rgb(142,184,207),Rgb(151,191,208)),>=51 and <=82=>(Rgb(65,181,239),Rgb(72,134,219)),3=>(Rgb(136,186,221),Rgb(124,167,203)),2 when _day!=false=>(Rgb(250,202,113),Rgb(151,202,225)),_ when _day==false=>(Rgb(85,125,244),Rgb(147,139,235)),_=>(Rgb(255,202,91),Rgb(255,173,78))};var strength=_host.ActualTheme==ElementTheme.Dark?.4:.3;_texture.Update(_capsule?[new(a,.22,.65,.54,1.2,strength*1.15),new(b,.86,.3,.48,1.1,strength*.85)]:[new(a,.5,.5,.33,.38,strength),new(b,.53,.48,.44,.46,strength*.45)],_animate,_code==WeatherNames.DustStorm?5:3,_code is 45 or 48 or WeatherNames.Haze?0:-2,_code==WeatherNames.DustStorm?10:_code is 45 or 48 or WeatherNames.Haze?22:15);}
    private static Color Rgb(byte r,byte g,byte b)=>ColorHelper.FromArgb(255,r,g,b);
}
