using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using AntigravityFrontend.Sdk;
using AntigravityFrontend.Windows;
using Microsoft.UI.Xaml;

namespace AntigravityFrontend;

public partial class App : Application
{
    private MainWindow? _mainWindow;
    private Mutex? _instanceMutex;
    private EventWaitHandle? _showWorkbenchEvent;
    private EventWaitHandle? _shutdownReplaceEvent;
    private CancellationTokenSource? _lifecycleCts;
    private int _shutdownCompleted;

    private static readonly string StartupLogPath = Path.Combine(AppContext.BaseDirectory, "startup.log");

    public static void LogStartup(string message)
    {
        try
        {
            File.AppendAllText(StartupLogPath, $"[{DateTimeOffset.Now:HH:mm:ss.fff}] {message}{Environment.NewLine}");
        }
        catch { }
    }

    public App()
    {
        try
        {
            File.WriteAllText(StartupLogPath, $"[{DateTimeOffset.Now:HH:mm:ss.fff}] App.ctor start (BaseDir={AppContext.BaseDirectory}){Environment.NewLine}");
        }
        catch { }

        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            LogStartup($"AppDomain.UnhandledException (Terminating={e.IsTerminating}): {e.ExceptionObject}");
        TaskScheduler.UnobservedTaskException += (_, e) =>
            LogStartup($"TaskScheduler.UnobservedTaskException: {e.Exception}");
        UnhandledException += (_, e) =>
        {
            LogStartup($"Application.UnhandledException: {e.Message}\n{e.Exception}");
        };

        try
        {
            InitializeComponent();
            LogStartup("App.InitializeComponent ok");
        }
        catch (Exception ex)
        {
            LogStartup($"App.InitializeComponent FAILED: {ex}");
            throw;
        }
    }

    private static string ComputeScopeKey()
    {
        var ep = PluginConfig.Load().ResolveEffectiveEndpointPath().Trim().ToLowerInvariant();
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(ep));
        return Convert.ToHexString(hash)[..12];
    }

    private bool AcquireSingletonOrSignalExisting(bool taskbarLaunch)
    {
        string key = ComputeScopeKey();
        string mutexName = $@"Local\AntigravityFrontend.LumiBar.{key}";
        string showEventName = $@"Local\AntigravityFrontend.Show.{key}";
        string replaceEventName = $@"Local\AntigravityFrontend.Replace.{key}";

        _showWorkbenchEvent = new EventWaitHandle(false, EventResetMode.AutoReset, showEventName);
        _shutdownReplaceEvent = new EventWaitHandle(false, EventResetMode.AutoReset, replaceEventName);
        _instanceMutex = new Mutex(initiallyOwned: true, mutexName, out bool createdNew);

        if (createdNew)
            return true;

        if (!taskbarLaunch)
        {
            LogStartup("Existing instance detected (workbench launch) -> signaling ShowWorkbench and exiting");
            try { _showWorkbenchEvent.Set(); } catch { }
            ReleaseSyncHandles();
            return false;
        }

        LogStartup("Existing instance detected (taskbar launch) -> signaling ShutdownForReplace");
        try { _shutdownReplaceEvent.Set(); } catch { }

        bool acquired = false;
        try
        {
            acquired = _instanceMutex.WaitOne(3000);
        }
        catch (AbandonedMutexException)
        {
            acquired = true;
        }
        catch { }

        if (!acquired)
        {
            LogStartup("Timed out waiting for previous instance to exit -> aborting duplicate taskbar launch");
            ReleaseSyncHandles();
            return false;
        }

        LogStartup("Acquired singleton mutex after previous instance exit");
        return true;
    }

    private void StartLifecycleSignalListener()
    {
        if (_showWorkbenchEvent == null || _shutdownReplaceEvent == null)
            return;

        _lifecycleCts = new CancellationTokenSource();
        var ct = _lifecycleCts.Token;
        WaitHandle[] handles = [_showWorkbenchEvent, _shutdownReplaceEvent, ct.WaitHandle];

        _ = Task.Run(() =>
        {
            while (!ct.IsCancellationRequested)
            {
                int idx;
                try { idx = WaitHandle.WaitAny(handles); }
                catch { break; }

                if (idx == 0)
                {
                    LogStartup("Signal received: ShowWorkbench");
                    _mainWindow?.DispatcherQueue.TryEnqueue(() => _mainWindow?.ShowWorkbenchWindow());
                }
                else if (idx == 1)
                {
                    LogStartup("Signal received: ShutdownForReplace");
                    _mainWindow?.DispatcherQueue.TryEnqueue(() => _mainWindow?.RequestCleanExit("replaced_by_new_launch"));
                    break;
                }
                else
                {
                    break;
                }
            }
        }, ct);
    }

    private void ReleaseSyncHandles()
    {
        try { _lifecycleCts?.Cancel(); } catch { }
        try { _instanceMutex?.ReleaseMutex(); } catch { }
        try { _instanceMutex?.Dispose(); } catch { }
        _instanceMutex = null;
        try { _showWorkbenchEvent?.Dispose(); } catch { }
        _showWorkbenchEvent = null;
        try { _shutdownReplaceEvent?.Dispose(); } catch { }
        _shutdownReplaceEvent = null;
    }

    [DllImport("kernel32.dll")]
    private static extern IntPtr GetCurrentProcess();

    [DllImport("kernel32.dll")]
    private static extern bool TerminateProcess(IntPtr hProcess, uint uExitCode);

    public void CompleteShutdown()
    {
        if (Interlocked.Exchange(ref _shutdownCompleted, 1) != 0)
            return;

        LogStartup("App.CompleteShutdown: releasing singleton handles and exiting");
        ReleaseSyncHandles();
        var fallback = new Thread(() =>
        {
            Thread.Sleep(700);
            LogStartup("App.CompleteShutdown: WinUI Exit() did not terminate within 700ms -> TerminateProcess(0)");
            TerminateProcess(GetCurrentProcess(), 0);
        }) { IsBackground = true };
        fallback.Start();
        try { Exit(); } catch { }
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        try
        {
            bool taskbarLaunch = PluginConfig.IsTaskbarLaunchMode;
            LogStartup($"OnLaunched start (taskbarMode={taskbarLaunch})");

            if (!AcquireSingletonOrSignalExisting(taskbarLaunch))
            {
                try { Exit(); } catch { }
                Environment.Exit(0);
                return;
            }

            _mainWindow = new MainWindow();
            StartLifecycleSignalListener();
            LogStartup("MainWindow constructed");
            if (taskbarLaunch)
            {
                Services.Win32WindowHelper.HideWindowImmediate(_mainWindow);
                LogStartup("MainWindow kept hidden for LUMIBAR_RENDERER_MODE=taskbar");
            }
            else
            {
                _mainWindow.Activate();
                LogStartup("MainWindow.Activate called");
            }
        }
        catch (Exception ex)
        {
            LogStartup($"OnLaunched FAILED: {ex}");
            ReleaseSyncHandles();
            throw;
        }
    }
}
