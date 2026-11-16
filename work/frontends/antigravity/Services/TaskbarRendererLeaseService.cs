using System;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using AntigravityFrontend.Sdk;
using LumiBar.Frontend.Sdk;
using Microsoft.UI.Dispatching;

namespace AntigravityFrontend.Services;

public enum TaskbarTakeoverPhase
{
    Disabled,
    WaitingBackend,
    Unsupported,
    PreviewOnly,
    Registering,
    PendingActivation,
    Active,
    BusyByOtherRenderer,
    ReservationUnavailable,
    PausedByUserRelease,
    Error
}

public sealed class TaskbarRendererLeaseService : IDisposable
{
    private const string RendererId = "antigravity.lumibar-glass";

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly DispatcherQueue _dispatcher;
    private readonly PluginConfig _config;
    private readonly LumiBarBridgeService _bridge;
    private readonly Func<string> _getMusicHwndHex;
    private readonly Func<string> _getWeatherHwndHex;
    private readonly Func<Task> _hideAllCapsulesAndFlyoutsAsync;
    private readonly Action _hideAllCapsulesAndFlyoutsNonBlocking;
    private readonly Func<bool> _isManagementUiVisible;

    private readonly CancellationTokenSource _cts = new();
    private readonly Stopwatch _leaseStopwatch = new();
    private readonly Stopwatch _disconnectedStopwatch = new();
    private readonly object _stateLock = new();

    private LumiBarClient? _leaseClient;
    private string? _leaseId;
    private string? _leaseToken;
    private int _leaseExpiresMs = 4000;
    private int _renewAfterMs = 1000;
    private bool _pausedByUserRelease;
    private bool _everConnected;
    private bool _autoExitSignaled;
    private int _hostPid;
    private string? _connectedEndpointPath;
    private string? _boundInstanceId;

    public bool IsPausedByUserRelease => _pausedByUserRelease;
    public TaskbarTakeoverPhase Phase { get; private set; } = TaskbarTakeoverPhase.WaitingBackend;
    public string StatusText { get; private set; } = "等待后端连接...";
    public string? LastReleaseReason { get; private set; }
    public int LatestSlotsVersion { get; private set; }
    public CapsuleSlotsDto? LatestSlots { get; private set; }
    public string LatestRenderingOwner { get; private set; } = "";
    public bool CanShowTaskbarCapsules => Phase == TaskbarTakeoverPhase.Active &&
                                         string.Equals(LatestRenderingOwner, "external", StringComparison.OrdinalIgnoreCase) &&
                                         !_config.UseMockMode &&
                                         _config.EnableTaskbarTakeover;

    public event Action? StatusOrSlotsChanged;
    public event Action<string>? AutoExitRequested;

    public TaskbarRendererLeaseService(
        DispatcherQueue dispatcher,
        PluginConfig config,
        LumiBarBridgeService bridge,
        Func<string> getMusicHwndHex,
        Func<string> getWeatherHwndHex,
        Func<Task> hideAllCapsulesAndFlyoutsAsync,
        Action hideAllCapsulesAndFlyoutsNonBlocking,
        Func<bool>? isManagementUiVisible = null)
    {
        _dispatcher = dispatcher;
        _config = config;
        _bridge = bridge;
        _getMusicHwndHex = getMusicHwndHex;
        _getWeatherHwndHex = getWeatherHwndHex;
        _hideAllCapsulesAndFlyoutsAsync = hideAllCapsulesAndFlyoutsAsync;
        _hideAllCapsulesAndFlyoutsNonBlocking = hideAllCapsulesAndFlyoutsNonBlocking;
        _isManagementUiVisible = isManagementUiVisible ?? (() => false);

        _bridge.ConnectionChanged += OnBridgeConnectionChanged;
        _bridge.InstanceChanged += OnBridgeInstanceChanged;
        _bridge.StateUpdated += OnBridgeStateUpdated;
    }

    public void Start()
    {
        _ = Task.Run(() => LeaseLoopAsync(_cts.Token));
        _ = Task.Run(() => WatchdogLoopAsync(_cts.Token));
    }

    public void RequestManualRetry()
    {
        _pausedByUserRelease = false;
        LastReleaseReason = null;
        if (Phase is TaskbarTakeoverPhase.PausedByUserRelease or TaskbarTakeoverPhase.BusyByOtherRenderer or TaskbarTakeoverPhase.ReservationUnavailable or TaskbarTakeoverPhase.Error)
        {
            SetPhase(TaskbarTakeoverPhase.WaitingBackend, "正在尝试重新接管任务栏...");
        }
    }

    private CancellationTokenSource? _resizeDebounceCts;

    public void NotifyPreferredSizesChanged()
    {
        if (Phase != TaskbarTakeoverPhase.Active) return;
        _resizeDebounceCts?.Cancel();
        var cts = new CancellationTokenSource();
        _resizeDebounceCts = cts;
        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(450, cts.Token);
                if (cts.IsCancellationRequested || _cts.IsCancellationRequested) return;
                if (Phase == TaskbarTakeoverPhase.Active)
                    await ReleaseImmediatelyAsync("preferred_sizes_updated", TaskbarTakeoverPhase.WaitingBackend, "正在按新尺寸重新注册任务栏胶囊...");
            }
            catch { }
        });
    }

    public async Task SetTakeoverEnabledAsync(bool enabled)
    {
        _config.EnableTaskbarTakeover = enabled;
        _config.Save();
        if (!enabled)
        {
            await ReleaseImmediatelyAsync("disabled_by_user", TaskbarTakeoverPhase.Disabled, "已关闭任务栏接管，已退回 LumiBar 内置渲染");
        }
        else
        {
            _pausedByUserRelease = false;
            SetPhase(TaskbarTakeoverPhase.WaitingBackend, "已开启任务栏接管，正在准备注册...");
        }
    }

    public async Task ReleaseByUserAsync()
    {
        _pausedByUserRelease = true;
        await ReleaseImmediatelyAsync("user_release", TaskbarTakeoverPhase.PausedByUserRelease, "已手动释放回 LumiBar 内置渲染（点击“重新接管”可恢复）");
    }

    public async Task ShutdownAsync()
    {
        await ReleaseImmediatelyAsync("shutdown", TaskbarTakeoverPhase.Disabled, "正在关闭...");
        _cts.Cancel();
    }

    public void ReleaseOnUiClose()
    {
        // Must run immediately without waiting on UI dispatcher to avoid deadlock during Window.Closed.
        _hideAllCapsulesAndFlyoutsNonBlocking();
        var creds = GetCredentialsSnapshot();
        ClearLeaseCredentials();
        var client = _leaseClient;
        _cts.Cancel();

        if (creds != null && client != null && client.IsConnected)
        {
            try
            {
                Task.Run(async () =>
                {
                    using var cts = new CancellationTokenSource(450);
                    await client.RequestAsync("windows.releaseTaskbarRenderer", new
                    {
                        leaseId = creds.Value.Id,
                        leaseToken = creds.Value.Token
                    }, cts.Token);
                }).Wait(500);
            }
            catch { }
        }
        DisposeLeaseClient();
    }

    private void OnBridgeConnectionChanged()
    {
        if (!_bridge.IsConnected || _bridge.IsMockMode)
        {
            FailClosedHideAndClear(
                _bridge.IsMockMode ? TaskbarTakeoverPhase.Disabled : TaskbarTakeoverPhase.WaitingBackend,
                _bridge.IsMockMode ? "示例数据模式下不接管真实任务栏" : "后端已断开，已隐藏原生任务栏胶囊");
        }
    }

    private void OnBridgeInstanceChanged(string? newInstanceId)
    {
        App.LogStartup("TaskbarRendererLeaseService: backend instanceId changed, fail-closed hide");
        FailClosedHideAndClear(TaskbarTakeoverPhase.WaitingBackend, "检测到后端实例重启，正在重新接管...");
    }

    private void OnBridgeStateUpdated()
    {
        // Consume pushed hints/reservation slots if newer than last lease response, and detect user_release.
        var hints = _bridge.CurrentHints;
        var reservation = _bridge.CurrentReservation;
        var status = reservation?.Renderer ?? hints?.Renderer;
        if (status != null && !string.IsNullOrEmpty(status.LastReleaseReason))
            LastReleaseReason = status.LastReleaseReason;

        if (Phase == TaskbarTakeoverPhase.Active && status != null)
        {
            var currentLeaseId = GetCredentialsSnapshot()?.Id;
            bool ownerLost = !string.IsNullOrEmpty(status.Owner) &&
                             !string.Equals(status.Owner, "external", StringComparison.OrdinalIgnoreCase);
            bool leaseMismatch = status.Lease != null &&
                                 ((currentLeaseId != null && !string.Equals(status.Lease.LeaseId, currentLeaseId, StringComparison.Ordinal)) ||
                                  (!string.IsNullOrEmpty(status.Lease.Phase) && !string.Equals(status.Lease.Phase, "active", StringComparison.OrdinalIgnoreCase)));
            if (ownerLost || leaseMismatch)
            {
                bool isUserRelease = string.Equals(status.LastReleaseReason, "user_release", StringComparison.OrdinalIgnoreCase);
                if (isUserRelease) _pausedByUserRelease = true;
                FailClosedHideAndClear(
                    isUserRelease ? TaskbarTakeoverPhase.PausedByUserRelease : TaskbarTakeoverPhase.WaitingBackend,
                    isUserRelease ? "后端触发了 user_release，已切回内置胶囊" : $"后端租约已收回（{status.LastReleaseReason ?? status.Owner}）");
                return;
            }
        }

        int incomingVer = reservation?.SlotsVersion ?? hints?.SlotsVersion ?? 0;
        CapsuleSlotsDto? incomingSlots = reservation?.Slots;
        if (incomingSlots == null && hints != null && (hints.MusicSlot != null || hints.WeatherSlot != null))
        {
            incomingSlots = new CapsuleSlotsDto
            {
                MusicSlot = hints.MusicSlot,
                WeatherSlot = hints.WeatherSlot,
                State = hints.LayoutState ?? ""
            };
        }

        if (incomingSlots != null && incomingVer >= LatestSlotsVersion)
        {
            bool changed = incomingVer > LatestSlotsVersion || LatestSlots == null;
            LatestSlotsVersion = incomingVer;
            LatestSlots = incomingSlots;
            if (!string.IsNullOrEmpty(hints?.RenderingOwner))
                LatestRenderingOwner = hints.RenderingOwner;
            if (!string.IsNullOrEmpty(reservation?.Owner))
                LatestRenderingOwner = reservation.Owner;

            if (changed && Phase == TaskbarTakeoverPhase.Active)
                NotifyUi(() => StatusOrSlotsChanged?.Invoke());
        }
    }

    private async Task LeaseLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                if (_config.UseMockMode)
                {
                    if (HasActiveLease)
                        await ReleaseImmediatelyAsync("mock_mode", TaskbarTakeoverPhase.Disabled, "示例数据模式下不接管真实任务栏");
                    else if (Phase != TaskbarTakeoverPhase.Disabled)
                        SetPhase(TaskbarTakeoverPhase.Disabled, "示例数据模式下不接管真实任务栏");
                    await Task.Delay(500, ct);
                    continue;
                }

                if (!_config.EnableTaskbarTakeover)
                {
                    if (HasActiveLease)
                        await ReleaseImmediatelyAsync("disabled", TaskbarTakeoverPhase.Disabled, "任务栏接管已关闭");
                    else if (Phase != TaskbarTakeoverPhase.Disabled)
                        SetPhase(TaskbarTakeoverPhase.Disabled, "任务栏接管已关闭");
                    await Task.Delay(500, ct);
                    continue;
                }

                if (_pausedByUserRelease)
                {
                    if (Phase != TaskbarTakeoverPhase.PausedByUserRelease)
                        SetPhase(TaskbarTakeoverPhase.PausedByUserRelease, "已释放回 LumiBar 内置渲染（点击“重新接管”可恢复）");
                    await Task.Delay(600, ct);
                    continue;
                }

                if (!_bridge.IsConnected || _bridge.CurrentHello == null)
                {
                    FailClosedHideAndClear(TaskbarTakeoverPhase.WaitingBackend, "等待 LumiBar 主连接就绪...");
                    await Task.Delay(400, ct);
                    continue;
                }

                var hello = _bridge.CurrentHello;
                if (string.Equals(hello.Mode, "preview", StringComparison.OrdinalIgnoreCase))
                {
                    FailClosedHideAndClear(TaskbarTakeoverPhase.PreviewOnly, "当前连接为 Preview 模拟后端（preview_only），仅内置展示预览工作台");
                    await Task.Delay(2000, ct);
                    continue;
                }

                var caps = hello.Capabilities;
                if (caps?.RendererReplacement != true || caps?.RendererLease != true)
                {
                    FailClosedHideAndClear(TaskbarTakeoverPhase.Unsupported, "当前后端不支持 0.7 Renderer Lease 协议");
                    await Task.Delay(3000, ct);
                    continue;
                }

                var epPath = _config.ResolveEffectiveEndpointPath();
                if (!string.Equals(_connectedEndpointPath, epPath, StringComparison.OrdinalIgnoreCase) ||
                    !string.Equals(_boundInstanceId, hello.InstanceId, StringComparison.Ordinal))
                {
                    if (HasActiveLease)
                        await ReleaseImmediatelyAsync("endpoint_or_instance_changed", TaskbarTakeoverPhase.WaitingBackend, "后端端点或实例变化，正在重连...");
                    DisposeLeaseClient();
                }

                if (_leaseClient == null || !_leaseClient.IsConnected)
                {
                    DisposeLeaseClient();
                    if (!File.Exists(epPath))
                    {
                        await Task.Delay(800, ct);
                        continue;
                    }
                    var client = new LumiBarClient();
                    await client.ConnectAsync(epPath, ct);
                    _leaseClient = client;
                    _connectedEndpointPath = epPath;
                    _boundInstanceId = hello.InstanceId;
                }

                // Step 1: Register if no lease
                if (!HasActiveLease)
                {
                    SetPhase(TaskbarTakeoverPhase.Registering, "正在隐藏宿主窗口并向后端注册外部渲染租约...");
                    await _hideAllCapsulesAndFlyoutsAsync();

                    var registerParams = BuildRegisterParams();
                    var regJson = await _leaseClient.RequestAsync("windows.registerRenderer", registerParams, ct);
                    var reg = JsonSerializer.Deserialize<RendererLeaseCommandResultDto>(regJson.GetRawText(), JsonOpts);
                    if (reg == null || string.IsNullOrEmpty(reg.LeaseId) || string.IsNullOrEmpty(reg.LeaseToken))
                    {
                        SetPhase(TaskbarTakeoverPhase.Error, "注册响应缺少 leaseId/leaseToken");
                        await Task.Delay(1500, ct);
                        continue;
                    }

                    lock (_stateLock)
                    {
                        _leaseId = reg.LeaseId;
                        _leaseToken = reg.LeaseToken;
                        _leaseExpiresMs = reg.ExpiresInMs > 0 ? reg.ExpiresInMs : (reg.LeaseDurationMs > 0 ? reg.LeaseDurationMs : 4000);
                        _renewAfterMs = reg.RenewAfterMs > 0 ? reg.RenewAfterMs : 1000;
                        _leaseStopwatch.Restart();
                    }
                    ApplySlotMetadata(reg);
                    SetPhase(TaskbarTakeoverPhase.PendingActivation, "租约已注册 (pending)，等待任务栏原生占位就绪并激活...");
                }

                // Step 2: Activate while pending
                if (Phase is TaskbarTakeoverPhase.PendingActivation or TaskbarTakeoverPhase.ReservationUnavailable)
                {
                    var creds = GetCredentialsSnapshot();
                    if (creds == null) continue;

                    try
                    {
                        var actJson = await _leaseClient.RequestAsync("windows.activateTaskbarRenderer", new
                        {
                            leaseId = creds.Value.Id,
                            leaseToken = creds.Value.Token
                        }, ct);
                        var act = JsonSerializer.Deserialize<RendererLeaseCommandResultDto>(actJson.GetRawText(), JsonOpts);
                        if (act != null)
                        {
                            lock (_stateLock)
                            {
                                _leaseExpiresMs = act.ExpiresInMs > 0 ? act.ExpiresInMs : _leaseExpiresMs;
                                _renewAfterMs = act.RenewAfterMs > 0 ? act.RenewAfterMs : _renewAfterMs;
                                _leaseStopwatch.Restart();
                            }
                            ApplySlotMetadata(act);
                            if (string.Equals(act.Phase, "active", StringComparison.OrdinalIgnoreCase) &&
                                string.Equals(act.RenderingOwner, "external", StringComparison.OrdinalIgnoreCase))
                            {
                                SetPhase(TaskbarTakeoverPhase.Active, $"已正式接管 Windows 任务栏原生胶囊（slots v{LatestSlotsVersion}）");
                            }
                        }
                    }
                    catch (LumiBarApiException ex)
                    {
                        if (!await HandleLeaseApiErrorAsync(ex, isActivation: true, ct))
                            continue;
                    }

                    // While still pending activation, renew if close to renew interval and retry activate after ~300ms
                    if (Phase != TaskbarTakeoverPhase.Active)
                    {
                        if (_leaseStopwatch.ElapsedMilliseconds >= Math.Max(400, _renewAfterMs - 100))
                            await RenewOnceAsync(ct);
                        await Task.Delay(300, ct);
                        continue;
                    }
                }

                // Step 3: Active heartbeat renewal loop
                int waitMs = Math.Clamp(_renewAfterMs > 0 ? _renewAfterMs : 1000, 300, 1200);
                await Task.Delay(waitMs, ct);
                if (Phase == TaskbarTakeoverPhase.Active && HasActiveLease)
                    await RenewOnceAsync(ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                break;
            }
            catch (LumiBarApiException ex)
            {
                if (ex.Code is "preview_only")
                {
                    FailClosedHideAndClear(TaskbarTakeoverPhase.PreviewOnly, "当前后端仅支持 Preview（preview_only）");
                    await Task.Delay(2500, ct);
                }
                else if (ex.Code is "renderer_busy")
                {
                    FailClosedHideAndClear(TaskbarTakeoverPhase.BusyByOtherRenderer, "任务栏正被其他外部渲染器占用（renderer_busy）");
                    await Task.Delay(4000, ct);
                }
                else if (ex.Code is "windows_must_be_hidden")
                {
                    await _hideAllCapsulesAndFlyoutsAsync();
                    FailClosedHideAndClear(TaskbarTakeoverPhase.WaitingBackend, "窗口需保持隐藏后再注册，正在重试...");
                    await Task.Delay(300, ct);
                }
                else
                {
                    FailClosedHideAndClear(TaskbarTakeoverPhase.Error, $"租约调用异常（{ex.Code}）：{ex.Message}");
                    await Task.Delay(1500, ct);
                }
            }
            catch (Exception ex)
            {
                DisposeLeaseClient();
                FailClosedHideAndClear(TaskbarTakeoverPhase.WaitingBackend, $"租约通道断开，重连中：{ex.Message}");
                await Task.Delay(1000, ct);
            }
        }
    }

    private async Task RenewOnceAsync(CancellationToken ct)
    {
        var creds = GetCredentialsSnapshot();
        if (creds == null || _leaseClient == null) return;

        try
        {
            var resJson = await _leaseClient.RequestAsync("windows.renewTaskbarRenderer", new
            {
                leaseId = creds.Value.Id,
                leaseToken = creds.Value.Token
            }, ct);
            var res = JsonSerializer.Deserialize<RendererLeaseCommandResultDto>(resJson.GetRawText(), JsonOpts);
            if (res == null) return;

            lock (_stateLock)
            {
                _leaseExpiresMs = res.ExpiresInMs > 0 ? res.ExpiresInMs : _leaseExpiresMs;
                _renewAfterMs = res.RenewAfterMs > 0 ? res.RenewAfterMs : _renewAfterMs;
                _leaseStopwatch.Restart();
            }

            bool slotsUpdated = ApplySlotMetadata(res);
            if (Phase == TaskbarTakeoverPhase.Active &&
                (!string.Equals(res.Phase, "active", StringComparison.OrdinalIgnoreCase) ||
                 !string.Equals(res.RenderingOwner, "external", StringComparison.OrdinalIgnoreCase)))
            {
                FailClosedHideAndClear(TaskbarTakeoverPhase.PendingActivation, $"渲染归属已变为 {res.RenderingOwner} ({res.Phase})，已安全隐藏胶囊");
                return;
            }

            if (slotsUpdated && Phase == TaskbarTakeoverPhase.Active)
                NotifyUi(() => StatusOrSlotsChanged?.Invoke());
        }
        catch (LumiBarApiException ex)
        {
            await HandleLeaseApiErrorAsync(ex, isActivation: false, ct);
        }
    }

    private async Task<bool> HandleLeaseApiErrorAsync(LumiBarApiException ex, bool isActivation, CancellationToken ct)
    {
        switch (ex.Code)
        {
            case "reservation_not_ready":
                if (Phase != TaskbarTakeoverPhase.PendingActivation)
                    SetPhase(TaskbarTakeoverPhase.PendingActivation, "任务栏原生占位尚未就绪 (reservation_not_ready)，保持隐藏并重试中...");
                return true;

            case "reservation_unavailable":
                _hideAllCapsulesAndFlyoutsNonBlocking();
                SetPhase(TaskbarTakeoverPhase.ReservationUnavailable, "任务栏原生占位当前不可用 (reservation_unavailable)，等待恢复...");
                await Task.Delay(1200, ct);
                return false;

            case "windows_must_be_hidden":
                await _hideAllCapsulesAndFlyoutsAsync();
                SetPhase(TaskbarTakeoverPhase.PendingActivation, "已确保任务栏宿主窗口隐藏，正在重试激活...");
                return true;

            case "preview_only":
                FailClosedHideAndClear(TaskbarTakeoverPhase.PreviewOnly, "当前连接为 Preview 模拟后端（preview_only）");
                await Task.Delay(2000, ct);
                return false;

            case "renderer_busy":
                FailClosedHideAndClear(TaskbarTakeoverPhase.BusyByOtherRenderer, "任务栏已被其他外部渲染器占用（renderer_busy）");
                await Task.Delay(4000, ct);
                return false;

            case "lease_invalid":
            case "lease_expired":
            case "reservation_lost":
            case "user_release":
            case "window_owner_mismatch":
            case "peer_mismatch":
                LastReleaseReason = ex.Code;
                if (ex.Code == "user_release")
                    _pausedByUserRelease = true;
                FailClosedHideAndClear(
                    ex.Code == "user_release" ? TaskbarTakeoverPhase.PausedByUserRelease : TaskbarTakeoverPhase.WaitingBackend,
                    ex.Code == "user_release" ? "用户主动释放了任务栏接管 (user_release)" : $"租约失效 ({ex.Code})，已隐藏胶囊并准备恢复");
                return false;

            default:
                FailClosedHideAndClear(TaskbarTakeoverPhase.Error, $"任务栏{(isActivation ? "激活" : "续租")}错误 ({ex.Code})：{ex.Message}");
                await Task.Delay(1200, ct);
                return false;
        }
    }

    private async Task WatchdogLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(120, ct);
                bool expired = false;
                lock (_stateLock)
                {
                    if (_leaseId != null && _leaseToken != null && _leaseStopwatch.IsRunning)
                    {
                        long safeBudgetMs = Math.Max(400, _leaseExpiresMs - 350);
                        if (_leaseStopwatch.ElapsedMilliseconds >= safeBudgetMs)
                            expired = true;
                    }
                }

                if (expired)
                {
                    App.LogStartup("TaskbarRendererLeaseService: local monotonic watchdog expired -> fail-closed hide");
                    FailClosedHideAndClear(TaskbarTakeoverPhase.WaitingBackend, "本地租约看门狗超时，已立即隐藏任务栏胶囊并重新同步");
                }

                CheckBackgroundAutoExit();
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                break;
            }
            catch { }
        }
    }

    private void CheckBackgroundAutoExit()
    {
        if (!PluginConfig.IsTaskbarLaunchMode || _autoExitSignaled)
            return;

        if (_bridge.IsConnected)
        {
            _everConnected = true;
            _disconnectedStopwatch.Reset();
            if (_hostPid <= 0)
                _hostPid = TryReadEndpointHostPid(_connectedEndpointPath ?? _config.ResolveEffectiveEndpointPath());
            return;
        }

        if (!_disconnectedStopwatch.IsRunning)
            _disconnectedStopwatch.Restart();

        if (_isManagementUiVisible())
            return;

        if (_everConnected)
        {
            if (_hostPid > 0 && !IsProcessAlive(_hostPid) && _disconnectedStopwatch.ElapsedMilliseconds >= 1500)
            {
                SignalAutoExit($"host_pid_{_hostPid}_exited");
                return;
            }
            if (_disconnectedStopwatch.ElapsedMilliseconds >= 12000)
            {
                SignalAutoExit("backend_disconnected_timeout");
                return;
            }
        }
        else if (_disconnectedStopwatch.ElapsedMilliseconds >= 15000)
        {
            SignalAutoExit("backend_connect_timeout");
        }
    }

    private void SignalAutoExit(string reason)
    {
        if (_autoExitSignaled) return;
        _autoExitSignaled = true;
        App.LogStartup($"TaskbarRendererLeaseService: auto-exit requested ({reason})");
        AutoExitRequested?.Invoke(reason);
    }

    private static int TryReadEndpointHostPid(string? endpointPath)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(endpointPath) || !File.Exists(endpointPath))
                return 0;
            using var doc = JsonDocument.Parse(File.ReadAllText(endpointPath));
            if (doc.RootElement.TryGetProperty("pid", out var pidEl) && pidEl.TryGetInt32(out int pid) && pid > 0)
                return pid;
        }
        catch { }
        return 0;
    }

    private static bool IsProcessAlive(int pid)
    {
        try
        {
            using var p = Process.GetProcessById(pid);
            return !p.HasExited;
        }
        catch
        {
            return false;
        }
    }

    private async Task ReleaseImmediatelyAsync(string reason, TaskbarTakeoverPhase targetPhase, string statusText)
    {
        await _hideAllCapsulesAndFlyoutsAsync();
        var creds = GetCredentialsSnapshot();
        ClearLeaseCredentials();
        LastReleaseReason = reason;
        SetPhase(targetPhase, statusText);

        if (creds != null && _leaseClient != null && _leaseClient.IsConnected)
        {
            try
            {
                await _leaseClient.RequestAsync("windows.releaseTaskbarRenderer", new
                {
                    leaseId = creds.Value.Id,
                    leaseToken = creds.Value.Token
                }, CancellationToken.None);
            }
            catch { }
        }
    }

    private void FailClosedHideAndClear(TaskbarTakeoverPhase phase, string statusText)
    {
        _hideAllCapsulesAndFlyoutsNonBlocking();
        ClearLeaseCredentials();
        LatestRenderingOwner = "builtin";
        SetPhase(phase, statusText);
    }

    private object BuildRegisterParams()
    {
        var settings = _bridge.CurrentState.Settings;
        double musicFull = Math.Clamp(settings.MusicWidth, 180, 340);
        double musicLite = 48;
        double weatherFull = 104;
        double weatherLite = 64;
        double gap = Math.Clamp(settings.Gap, 0, 24);
        double height = 34;

        return new
        {
            rendererId = RendererId,
            pid = Environment.ProcessId,
            capsuleHwnds = new
            {
                music = _getMusicHwndHex(),
                weather = _getWeatherHwndHex()
            },
            preferredSizes = new
            {
                musicFullDip = musicFull,
                musicLiteDip = musicLite,
                weatherFullDip = weatherFull,
                weatherLiteDip = weatherLite,
                gapDip = gap,
                heightDip = height
            }
        };
    }

    private bool ApplySlotMetadata(RendererLeaseCommandResultDto dto)
    {
        bool changed = dto.SlotsVersion != LatestSlotsVersion || LatestSlots == null;
        if (dto.Slots != null)
            LatestSlots = dto.Slots;
        if (dto.SlotsVersion > 0)
            LatestSlotsVersion = dto.SlotsVersion;
        if (!string.IsNullOrEmpty(dto.RenderingOwner))
            LatestRenderingOwner = dto.RenderingOwner;
        if (!string.IsNullOrEmpty(dto.Reason))
            LastReleaseReason = dto.Reason;
        return changed;
    }

    private bool HasActiveLease
    {
        get
        {
            lock (_stateLock)
                return _leaseId != null && _leaseToken != null;
        }
    }

    private (string Id, string Token)? GetCredentialsSnapshot()
    {
        lock (_stateLock)
        {
            if (_leaseId == null || _leaseToken == null)
                return null;
            return (_leaseId, _leaseToken);
        }
    }

    private void ClearLeaseCredentials()
    {
        lock (_stateLock)
        {
            _leaseId = null;
            _leaseToken = null;
            _leaseStopwatch.Reset();
        }
    }

    private void SetPhase(TaskbarTakeoverPhase phase, string status)
    {
        if (Phase != phase)
            App.LogStartup($"TaskbarRendererLeaseService: phase {Phase} -> {phase} ({status})");
        Phase = phase;
        StatusText = status;
        NotifyUi(() => StatusOrSlotsChanged?.Invoke());

        if (phase == TaskbarTakeoverPhase.PausedByUserRelease &&
            PluginConfig.IsTaskbarLaunchMode &&
            !_isManagementUiVisible())
        {
            SignalAutoExit("user_release");
        }
    }

    private void NotifyUi(Action action) => _dispatcher.TryEnqueue(() => action());

    private void DisposeLeaseClient()
    {
        try { _leaseClient?.Dispose(); } catch { }
        _leaseClient = null;
        _connectedEndpointPath = null;
        _boundInstanceId = null;
    }

    public void Dispose()
    {
        _bridge.ConnectionChanged -= OnBridgeConnectionChanged;
        _bridge.InstanceChanged -= OnBridgeInstanceChanged;
        _bridge.StateUpdated -= OnBridgeStateUpdated;
        _cts.Cancel();
        _hideAllCapsulesAndFlyoutsNonBlocking();
        ClearLeaseCredentials();
        DisposeLeaseClient();
        _cts.Dispose();
    }
}
