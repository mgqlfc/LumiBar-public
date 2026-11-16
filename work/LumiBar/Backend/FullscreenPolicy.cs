namespace LumiBar;
internal static class FullscreenPolicy
{
    internal static bool Covers(ScreenRect window,ScreenRect taskbar,string windowClass,bool ownedByTaskbar)
    {
        if(windowClass is "Progman" or "WorkerW" or "Shell_TrayWnd")return false;
        if(ownedByTaskbar&&windowClass is "XamlExplorerHostIslandWindow" or "TaskListThumbnailWnd" or "MultitaskingViewFrame" or "Shell_SecondaryTrayWnd" or "NotifyIconOverflowWindow" or "TopLevelWindowForOverflowXamlIsland")return false;
        return window.Left<=taskbar.Left&&window.Right>=taskbar.Right&&window.Top<taskbar.Top&&window.Bottom>=taskbar.Bottom;
    }
}
