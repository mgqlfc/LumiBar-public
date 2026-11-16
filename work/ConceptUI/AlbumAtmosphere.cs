using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
namespace LumiBar;
internal sealed class AlbumAtmosphere:Grid
{
    private readonly SoftLightTexture _texture=new();private readonly bool _capsule;
    internal AlbumAtmosphere(bool capsule=false){_capsule=capsule;IsHitTestVisible=false;Children.Add(_texture);ActualThemeChanged+=(_,_)=>Refresh();}
    internal void SetActive(bool active)=>_texture.SetActive(active);
    internal void Refresh(){var a=App.Instance;var t=a.Track;var strength=(_capsule?.3:.65)+a.Prefs.Glow*.2;if(ActualTheme==ElementTheme.Light)strength*=.8;if(!a.Prefs.CoverGlow||t?.Cover==null)strength=0;_texture.Update([new(t?.Accent??Ui.Teal,.3,.78,_capsule?.8:.58,_capsule?1.5:.40,strength),new(t?.SecondaryAccent??t?.Accent??Ui.Teal,.88,.8,_capsule?.7:.53,_capsule?1.5:.44,strength*.9)],a.Prefs.Animations);}
}
