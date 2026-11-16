namespace LumiBar;
internal sealed record AccentPalette(string Id,string Name,uint First,uint Middle,uint Last,uint LightInk,uint DarkInk)
{
    internal uint Sample(double position)
    {
        position=Math.Clamp(position,0,1);var a=position<=.46?First:Middle;var b=position<=.46?Middle:Last;var t=position<=.46?position/.46:(position-.46)/.54;
        uint Channel(int shift)=>(uint)Math.Round(((a>>shift)&255)*(1-t)+((b>>shift)&255)*t);
        return Channel(16)<<16|Channel(8)<<8|Channel(0);
    }
    public string[] Colors=>[ $"#{First:X6}",$"#{Middle:X6}",$"#{Last:X6}" ];
}
internal static class AccentPalettes
{
    internal const string Default="blue";
    internal static readonly IReadOnlyList<AccentPalette> All=Array.AsReadOnly(new[]{
        new AccentPalette("blue","冰糖蓝",0x6CE7DA,0x21BEC2,0x118FB1,0x006C80,0x70E8DA),
        new AccentPalette("mint","薄荷绿",0xBAF0D6,0x6BCDAA,0x399F8C,0x176D5D,0x9BE2C4),
        new AccentPalette("lime","青柠糖",0xE0EEB3,0xB4CF82,0x7DA56C,0x506C2B,0xD0E5A4),
        new AccentPalette("butter","奶油黄",0xFFF1B6,0xF0D47A,0xD0B365,0x786117,0xF6DF97),
        new AccentPalette("apricot","奶杏橙",0xFFE0BB,0xF2BA91,0xD99579,0x985235,0xF7CCA5),
        new AccentPalette("peach","蜜桃粉",0xFFD3DF,0xF0ACC9,0xD48EB6,0x91446C,0xF6BDD5),
        new AccentPalette("berry","莓果红",0xFFD0CE,0xEF9EA9,0xD17F9C,0x973D5A,0xF4B4BF),
        new AccentPalette("lavender","薰衣紫",0xE5D5FF,0xC6ACEF,0xA58BDA,0x6B4DA0,0xD7C0F7),
        new AccentPalette("periwinkle","绣球蓝",0xD2E3FF,0xA9C4F3,0x86A5DE,0x435D99,0xBED3F8)
    });
    internal static bool Contains(string? id)=>id!=null&&All.Any(p=>string.Equals(p.Id,id.Trim(),StringComparison.OrdinalIgnoreCase));
    internal static string Normalize(string? id)=>All.FirstOrDefault(p=>string.Equals(p.Id,id?.Trim(),StringComparison.OrdinalIgnoreCase))?.Id??Default;
    internal static AccentPalette Get(string? id)=>All.First(p=>p.Id==Normalize(id));
}
