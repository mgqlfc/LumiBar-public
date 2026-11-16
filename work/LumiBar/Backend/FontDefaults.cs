namespace LumiBar;

internal static class FontDefaults
{
    internal const string Chinese="PingFang SC",Western="Geist";
    internal static bool IsChinese(string? family)=>family!=null&&new[]{"PingFang SC",".PingFang SC","苹方-简","苹方"}.Contains(family.Trim(),StringComparer.OrdinalIgnoreCase);
    internal static bool IsWestern(string? family)=>string.Equals(family?.Trim(),Western,StringComparison.OrdinalIgnoreCase);
    internal static string ChineseFile(int weight)=>weight switch{<=100=>"Ultralight",<=200=>"Thin",<=300=>"Light",<=450=>"Regular",<=550=>"Medium",_=>"Semibold"};
}
