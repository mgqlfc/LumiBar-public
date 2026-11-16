using System.Text.Json;
using LumiBar;

internal static class AppearanceCityTests
{
    internal static List<string> Run()
    {
        var results=new List<string>();
        void Check(bool value,string name)=>results.Add($"{(value?"PASS":"FAIL")} {name}");
        static double Chroma((byte R,byte G,byte B) c)=>Math.Max(c.R,Math.Max(c.G,c.B))-Math.Min(c.R,Math.Min(c.G,c.B));
        foreach(var light in new[]{false,true})
        {
            var gray=AlbumLightColor.Soften(220,54,91,light,0);
            var soft=AlbumLightColor.Soften(220,54,91,light,.35);
            var vivid=AlbumLightColor.Soften(220,54,91,light,1);
            Check(gray.R==gray.G&&gray.G==gray.B,$"Zero cover saturation gives neutral atmosphere light={light}");
            Check(Chroma(gray)<Chroma(soft)&&Chroma(soft)<Chroma(vivid),$"Cover saturation slider increases chroma without changing artwork light={light}");
            Check(soft.R>soft.B&&soft.B>soft.G&&vivid.R>vivid.B&&vivid.B>vivid.G,$"Cover saturation preserves sampled hue light={light}");
            Check(AlbumLightColor.Soften(220,54,91,light,-1)==gray&&AlbumLightColor.Soften(220,54,91,light,2)==vivid,$"Out-of-range saturation clamps safely light={light}");
            Check(AlbumLightColor.Soften(220,54,91,light,double.NaN)==soft,$"Non-finite saturation uses default light={light}");
        }

        static byte[] Pixels(params (PaletteRgb Color,int Count,byte Alpha)[] blocks)
        {
            var pixels=new List<byte>();
            foreach(var (color,count,alpha) in blocks)for(var i=0;i<count;i++)pixels.AddRange([color.R,color.G,color.B,alpha]);
            return pixels.ToArray();
        }
        var coral=new PaletteRgb(216,72,88);var rose=new PaletteRgb(184,88,120);var blue=new PaletteRgb(30,100,220);
        var paired=CoverPalette.Extract(Pixels((coral,100,255),(rose,40,255),(blue,20,255)));
        Check(paired.Primary==coral&&paired.Secondary==rose,"Cover palette samples two actual nearby artwork colors and excludes unrelated blue");
        var ignored=CoverPalette.Extract(Pixels((coral,50,255),(blue,1000,0),(new(0,0,0),2000,255),(new(255,255,255),2000,255)));
        Check(ignored.Primary==coral,"Transparent pixels and black/white borders cannot dominate artwork color");
        var flat=CoverPalette.Extract(Pixels((coral,100,255)));
        Check(flat.Primary==coral&&flat.Secondary!=coral&&CoverPalette.HueDistance(flat.Primary,flat.Secondary)<2,"Flat artwork creates a different tone with the same hue");
        var wrap=CoverPalette.Extract(Pixels((new(220,40,55),100,255),(new(220,65,40),50,255)));
        Check(wrap.Primary!=wrap.Secondary&&CoverPalette.HueDistance(wrap.Primary,wrap.Secondary)<20,"Nearby reds across the hue-wheel boundary form one palette");
        Check(CoverPalette.Extract([])==CoverPalette.Extract([1,2,3]),"Missing and incomplete artwork pixels use a stable fallback");
        var brightSpot=CoverPalette.Extract(Pixels((new(44,46,49),10000,255),(new(80,20,30),3000,255),(coral,16,255),(rose,6,255)));
        Check(brightSpot.Primary==coral,"Small bright artwork region wins over large dark-gray and dark-color backgrounds");
        foreach(var neutral in new[]{new PaletteRgb(0,0,0),new PaletteRgb(36,36,36),new PaletteRgb(128,128,128),new PaletteRgb(255,255,255),new PaletteRgb(104,108,112)})
        {
            var fallback=CoverPalette.Extract(Pixels((neutral,1024,255)));
            Check(fallback==CoverPalette.Extract([])&&fallback.Primary!=fallback.Secondary&&
                Chroma((fallback.Primary.R,fallback.Primary.G,fallback.Primary.B))>=40&&
                Chroma((fallback.Secondary.R,fallback.Secondary.G,fallback.Secondary.B))>=40&&
                CoverPalette.HueDistance(fallback.Primary,fallback.Secondary)<=60,$"Monochrome cover {neutral} uses a soft colorful nearby pair");
        }
        foreach(var color in new[]{new PaletteRgb(230,45,65),new PaletteRgb(35,210,220),new PaletteRgb(235,205,45)})
        {
            var vibrant=CoverPalette.Extract(Pixels((new(52,52,52),1000,255),(color,24,255)));
            Check(vibrant.Primary==color&&CoverPalette.HueDistance(vibrant.Primary,vibrant.Secondary)<2&&vibrant.Primary!=vibrant.Secondary,$"Red cyan or yellow artwork keeps its recognizable hue and a second tone: {color}");
        }
        foreach(var color in new[]{new PaletteRgb(90,30,45),new PaletteRgb(20,80,90),new PaletteRgb(90,75,20)})
        {
            var lifted=CoverPalette.Extract(Pixels((color,1024,255)));
            Check(Math.Max(lifted.Primary.R,Math.Max(lifted.Primary.G,lifted.Primary.B))>=176&&
                CoverPalette.HueDistance(color,lifted.Primary)<2&&CoverPalette.HueDistance(lifted.Primary,lifted.Secondary)<2,
                $"Mid-dark colorful cover lifts brightness without shifting hue: {color}");
        }

        var oldCity=new City("Yangcun",39.36389,117.06028,"天津市 · 中国","Asia/Shanghai");
        var newCity=new City("奎文区",36.707,119.132,"山东省 · 潍坊市 · 中国","Asia/Shanghai");
        var prefs=new Preferences{City=oldCity,AutoLocateCity=false};
        SavedCities.Select(prefs,newCity);
        Check(prefs.City==newCity&&prefs.Favorites.SequenceEqual([oldCity,newCity])&&!prefs.AutoLocateCity,"Selecting a city preserves the previous active city from an older profile");
        for(var i=0;i<20;i++){SavedCities.Select(prefs,oldCity);SavedCities.Select(prefs,newCity);}
        Check(prefs.Favorites.Count==2,"Repeated saved-city switching never duplicates favorites");
        var renamed=newCity with{Name="潍坊市奎文区",Latitude=newCity.Latitude+.0001};
        SavedCities.Select(prefs,renamed);
        Check(prefs.Favorites.Count==2&&prefs.Favorites.Any(c=>c.Name==renamed.Name),"Nearby coordinates update a saved city's name instead of adding duplicate chips");
        var favoritesBefore=prefs.Favorites.ToArray();
        for(var i=0;i<20;i++)SavedCities.Select(prefs,new City("GPS 位置",36.7+i*.002,119.1),true);
        Check(prefs.AutoLocateCity&&prefs.Favorites.SequenceEqual(favoritesBefore),"Repeated GPS fixes do not fill the saved-city strip with coordinate drift");
        SavedCities.Select(prefs,oldCity);
        Check(!prefs.AutoLocateCity&&prefs.City==oldCity&&prefs.Favorites.Count==2,"Clicking a saved city turns off automatic GPS selection without saving the last GPS fix");
        var capacity=new Preferences{City=oldCity,AutoLocateCity=false,Favorites=Enumerable.Range(0,30).Select(i=>new City($"保存城市 {i}",20+i*.1,100)).ToList()};
        SavedCities.Select(capacity,newCity);capacity.Normalize();
        Check(capacity.Favorites.Count==30&&capacity.Favorites.Contains(newCity)&&capacity.Favorites.Contains(oldCity),"Full saved-city list retains newly selected city and legacy active city after normalization");
        var automaticCapacity=new Preferences{City=oldCity,AutoLocateCity=false,Favorites=Enumerable.Range(0,30).Select(i=>new City($"保存城市 {i}",20+i*.1,100)).ToList()};
        SavedCities.Select(automaticCapacity,newCity,true);automaticCapacity.Normalize();
        Check(automaticCapacity.AutoLocateCity&&automaticCapacity.Favorites.Count==30&&automaticCapacity.Favorites.Contains(oldCity)&&!automaticCapacity.Favorites.Contains(newCity),"Enabling GPS with a full saved-city list preserves the previous manual city without saving GPS drift");

        using(var named=JsonDocument.Parse("""{"lat":"99","lon":"99","address":{"city_district":"奎文区","city":"潍坊市","state":"山东省","country":"中国","country_code":"cn"}}"""))
        {
            var city=GpsCityName.Resolve(named.RootElement,36.707,119.132);
            Check(city.Name=="奎文区"&&city.Region=="山东省 · 潍坊市 · 中国"&&city.TimeZone=="Asia/Shanghai","GPS naming prefers district while retaining region and China timezone");
            Check(city.Latitude==36.707&&city.Longitude==119.132,"Reverse geocoder labels never replace the satellite coordinates");
        }
        using(var town=JsonDocument.Parse("""{"address":{"town":"杨村街道","city":"天津市","state":"天津市","country":"中国","country_code":"cn"}}"""))
        {
            var city=GpsCityName.Resolve(town.RootElement,39.36,117.06);
            Check(city.Name=="杨村街道"&&city.Region=="天津市 · 中国","GPS town names retain useful precision and deduplicate municipality region");
        }
        using(var unknown=JsonDocument.Parse("{}"))
        {
            var city=GpsCityName.Resolve(unknown.RootElement,39.36,117.06);
            Check(city.Name=="当前位置"&&city.Latitude==39.36&&city.Longitude==117.06,"No reverse-geocode result preserves usable Windows position with a neutral label");
        }

        var previousPath=Environment.GetEnvironmentVariable("LUMIBAR_DATA_DIR");
        var folder=Path.Combine(AppContext.BaseDirectory,"test-data","appearance-cities-"+Guid.NewGuid().ToString("N"));
        try
        {
            Environment.SetEnvironmentVariable("LUMIBAR_DATA_DIR",folder);
            prefs.CoverSaturation=.67;SavedCities.Select(prefs,newCity,true);prefs.Save();
            var loaded=Preferences.Load();
            Check(loaded.CoverSaturation==.67&&loaded.AutoLocateCity&&loaded.City==newCity&&loaded.Favorites.SequenceEqual(prefs.Favorites),"Saturation, GPS selection and saved cities survive atomic settings save and reload");
            foreach(var (input,expected) in new[]{(-1d,0d),(2d,1d),(double.NaN,new Preferences().CoverSaturation),(double.PositiveInfinity,new Preferences().CoverSaturation)})
            {
                var invalid=new Preferences{CoverSaturation=input};invalid.Save();
                Check(Preferences.Load().CoverSaturation==expected,$"Invalid persisted saturation {input} normalizes safely");
            }
        }
        finally {Environment.SetEnvironmentVariable("LUMIBAR_DATA_DIR",previousPath);}
        return results;
    }
}
