using Microsoft.Win32;
namespace LumiBar;
internal static class StartupRegistration
{
    internal static void Apply(bool enabled)
    {
        using var key=Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run");
        if(enabled){var command=$"\"{Environment.ProcessPath}\"";if(key.GetValue("LumiBar") as string!=command)key.SetValue("LumiBar",command);}
        else key.DeleteValue("LumiBar",false);
    }
}
