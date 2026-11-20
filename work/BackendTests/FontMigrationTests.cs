using LumiBar;

internal static class FontMigrationTests
{
    internal static List<string> Run()
    {
        var results=new List<string>();
        void Check(bool value,string name)=>results.Add($"{(value?"PASS":"FAIL")} {name}");
        var previousPath=Environment.GetEnvironmentVariable("LUMIBAR_DATA_DIR");
        var folder=Path.Combine(AppContext.BaseDirectory,"test-data","font-migration-"+Guid.NewGuid().ToString("N"));
        var caseNumber=0;
        Preferences Load(string json,string? backup=null)
        {
            var path=Path.Combine(folder,(++caseNumber).ToString());Directory.CreateDirectory(path);
            Environment.SetEnvironmentVariable("LUMIBAR_DATA_DIR",path);
            File.WriteAllText(Path.Combine(path,"settings.json"),json);
            if(backup!=null)File.WriteAllText(Path.Combine(path,"settings.json.bak"),backup);
            return Preferences.Load();
        }
        try
        {
            var legacy=Load("""{"FontFamily":" Inter ","Theme":"Dark"}""");
            Check(legacy.ChineseFontFamily=="Inter"&&legacy.WesternFontFamily=="Inter"&&legacy.Theme=="Dark","Legacy single-font profile migrates both script fonts and preserves other preferences");
            var partialChinese=Load("""{"FontFamily":"Inter","ChineseFontFamily":null}""");
            Check(partialChinese.ChineseFontFamily==null&&partialChinese.WesternFontFamily=="Inter","Explicit Chinese system-font choice survives migration while missing Western font inherits legacy value");
            var partialWestern=Load("""{"FontFamily":"PingFang SC","WesternFontFamily":"Inter"}""");
            Check(partialWestern.ChineseFontFamily==null&&partialWestern.WesternFontFamily=="Inter","Old bundled Chinese font returns to the new default while explicit Western font survives");
            var oldBundled=Load("""{"ChineseFontFamily":"苹方-简","WesternFontFamily":".PingFang SC"}""");
            Check(oldBundled.ChineseFontFamily==null&&oldBundled.WesternFontFamily==null,"Explicit aliases of the previous bundled font return to the script defaults");
            Check(FontDefaults.Chinese=="Source Han Sans SC VF"&&FontDefaults.IsChinese("思源黑体"),"The default Chinese family and its localized name resolve to the new bundled font");
            var reset=Load("""{"FontFamily":"Inter","ChineseFontFamily":null,"WesternFontFamily":null}""");
            Check(reset.ChineseFontFamily==null&&reset.WesternFontFamily==null,"Both explicit system-font resets stay null despite retained legacy font");
            var blank=Load("""{"FontFamily":"Inter","ChineseFontFamily":"","WesternFontFamily":" "}""");
            Check(blank.ChineseFontFamily==null&&blank.WesternFontFamily==null,"Explicit blank font values normalize to system defaults without legacy inheritance");
            var unsafeFont=Load("""{"FontFamily":"https://example.invalid/font.ttf"}""");
            Check(unsafeFont.FontFamily==null&&unsafeFont.ChineseFontFamily==null&&unsafeFont.WesternFontFamily==null,"Legacy remote font values remain rejected after migration");
            foreach(var corrupt in new[]{"broken{","{\"MusicWidth\":\"invalid\"}","[]","null"})
            {
                var recovered=Load(corrupt,"""{"FontFamily":"Inter","ChineseFontFamily":null,"Gap":12}""");
                Check(recovered.ChineseFontFamily==null&&recovered.WesternFontFamily=="Inter"&&recovered.Gap==12,$"Font migration also applies after backup recovery from {corrupt}");
            }
            var saved=Load("""{"FontFamily":"Inter"}""");saved.Save();
            saved=Preferences.Load();saved.ChineseFontFamily=null;saved.Save();
            var restored=Preferences.Load();
            Check(restored.ChineseFontFamily==null&&restored.WesternFontFamily=="Inter","Migrated profile can restore Chinese system font and retain that choice across save and reload");
            var absent=Load("{}");
            Check(absent.ChineseFontFamily==null&&absent.WesternFontFamily==null,"Fresh profiles keep system defaults for both script fonts");
        }
        finally{Environment.SetEnvironmentVariable("LUMIBAR_DATA_DIR",previousPath);}
        return results;
    }
}
