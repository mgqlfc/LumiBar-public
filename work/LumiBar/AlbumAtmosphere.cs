using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
namespace LumiBar;
internal sealed class AlbumAtmosphere:Grid
{
    private readonly SoftLightTexture _texture=new(),_secondary=new();private readonly bool _capsule;
    internal AlbumAtmosphere(bool capsule=false){_capsule=capsule;IsHitTestVisible=false;if(capsule){var mask=new RoundedLightSurface();mask.Source.Children.Add(_texture);mask.Source.Children.Add(_secondary);Children.Add(mask);}else{Children.Add(_texture);Children.Add(_secondary);}ActualThemeChanged+=(_,_)=>Refresh();}
    internal void SetActive(bool active){_texture.SetActive(active);_secondary.SetActive(active);}
    private Windows.UI.Color Soft(Windows.UI.Color color){var (r,g,b)=AlbumLightColor.Soften(color.R,color.G,color.B,ActualTheme==ElementTheme.Light,App.Instance.Prefs.CoverSaturation);return Windows.UI.Color.FromArgb(color.A,r,g,b);}
    internal void Refresh(){var a=App.Instance;var t=a.Track;var strength=(_capsule?.58:.92)*Math.Clamp(a.Prefs.Glow/.55,0,1);if(ActualTheme==ElementTheme.Light)strength*=.8;if(!a.Prefs.CoverGlow||t?.Cover==null)strength=0;_texture.Update([new(Soft(t?.Accent??Ui.Teal),.3,.78,_capsule?.8:.58,_capsule?1.5:.40,strength)],a.Prefs.Animations,3,-2,17);_secondary.Update([new(Soft(t?.SecondaryAccent??t?.Accent??Ui.Teal),.88,.8,_capsule?.7:.53,_capsule?1.5:.44,strength*.9)],a.Prefs.Animations,-4,2,21);}
}
