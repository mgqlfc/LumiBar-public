using System;
using System.IO;
using System.Text.Json;

namespace AntigravityFrontend.Sdk;

public sealed class PluginConfig
{
    public string EndpointPath { get; set; } = @"C:\Users\ROG\Documents\Codex\2026-11-14\ban\work\frontend-dev-data\frontend-endpoint.json";
    public bool UseMockMode { get; set; } = false;
    public bool EnableTaskbarTakeover { get; set; } = true;
    public string PopupTrigger { get; set; } = "Click"; // Click | Hover
    public string PopupDirection { get; set; } = "Up";  // Up | Down
    public int HoverDelayMs { get; set; } = 220;
    public int ConfigVersion { get; set; } = 0;
    public double LyricsFlyoutWidth { get; set; } = 512;
    public double LyricsFlyoutHeight { get; set; } = 384;
    public double WeatherFlyoutWidth { get; set; } = 436;
    public double WeatherFlyoutHeight { get; set; } = 900;
    public bool PinLyricsFlyout { get; set; } = false;
    public bool PinWeatherFlyout { get; set; } = false;
    public bool CloseOnDeactivate { get; set; } = true;
    public bool FloatingDesktopMode { get; set; } = false;
    public int SimulatedAvailableWidth { get; set; } = 520;

    public static string OfficialEndpointPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "LumiBar",
        "frontend-endpoint.json");

    public static string PreviewEndpointPath =>
        @"C:\Users\ROG\Documents\Codex\2026-11-14\ban\work\frontend-dev-data\frontend-endpoint.json";

    public static string? EnvEndpointOverride =>
        Environment.GetEnvironmentVariable("LUMIBAR_ENDPOINT_FILE")?.Trim();

    public static bool IsTaskbarLaunchMode =>
        string.Equals(
            Environment.GetEnvironmentVariable("LUMIBAR_RENDERER_MODE")?.Trim(),
            "taskbar",
            StringComparison.OrdinalIgnoreCase);

    public string ResolveEffectiveEndpointPath()
    {
        var env = EnvEndpointOverride;
        if (!string.IsNullOrWhiteSpace(env))
            return env;
        return string.IsNullOrWhiteSpace(EndpointPath) ? OfficialEndpointPath : EndpointPath.Trim();
    }

    private static string ConfigFilePath =>
        Path.Combine(AppContext.BaseDirectory, "antigravity-plugin.config.json");

    public static PluginConfig Load()
    {
        try
        {
            if (File.Exists(ConfigFilePath))
            {
                var json = File.ReadAllText(ConfigFilePath);
                var parsed = JsonSerializer.Deserialize<PluginConfig>(json);
                if (parsed != null)
                {
                    if (parsed.ConfigVersion < 7)
                    {
                        if (parsed.WeatherFlyoutHeight < 860)
                            parsed.WeatherFlyoutHeight = 900;
                        if (parsed.WeatherFlyoutWidth < 432)
                            parsed.WeatherFlyoutWidth = 436;
                        parsed.LyricsFlyoutWidth = 512;
                        parsed.LyricsFlyoutHeight = 384;
                        parsed.ConfigVersion = 7;
                        parsed.Save();
                    }
                    return ApplyLaunchOverrides(parsed);
                }
            }
        }
        catch { }
        return ApplyLaunchOverrides(new PluginConfig { ConfigVersion = 7 });
    }

    private static PluginConfig ApplyLaunchOverrides(PluginConfig config)
    {
        if (IsTaskbarLaunchMode || !string.IsNullOrWhiteSpace(EnvEndpointOverride))
        {
            config.UseMockMode = false;
            config.EnableTaskbarTakeover = true;
        }
        return config;
    }

    public void Save()
    {
        try
        {
            var json = JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(ConfigFilePath, json);
        }
        catch { }
    }
}
