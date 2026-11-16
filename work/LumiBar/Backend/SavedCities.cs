namespace LumiBar;
internal static class SavedCities
{
    internal static void BeginAutomatic(Preferences prefs)
    {
        if(prefs.City is {} previous&&!prefs.AutoLocateCity){
            if(!prefs.Favorites.Any(c=>Same(c,previous)))prefs.Favorites.Add(previous);
            while(prefs.Favorites.Count>30)prefs.Favorites.RemoveAt(prefs.Favorites.FindIndex(c=>!Same(c,previous)));
        }
        prefs.AutoLocateCity=true;
    }
    internal static bool Same(City? a,City? b)=>a!=null&&b!=null&&Math.Abs(a.Latitude-b.Latitude)<.001&&Math.Abs(a.Longitude-b.Longitude)<.001;
    internal static void Select(Preferences prefs,City city,bool automatic=false)
    {
        if(automatic){BeginAutomatic(prefs);prefs.City=city;return;}
        // Preserve the old active city even when an older profile did not save it.
        if(prefs.City is {} previous&&!prefs.AutoLocateCity&&!prefs.Favorites.Any(c=>Same(c,previous)))prefs.Favorites.Add(previous);
        var index=prefs.Favorites.FindIndex(c=>Same(c,city));
        if(index>=0)prefs.Favorites[index]=city;else prefs.Favorites.Add(city);
        while(prefs.Favorites.Count>30)prefs.Favorites.RemoveAt(prefs.Favorites.FindIndex(c=>!Same(c,city)));
        prefs.City=city;prefs.AutoLocateCity=automatic;
    }
}
