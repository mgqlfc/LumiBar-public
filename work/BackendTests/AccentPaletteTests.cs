using LumiBar;
internal static class AccentPaletteTests
{
    internal static List<string> Run()
    {
        var results=new List<string>();void Check(bool ok,string name)=>results.Add($"{(ok?"PASS":"FAIL")} {name}");
        Check(AccentPalettes.All.Count==9&&AccentPalettes.All.Select(p=>p.Id).Distinct().Count()==9,"Nine distinct candy accents include the added yellow");
        var blue=AccentPalettes.Get("blue");Check(blue.First==0x6CE7DA&&blue.Middle==0x21BEC2&&blue.Last==0x118FB1,"Existing cyan-blue gradient remains the default palette");
        Check(AccentPalettes.Normalize(" LAVENDER ")=="lavender"&&AccentPalettes.Normalize("unknown")=="blue"&&AccentPalettes.Normalize(null)=="blue","Stored palette IDs normalize safely without losing legacy defaults");
        Check(AccentPalettes.All.All(p=>p.First!=p.Middle&&p.Middle!=p.Last&&p.Sample(0)==p.First&&p.Sample(.46)==p.Middle&&p.Sample(1)==p.Last),"Every preset has three real gradient stops with consistent interpolation");
        double Luma(uint rgb){double Linear(uint channel){var c=channel/255d;return c<=.04045?c/12.92:Math.Pow((c+.055)/1.055,2.4);}return .2126*Linear((rgb>>16)&255)+.7152*Linear((rgb>>8)&255)+.0722*Linear(rgb&255);}
        double Contrast(uint a,uint b){var x=Luma(a);var y=Luma(b);return (Math.Max(x,y)+.05)/(Math.Min(x,y)+.05);}
        Check(AccentPalettes.All.All(p=>Contrast(p.LightInk,0xFFFFFF)>=4.5&&Contrast(p.DarkInk,0x181B23)>=4.5),"All nine palettes keep readable accent text and focus tones in light and dark");
        var a=new Preferences();var b=new Preferences{AccentColor="peach"};Check(PreferenceChanges.Between(a,b).AccentOnly&&!PreferenceChanges.Between(a,b).GlassOnly,"Accent-only writes use their dedicated route instead of rebuilding glass");
        var folder=Path.Combine(AppContext.BaseDirectory,"test-data","accent-"+Guid.NewGuid().ToString("N"));var old=Environment.GetEnvironmentVariable("LUMIBAR_DATA_DIR");Directory.CreateDirectory(folder);Environment.SetEnvironmentVariable("LUMIBAR_DATA_DIR",folder);
        try{Check(Preferences.Load().AccentColor=="blue","Fresh profiles retain the existing blue accent");var prefs=new Preferences{Theme="Dark",AccentColor="butter"};prefs.Save();var loaded=Preferences.Load();Check(loaded.AccentColor=="butter"&&loaded.Theme=="Dark","Accent selection persists independently of light or dark preference");File.WriteAllText(Path.Combine(folder,"settings.json"),"""{"Theme":"Light","Glow":0.31}""");var legacy=Preferences.Load();Check(legacy.AccentColor=="blue"&&legacy.Glow==.31&&legacy.Theme=="Light","Older profiles acquire blue without resetting their preferences");}finally{Environment.SetEnvironmentVariable("LUMIBAR_DATA_DIR",old);}
        return results;
    }
}
