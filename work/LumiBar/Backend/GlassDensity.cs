namespace LumiBar;
internal static class GlassDensity
{
    internal const double Minimum=.35,Maximum=.95;
    // Keep the native acrylic graph stable. A neutral compositor wash supplies
    // the remaining density, without retargeting the controller's crossfades.
    internal static double WashOpacity(double density,bool light)
    {
        density=double.IsFinite(density)?Math.Clamp(density,Minimum,Maximum):.62;
        var baseline=light?.38+Minimum*.32:.3+Minimum*.45;
        var slope=light?.32:.45;
        return slope*(density-Minimum)/(1-baseline);
    }
}
