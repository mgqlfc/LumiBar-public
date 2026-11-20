namespace LumiBar;

internal static class FontDefaults
{
    internal const string Chinese = "Source Han Sans SC VF", Western = "Geist";
    internal const string ChineseDisplayName = "思源黑体";
    internal const string ChineseAsset = "SourceHanSansSC-VF.ttf";

    internal static readonly string[] ChineseAliases = [Chinese, ChineseDisplayName, "思源黑体 VF"];
    private static readonly string[] LegacyChineseAliases = ["PingFang SC", ".PingFang SC", "苹方-简", "苹方"];

    internal static bool IsChinese(string? family) => family is not null &&
        (ChineseAliases.Contains(family.Trim(), StringComparer.OrdinalIgnoreCase) || IsLegacyChinese(family));

    internal static bool IsLegacyChinese(string? family) => family is not null &&
        LegacyChineseAliases.Contains(family.Trim(), StringComparer.OrdinalIgnoreCase);

    internal static bool IsWestern(string? family) => string.Equals(family?.Trim(), Western, StringComparison.OrdinalIgnoreCase);
}
