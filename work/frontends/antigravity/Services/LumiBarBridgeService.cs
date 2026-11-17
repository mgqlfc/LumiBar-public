using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using AntigravityFrontend.Sdk;
using LumiBar.Frontend.Sdk;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Storage.Streams;

namespace AntigravityFrontend.Services;

public sealed class LumiBarBridgeService : IDisposable
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly DispatcherQueue _dispatcher;
    private readonly PluginConfig _config;
    private readonly CancellationTokenSource _cts = new();

    private LumiBarClient? _stateClient;
    private LumiBarClient? _spectrumClient;
    private LumiBarClient? _eventsClient;
    private readonly SemaphoreSlim _actionLock = new(1, 1);

    private readonly Dictionary<string, BitmapImage?> _artworkCache = new();
    private string? _pendingArtworkKey;

    private DateTime _lastSnapshotLocalTime = DateTime.UtcNow;
    private double _snapshotMediaPosition;
    private double _mockElapsedSec = 84;
    private double _mockPhase;
    private bool _isAnyPopupVisible;
    private long _lastStateVersion;
    private string? _lastInstanceId;
    private DateTime _lastEventsPingUtc = DateTime.MinValue;

    public StateDto CurrentState { get; private set; }
    public SpectrumDto CurrentSpectrum { get; private set; } = new();
    public WindowHintsDto? CurrentHints { get; private set; }
    public HelloResponseDto? CurrentHello { get; private set; }
    public WindowsReservationDto? CurrentReservation { get; private set; }
    public BitmapImage? CurrentArtwork { get; private set; }

    public bool IsConnected { get; private set; }
    public bool IsMockMode => _config.UseMockMode;
    public string ConnectionStatus { get; private set; } = "未连接";
    public string LastErrorMessage { get; private set; } = "";
    public bool IsRefreshingWeather { get; private set; }

    public event Action? StateUpdated;
    public event Action? SpectrumUpdated;
    public event Action? ArtworkUpdated;
    public event Action? ConnectionChanged;
    public event Action<string?>? InstanceChanged;

    public string EffectiveEndpointPath => _config.ResolveEffectiveEndpointPath();

    public void ReconnectEndpoint()
    {
        _stateClient?.Dispose();
        _stateClient = null;
        _spectrumClient?.Dispose();
        _spectrumClient = null;
        _eventsClient?.Dispose();
        _eventsClient = null;
        _artworkCache.Clear();
        _pendingArtworkKey = null;
        CurrentArtwork = null;
        _lastStateVersion = 0;
        SetOffline("正在切换并重连后端...");
        NotifyUi(() => ArtworkUpdated?.Invoke());
    }

    private readonly global::Windows.UI.ViewManagement.UISettings _uiSettings = new();

    public LumiBarBridgeService(DispatcherQueue dispatcher, PluginConfig config)
    {
        _dispatcher = dispatcher;
        _config = config;
        CurrentState = CreateMockState();
        _uiSettings.ColorValuesChanged += OnSystemColorValuesChanged;
    }

    private void OnSystemColorValuesChanged(global::Windows.UI.ViewManagement.UISettings sender, object args)
    {
        Win32WindowHelper.ApplySystemThemeIfFollowing(CurrentState);
        NotifyUi(() => StateUpdated?.Invoke());
    }

    public void Start()
    {
        _ = Task.Run(() => StateLoopAsync(_cts.Token));
        _ = Task.Run(() => SpectrumLoopAsync(_cts.Token));
        _ = Task.Run(() => EventsLoopAsync(_cts.Token));
    }

    public void SetPopupVisibility(bool anyPopupVisible)
    {
        _isAnyPopupVisible = anyPopupVisible;
    }

    public void SetMockMode(bool useMock)
    {
        _config.UseMockMode = useMock;
        _config.Save();
        if (useMock)
        {
            _stateClient?.Dispose();
            _stateClient = null;
            _spectrumClient?.Dispose();
            _spectrumClient = null;
            _eventsClient?.Dispose();
            _eventsClient = null;
            _lastStateVersion = 0;
            _lastInstanceId = null;
            CurrentHello = null;
            CurrentHints = null;
            CurrentReservation = null;
            CurrentState = CreateMockState();
            CurrentArtwork = null;
            IsConnected = false;
            ConnectionStatus = "示例数据模式（未连接实况后端）";
            LastErrorMessage = "";
            NotifyUi(() =>
            {
                ConnectionChanged?.Invoke();
                StateUpdated?.Invoke();
                ArtworkUpdated?.Invoke();
            });
        }
        else
        {
            ConnectionStatus = "正在连接后端...";
            NotifyUi(() => ConnectionChanged?.Invoke());
        }
    }

    public double GetInterpolatedMediaPosition()
    {
        var media = CurrentState.Media;
        if (media == null) return 0;
        if (_config.UseMockMode) return _mockElapsedSec;
        if (!media.Playing || media.Duration <= 0) return media.Position;
        var dt = (DateTime.UtcNow - _lastSnapshotLocalTime).TotalSeconds;
        return Math.Clamp(_snapshotMediaPosition + dt, 0, media.Duration);
    }

    public PrecipitationViewModel GetOneHourPrecipitation()
    {
        var trend = CurrentState.Weather.PrecipitationNextHour;
        if (trend != null)
        {
            var slots = new List<PrecipitationSlot>();
            foreach (var item in trend.Intervals)
            {
                string lbl = item.Start;
                if (DateTimeOffset.TryParse(item.Start, out var dt))
                    lbl = dt.ToLocalTime().ToString("HH:mm");
                slots.Add(new PrecipitationSlot
                {
                    Label = lbl,
                    Millimeters = item.AmountMm
                });
            }
            return new PrecipitationViewModel
            {
                Slots = slots,
                PrecisionNote = trend.PrecisionNote,
                Complete = trend.Complete
            };
        }

        return new PrecipitationViewModel
        {
            Slots = new List<PrecipitationSlot>(),
            PrecisionNote = null,
            Complete = true
        };
    }

    public List<PrecipitationSlot> GetOneHourPrecipitationSlots() => GetOneHourPrecipitation().Slots;

    private async Task StateLoopAsync(CancellationToken ct)
    {
        int[] backoffSeconds = { 1, 2, 5, 10 };
        int retryIdx = 0;

        while (!ct.IsCancellationRequested)
        {
            if (_config.UseMockMode)
            {
                if (CurrentState.Media?.Playing == true)
                {
                    _mockElapsedSec = (_mockElapsedSec + 1) % Math.Max(60, CurrentState.Media.Duration);
                    CurrentState.Media.Position = _mockElapsedSec;
                    NotifyUi(() => StateUpdated?.Invoke());
                }
                await Task.Delay(1000, ct);
                continue;
            }

            try
            {
                if (_stateClient == null || !_stateClient.IsConnected)
                {
                    _stateClient?.Dispose();
                    var epPath = _config.ResolveEffectiveEndpointPath();
                    if (!File.Exists(epPath))
                    {
                        SetOffline($"未找到接口文件：{epPath}");
                        await Task.Delay(TimeSpan.FromSeconds(backoffSeconds[Math.Min(retryIdx++, backoffSeconds.Length - 1)]), ct);
                        continue;
                    }

                    var client = new LumiBarClient();
                    var helloJson = await client.ConnectAsync(epPath, ct);
                    try
                    {
                        CurrentHello = JsonSerializer.Deserialize<HelloResponseDto>(helloJson.GetRawText(), JsonOpts);
                    }
                    catch { CurrentHello = null; }
                    if (CurrentHello != null && !string.IsNullOrEmpty(CurrentHello.InstanceId) &&
                        !string.IsNullOrEmpty(_lastInstanceId) &&
                        !string.Equals(CurrentHello.InstanceId, _lastInstanceId, StringComparison.Ordinal))
                    {
                        _lastInstanceId = CurrentHello.InstanceId;
                        NotifyUi(() => InstanceChanged?.Invoke(_lastInstanceId));
                    }
                    else if (!string.IsNullOrEmpty(CurrentHello?.InstanceId))
                    {
                        _lastInstanceId = CurrentHello.InstanceId;
                    }
                    _stateClient = client;
                    retryIdx = 0;
                    IsConnected = true;
                    ConnectionStatus = "已连接 LumiBar 后端 (API v1)";
                    LastErrorMessage = "";
                    NotifyUi(() => ConnectionChanged?.Invoke());
                }

                var stateJson = await _stateClient.RequestAsync("state.get", null, ct);
                var dto = JsonSerializer.Deserialize<StateDto>(stateJson.GetRawText(), JsonOpts);
                if (dto != null)
                {
                    Win32WindowHelper.ApplySystemThemeIfFollowing(dto);
                    CurrentState = dto;
                    _lastSnapshotLocalTime = DateTime.UtcNow;
                    _snapshotMediaPosition = dto.Media?.Position ?? 0;
                    if (dto.StateVersion.HasValue) _lastStateVersion = dto.StateVersion.Value;
                    if (!string.IsNullOrEmpty(dto.InstanceId)) _lastInstanceId = dto.InstanceId;

                    await RefreshHintsAsync(ct);
                    await EnsureArtworkLoadedAsync(dto.Media, ct);
                    NotifyUi(() => StateUpdated?.Invoke());
                }

                bool longPollHealthy = (DateTime.UtcNow - _lastEventsPingUtc).TotalSeconds < 30;
                var intervalMs = longPollHealthy ? 15000 : (_isAnyPopupVisible ? 1000 : 3000);
                await Task.Delay(intervalMs, ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _stateClient?.Dispose();
                _stateClient = null;
                SetOffline($"后端已断开：{ex.Message}");
                var wait = backoffSeconds[Math.Min(retryIdx++, backoffSeconds.Length - 1)];
                await Task.Delay(TimeSpan.FromSeconds(wait), ct);
            }
        }
    }

    private async Task RefreshHintsAsync(CancellationToken ct)
    {
        if (_stateClient == null || !_stateClient.IsConnected) return;
        try
        {
            var hintsJson = await _stateClient.RequestAsync("windows.hints", null, ct);
            CurrentHints = JsonSerializer.Deserialize<WindowHintsDto>(hintsJson.GetRawText(), JsonOpts);
        }
        catch { }

        bool supportsReservationQuery = CurrentHello?.Methods == null ||
                                        CurrentHello.Methods.Count == 0 ||
                                        CurrentHello.Methods.Contains("windows.reservation", StringComparer.OrdinalIgnoreCase);
        if (supportsReservationQuery)
        {
            try
            {
                var resJson = await _stateClient.RequestAsync("windows.reservation", null, ct);
                CurrentReservation = JsonSerializer.Deserialize<WindowsReservationDto>(resJson.GetRawText(), JsonOpts);
            }
            catch { }
        }
    }

    private async Task EventsLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            if (_config.UseMockMode || !IsConnected)
            {
                await Task.Delay(500, ct);
                continue;
            }

            try
            {
                if (_eventsClient == null || !_eventsClient.IsConnected)
                {
                    var epPath = _config.ResolveEffectiveEndpointPath();
                    if (!File.Exists(epPath))
                    {
                        await Task.Delay(1000, ct);
                        continue;
                    }
                    var client = new LumiBarClient();
                    await client.ConnectAsync(epPath, ct);
                    _eventsClient = client;
                }

                var batchJson = await _eventsClient.RequestAsync("events.wait", new
                {
                    afterVersion = _lastStateVersion,
                    timeoutMs = 25000
                }, ct);
                _lastEventsPingUtc = DateTime.UtcNow;

                var batch = JsonSerializer.Deserialize<ChangeBatchDto>(batchJson.GetRawText(), JsonOpts);
                if (batch == null) continue;

                bool instanceChanged = !string.IsNullOrEmpty(batch.InstanceId) &&
                                       !string.IsNullOrEmpty(_lastInstanceId) &&
                                       !string.Equals(batch.InstanceId, _lastInstanceId, StringComparison.Ordinal);

                if (batch.ResyncRequired || instanceChanged)
                {
                    _lastInstanceId = batch.InstanceId;
                    _lastStateVersion = batch.Version;
                    if (instanceChanged)
                        NotifyUi(() => InstanceChanged?.Invoke(_lastInstanceId));
                    await ForceStateRefreshAsync();
                    await RefreshHintsAsync(ct);
                    continue;
                }

                if (batch.Version > _lastStateVersion)
                    _lastStateVersion = batch.Version;
                if (!string.IsNullOrEmpty(batch.InstanceId))
                    _lastInstanceId = batch.InstanceId;

                if (batch.TimedOut || batch.Topics.Count == 0)
                    continue;

                bool needState = batch.Topics.Any(t => t is "media" or "weather" or "lyrics" or "settings" or "appearance");
                bool needWindows = batch.Topics.Contains("windows");

                if (needState)
                    await ForceStateRefreshAsync();
                if (needWindows)
                {
                    await RefreshHintsAsync(ct);
                    if (!needState) NotifyUi(() => StateUpdated?.Invoke());
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                break;
            }
            catch
            {
                _eventsClient?.Dispose();
                _eventsClient = null;
                await Task.Delay(1500, ct);
            }
        }
    }

    private async Task SpectrumLoopAsync(CancellationToken ct)
    {
        // Absolute cadence: request latency is part of the 25 ms budget, rather
        // than adding another delay after every reply. Missed ticks coalesce.
        using var cadence = new PeriodicTimer(TimeSpan.FromMilliseconds(25));
        while (!ct.IsCancellationRequested)
        {
            if (_config.UseMockMode)
            {
                var playing = CurrentState.Media?.Playing == true;
                var bands = new double[20];
                if (playing)
                {
                    _mockPhase += 0.19;
                    for (int i = 0; i < 20; i++)
                    {
                        var w1 = Math.Sin(_mockPhase * 1.4 + i * 0.45) * 0.5 + 0.5;
                        var w2 = Math.Cos(_mockPhase * 2.3 - i * 0.25) * 0.5 + 0.5;
                        var bass = i < 5 ? Math.Pow(Math.Sin(_mockPhase) * 0.5 + 0.5, 2) * 0.35 : 0;
                        bands[i] = Math.Clamp(0.15 + w1 * 0.42 + w2 * 0.28 + bass, 0, 1);
                    }
                }
                CurrentSpectrum = new SpectrumDto { Playing = playing, Bands = bands, Status = "mock" };
                NotifySpectrumUi();
                await cadence.WaitForNextTickAsync(ct);
                continue;
            }

            bool shouldPoll = CurrentState.Settings.Spectrum && (CurrentState.Media?.Playing == true);
            if (!shouldPoll)
            {
                if (CurrentSpectrum.Bands.Any(b => b > 0.001))
                {
                    CurrentSpectrum = new SpectrumDto { Playing = false, Bands = new double[20] };
                    NotifySpectrumUi();
                }
                await Task.Delay(180, ct);
                continue;
            }

            try
            {
                if (_spectrumClient == null || !_spectrumClient.IsConnected)
                {
                    var epPath = _config.ResolveEffectiveEndpointPath();
                    if (!File.Exists(epPath))
                    {
                        await Task.Delay(1000, ct);
                        continue;
                    }
                    var client = new LumiBarClient();
                    await client.ConnectAsync(epPath, ct);
                    _spectrumClient = client;
                }

                var specJson = await _spectrumClient.RequestAsync("spectrum.get", null, ct);
                var dto = JsonSerializer.Deserialize<SpectrumDto>(specJson.GetRawText(), JsonOpts);
                if (dto != null)
                {
                    CurrentSpectrum = dto;
                    NotifySpectrumUi();
                }
                await cadence.WaitForNextTickAsync(ct); // 40 Hz, no accumulated RPC drift
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                break;
            }
            catch
            {
                _spectrumClient?.Dispose();
                _spectrumClient = null;
                await Task.Delay(1000, ct);
            }
        }
    }

    private async Task EnsureArtworkLoadedAsync(MediaDto? media, CancellationToken ct)
    {
        var requestedKey = media?.Key;
        if (media == null || !media.HasArtwork || string.IsNullOrEmpty(requestedKey))
        {
            if (CurrentArtwork != null)
            {
                CurrentArtwork = null;
                NotifyUi(() => ArtworkUpdated?.Invoke());
            }
            return;
        }

        if (_artworkCache.TryGetValue(requestedKey, out var cached) && cached != null)
        {
            if (!ReferenceEquals(CurrentArtwork, cached))
            {
                CurrentArtwork = cached;
                NotifyUi(() => ArtworkUpdated?.Invoke());
            }
            return;
        }

        if (_pendingArtworkKey == requestedKey || _stateClient == null) return;
        _pendingArtworkKey = requestedKey;
        try
        {
            var artJson = await _stateClient.RequestAsync("media.artwork", new { trackKey = requestedKey }, ct);
            var dto = JsonSerializer.Deserialize<ArtworkDto>(artJson.GetRawText(), JsonOpts);
            if (!string.Equals(CurrentState.Media?.Key, requestedKey, StringComparison.Ordinal) ||
                CurrentState.Media?.HasArtwork != true)
            {
                // Track changed or artwork disappeared while request was in-flight; discard stale result.
                return;
            }
            if (dto != null && !string.IsNullOrEmpty(dto.Base64) &&
                (string.IsNullOrEmpty(dto.TrackKey) || string.Equals(dto.TrackKey, requestedKey, StringComparison.Ordinal)))
            {
                var bytes = Convert.FromBase64String(dto.Base64);
                var tcs = new TaskCompletionSource<BitmapImage?>();
                _dispatcher.TryEnqueue(async () =>
                {
                    try
                    {
                        using var stream = new InMemoryRandomAccessStream();
                        await stream.WriteAsync(bytes.AsBuffer());
                        stream.Seek(0);
                        var bmp = new BitmapImage();
                        await bmp.SetSourceAsync(stream);
                        tcs.TrySetResult(bmp);
                    }
                    catch
                    {
                        tcs.TrySetResult(null);
                    }
                });

                var image = await tcs.Task;
                if (image == null ||
                    !string.Equals(CurrentState.Media?.Key, requestedKey, StringComparison.Ordinal) ||
                    CurrentState.Media?.HasArtwork != true)
                {
                    return;
                }

                if (_artworkCache.Count > 24) _artworkCache.Clear();
                _artworkCache[requestedKey] = image;
                CurrentArtwork = image;
                NotifyUi(() => ArtworkUpdated?.Invoke());
            }
        }
        catch (LumiBarApiException ex) when (ex.Code == "stale_track")
        {
            // Track changed mid-flight; next state.get will trigger the new key.
        }
        catch { }
        finally
        {
            if (_pendingArtworkKey == requestedKey)
                _pendingArtworkKey = null;
        }
    }

    public async Task<bool> SendMediaCommandAsync(string method, object? parameters = null)
    {
        if (_config.UseMockMode)
        {
            if (CurrentState.Media == null) return false;
            switch (method)
            {
                case "media.toggle":
                    CurrentState.Media.Playing = !CurrentState.Media.Playing;
                    break;
                case "media.next":
                case "media.previous":
                    _mockElapsedSec = 0;
                    CurrentState.Media.Position = 0;
                    CurrentState.Media.Title = CurrentState.Media.Title == "微光" ? "Midnight Bloom" : "微光";
                    CurrentState.Media.Artist = CurrentState.Media.Title == "微光" ? "Lumi Studio" : "Neon Arcade";
                    CurrentState.Media.Accent = CurrentState.Media.Title == "微光" ? "#CB8FB9" : "#38BDF8";
                    break;
                case "media.seek" when parameters != null:
                    var prop = parameters.GetType().GetProperty("seconds");
                    var rawVal = prop?.GetValue(parameters);
                    if (rawVal != null)
                    {
                        double s = Convert.ToDouble(rawVal);
                        _mockElapsedSec = Math.Clamp(s, 0, Math.Max(0, CurrentState.Media.Duration));
                        CurrentState.Media.Position = _mockElapsedSec;
                        _snapshotMediaPosition = _mockElapsedSec;
                        _lastSnapshotLocalTime = DateTime.UtcNow;
                    }
                    break;
                case "media.setShuffle" when parameters != null:
                    var enProp = parameters.GetType().GetProperty("enabled");
                    if (enProp?.GetValue(parameters) is bool en)
                        CurrentState.Media.ShuffleEnabled = en;
                    break;
                case "media.setRepeat" when parameters != null:
                    var modeProp = parameters.GetType().GetProperty("mode");
                    if (modeProp?.GetValue(parameters) is string rm)
                        CurrentState.Media.RepeatMode = rm;
                    break;
            }
            NotifyUi(() => StateUpdated?.Invoke());
            return true;
        }

        try
        {
            await SendSingleActionAsync(method, parameters);
            if (method == "media.seek" && parameters != null && CurrentState.Media != null)
            {
                var rawVal = parameters.GetType().GetProperty("seconds")?.GetValue(parameters);
                if (rawVal != null)
                {
                    double target = Math.Clamp(Convert.ToDouble(rawVal), 0, Math.Max(0, CurrentState.Media.Duration));
                    _snapshotMediaPosition = target;
                    _lastSnapshotLocalTime = DateTime.UtcNow;
                    CurrentState.Media.Position = target;
                }
            }
            await ForceStateRefreshAsync();
            return true;
        }
        catch (Exception ex)
        {
            LastErrorMessage = ex.Message;
            NotifyUi(() => ConnectionChanged?.Invoke());
            return false;
        }
    }

    public async Task<(bool Ok, string Error)> SetShuffleAsync(bool enabled)
    {
        if (CurrentState.Media?.Controls?.Shuffle != true)
            return (false, "当前播放器未开放随机播放控制");
        bool ok = await SendMediaCommandAsync("media.setShuffle", new { enabled });
        return ok ? (true, "") : (false, string.IsNullOrWhiteSpace(LastErrorMessage) ? "播放器拒绝切换随机播放" : LastErrorMessage);
    }

    public async Task<(bool Ok, string Error)> SetRepeatAsync(string mode)
    {
        if (CurrentState.Media?.Controls?.Repeat != true)
            return (false, "当前播放器未开放循环模式控制");
        bool ok = await SendMediaCommandAsync("media.setRepeat", new { mode });
        return ok ? (true, "") : (false, string.IsNullOrWhiteSpace(LastErrorMessage) ? "播放器拒绝切换循环模式" : LastErrorMessage);
    }

    public async Task RefreshWeatherAsync()
    {
        if (_config.UseMockMode) return;
        IsRefreshingWeather = true;
        NotifyUi(() => StateUpdated?.Invoke());
        try
        {
            await SendSingleActionAsync("weather.refresh", null);
            await Task.Delay(500);
            await ForceStateRefreshAsync();
        }
        catch (Exception ex)
        {
            LastErrorMessage = ex.Message;
            NotifyUi(() => ConnectionChanged?.Invoke());
        }
        finally
        {
            IsRefreshingWeather = false;
            NotifyUi(() => StateUpdated?.Invoke());
        }
    }

    public async Task<List<CityDto>> SearchCitiesAsync(string query)
    {
        if (_config.UseMockMode)
        {
            return new List<CityDto>
            {
                new() { Name = "奎文区", Region = "山东省 · 潍坊市", Latitude = 36.7066306, Longitude = 119.1268375, TimeZone = "Asia/Shanghai" },
                new() { Name = "静安区", Region = "上海市", Latitude = 31.227, Longitude = 121.448, TimeZone = "Asia/Shanghai" },
                new() { Name = "海淀区", Region = "北京市", Latitude = 39.959, Longitude = 116.298, TimeZone = "Asia/Shanghai" }
            }.Where(c => c.Name.Contains(query, StringComparison.OrdinalIgnoreCase) || c.Region.Contains(query, StringComparison.OrdinalIgnoreCase)).ToList();
        }

        // Dedicated short-lived connection for slow city search so spectrum and state never block.
        using var client = new LumiBarClient();
        await client.ConnectAsync(_config.EndpointPath);
        var json = await client.RequestAsync("cities.search", new { query });
        return JsonSerializer.Deserialize<List<CityDto>>(json.GetRawText(), JsonOpts) ?? new();
    }

    public async Task SelectCityAsync(CityDto city)
    {
        if (_config.UseMockMode)
        {
            CurrentState.Weather.City = city;
            CurrentState.Settings.City = city;
            NotifyUi(() => StateUpdated?.Invoke());
            return;
        }

        await SendSingleActionAsync("cities.select", new
        {
            name = city.Name,
            latitude = city.Latitude,
            longitude = city.Longitude,
            region = city.Region,
            timeZone = city.TimeZone
        });
        await ForceStateRefreshAsync();
    }

    public async Task<(bool Ok, string Message)> LocateGpsAsync()
    {
        if (_config.UseMockMode)
            return (false, "示例模式下不调用系统 GPS；请切换到真实后端。");

        try
        {
            using var client = new LumiBarClient();
            await client.ConnectAsync(_config.EndpointPath);
            await client.RequestAsync("cities.locateGps");
            await ForceStateRefreshAsync();
            return (true, "已通过 Windows GPS 更新位置");
        }
        catch (Exception ex)
        {
            return (false, $"GPS 定位失败：{ex.Message}（按规范不回退到 IP 定位）");
        }
    }

    public async Task RefreshLyricsAsync()
    {
        if (_config.UseMockMode) return;
        await SendSingleActionAsync("lyrics.refresh", null);
        await Task.Delay(350);
        await ForceStateRefreshAsync();
    }

    public async Task<(bool Ok, string Message)> ImportLyricsAsync(string trackKey, string lrcText)
    {
        if (lrcText.Length > 12000)
            return (false, "歌词文本超过 12,000 字符上限。");

        if (_config.UseMockMode)
        {
            var parsed = new List<LyricLineDto>();
            foreach (var raw in lrcText.Split('\n'))
            {
                var line = raw.Trim();
                if (!string.IsNullOrEmpty(line))
                    parsed.Add(new LyricLineDto { Seconds = null, Text = line });
            }
            CurrentState.Lyrics = new LyricsDto { TrackKey = trackKey, Status = "示例导入歌词", Lines = parsed };
            NotifyUi(() => StateUpdated?.Invoke());
            return (true, "已在示例模式中应用歌词");
        }

        try
        {
            await SendSingleActionAsync("lyrics.import", new { trackKey, text = lrcText });
            await ForceStateRefreshAsync();
            return (true, "歌词已导入");
        }
        catch (Exception ex)
        {
            return (false, ex.Message);
        }
    }

    public async Task<FontsListResponseDto> ListSystemFontsAsync(string query = "", int offset = 0, int limit = 100, bool refresh = false)
    {
        var q = (query ?? "").Trim();
        if (q.Length > 128) q = q[..128];

        if (_config.UseMockMode || !IsConnected)
        {
            var all = SystemFontHelper.GetInstalledFonts(refresh);
            var filtered = string.IsNullOrEmpty(q)
                ? all
                : all.Where(f =>
                    f.DisplayName.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                    f.Family.Contains(q, StringComparison.OrdinalIgnoreCase)).ToList();
            var slice = filtered.Skip(Math.Max(0, offset)).Take(Math.Clamp(limit, 1, 500)).ToList();
            return new FontsListResponseDto
            {
                Source = "windows-gdi-fallback",
                InstalledCount = all.Count,
                Total = filtered.Count,
                Offset = offset,
                HasMore = offset + slice.Count < filtered.Count,
                Items = slice
            };
        }

        try
        {
            using var client = new LumiBarClient();
            await client.ConnectAsync(_config.ResolveEffectiveEndpointPath(), _cts.Token);
            var resJson = await client.RequestAsync("fonts.list", new { query = q, offset, limit = Math.Clamp(limit, 1, 500), refresh }, _cts.Token);
            return JsonSerializer.Deserialize<FontsListResponseDto>(resJson.GetRawText(), JsonOpts) ?? new FontsListResponseDto();
        }
        catch
        {
            return new FontsListResponseDto();
        }
    }

    public async Task<(bool Ok, string Message)> SetFontFamilyAsync(string? fontFamily)
    {
        if (_config.UseMockMode)
        {
            var normalized = string.IsNullOrWhiteSpace(fontFamily) ? null : fontFamily.Trim();
            CurrentState.Settings.FontFamily = normalized;
            CurrentState.Appearance.FontFamily = normalized;
            NotifyUi(() => StateUpdated?.Invoke());
            return (true, normalized == null ? "已恢复系统默认字体" : $"已应用字体：{normalized}");
        }

        try
        {
            var resJson = await SendSingleActionAsync("settings.patch", new { fontFamily });
            var updated = JsonSerializer.Deserialize<SettingsDto>(resJson.GetRawText(), JsonOpts);
            if (updated != null)
            {
                CurrentState.Settings = updated;
                CurrentState.Appearance.FontFamily = updated.FontFamily;
                await ForceStateRefreshAsync();
            }
            return (true, string.IsNullOrWhiteSpace(fontFamily) ? "已恢复系统默认字体" : $"已应用字体：{CurrentState.Settings.FontFamily ?? fontFamily}");
        }
        catch (LumiBarApiException ex)
        {
            return (false, ex.Code == "font_not_found" ? "未找到该系统字体，请点击刷新列表后重试。" : ex.Message);
        }
        catch (Exception ex)
        {
            return (false, ex.Message);
        }
    }

    public async Task PatchSettingsAsync(object patchObject)
    {
        if (_config.UseMockMode)
        {
            var json = JsonSerializer.Serialize(patchObject);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            var s = CurrentState.Settings;
            if (root.TryGetProperty("theme", out var th) && th.GetString() is string themeStr)
            {
                s.Theme = themeStr;
                CurrentState.Appearance.Preference = themeStr;
                Win32WindowHelper.ApplySystemThemeIfFollowing(CurrentState);
            }
            if (root.TryGetProperty("coverGlow", out var cg)) { s.CoverGlow = cg.GetBoolean(); CurrentState.Appearance.CoverGlow = s.CoverGlow; }
            if (root.TryGetProperty("glow", out var gl)) { s.Glow = Math.Clamp(gl.GetDouble(), 0.08, 0.55); CurrentState.Appearance.Glow = s.Glow; }
            if (root.TryGetProperty("glassOpacity", out var go)) { s.GlassOpacity = Math.Clamp(go.GetDouble(), 0.35, 0.95); CurrentState.Appearance.GlassOpacity = s.GlassOpacity; }
            if (root.TryGetProperty("animations", out var an)) { s.Animations = an.GetBoolean(); CurrentState.Appearance.Animations = s.Animations; }
            if (root.TryGetProperty("showWeather", out var sw)) s.ShowWeather = sw.GetBoolean();
            if (root.TryGetProperty("showMusic", out var sm)) s.ShowMusic = sm.GetBoolean();
            if (root.TryGetProperty("spectrum", out var sp)) s.Spectrum = sp.GetBoolean();
            if (root.TryGetProperty("hideInFullscreen", out var hf)) s.HideInFullscreen = hf.GetBoolean();
            if (root.TryGetProperty("autoLyrics", out var al)) s.AutoLyrics = al.GetBoolean();
            if (root.TryGetProperty("weatherRefreshMinutes", out var wm)) s.WeatherRefreshMinutes = wm.GetInt32();
            if (root.TryGetProperty("fahrenheit", out var fh)) s.Fahrenheit = fh.GetBoolean();
            if (root.TryGetProperty("windMetersPerSecond", out var ws)) s.WindMetersPerSecond = ws.GetBoolean();
            if (root.TryGetProperty("gap", out var gp)) s.Gap = Math.Clamp(gp.GetDouble(), 4, 24);
            if (root.TryGetProperty("musicWidth", out var mw)) s.MusicWidth = Math.Clamp(mw.GetDouble(), 180, 340);
            if (root.TryGetProperty("lyricsFontSize", out var lf)) s.LyricsFontSize = Math.Clamp(lf.GetDouble(), 18, 36);
            if (root.TryGetProperty("lyricsOffset", out var lo)) { s.LyricsOffset = Math.Clamp(lo.GetDouble(), -5, 5); CurrentState.Lyrics.OffsetSeconds = s.LyricsOffset; }
            if (root.TryGetProperty("sensitivity", out var se)) s.Sensitivity = Math.Clamp(se.GetDouble(), 0.5, 2.5);
            if (root.TryGetProperty("fontFamily", out var ff))
            {
                s.FontFamily = ff.ValueKind == JsonValueKind.Null ? null : ff.GetString();
                CurrentState.Appearance.FontFamily = s.FontFamily;
            }
            NotifyUi(() => StateUpdated?.Invoke());
            return;
        }

        try
        {
            var resJson = await SendSingleActionAsync("settings.patch", patchObject);
            var updated = JsonSerializer.Deserialize<SettingsDto>(resJson.GetRawText(), JsonOpts);
            if (updated != null)
            {
                CurrentState.Settings = updated;
                await ForceStateRefreshAsync();
            }
        }
        catch (Exception ex)
        {
            LastErrorMessage = ex.Message;
            NotifyUi(() => ConnectionChanged?.Invoke());
        }
    }

    public void ToggleMockSession(bool hasMedia, bool isPlaying)
    {
        if (!_config.UseMockMode) return;
        if (!hasMedia)
        {
            CurrentState.Media = null;
        }
        else
        {
            CurrentState.Media ??= CreateMockState().Media;
            if (CurrentState.Media != null)
                CurrentState.Media.Playing = isPlaying;
        }
        NotifyUi(() => StateUpdated?.Invoke());
    }

    private async Task<JsonElement> SendSingleActionAsync(string method, object? parameters)
    {
        await _actionLock.WaitAsync(_cts.Token);
        try
        {
            if (_stateClient == null || !_stateClient.IsConnected)
            {
                using var temp = new LumiBarClient();
                await temp.ConnectAsync(_config.ResolveEffectiveEndpointPath(), _cts.Token);
                return await temp.RequestAsync(method, parameters, _cts.Token);
            }
            return await _stateClient.RequestAsync(method, parameters, _cts.Token);
        }
        finally
        {
            _actionLock.Release();
        }
    }

    public async Task RefreshWindowsHintsAsync()
    {
        await RefreshHintsAsync(_cts.Token);
        NotifyUi(() => StateUpdated?.Invoke());
    }

    private async Task ForceStateRefreshAsync()
    {
        if (_stateClient == null || !_stateClient.IsConnected) return;
        try
        {
            var json = await _stateClient.RequestAsync("state.get", null, _cts.Token);
            var dto = JsonSerializer.Deserialize<StateDto>(json.GetRawText(), JsonOpts);
            if (dto != null)
            {
                Win32WindowHelper.ApplySystemThemeIfFollowing(dto);
                CurrentState = dto;
                _lastSnapshotLocalTime = DateTime.UtcNow;
                _snapshotMediaPosition = dto.Media?.Position ?? 0;
                if (dto.StateVersion.HasValue && dto.StateVersion.Value > _lastStateVersion)
                    _lastStateVersion = dto.StateVersion.Value;
                if (!string.IsNullOrEmpty(dto.InstanceId))
                    _lastInstanceId = dto.InstanceId;
                await EnsureArtworkLoadedAsync(dto.Media, _cts.Token);
                NotifyUi(() => StateUpdated?.Invoke());
            }
        }
        catch { }
    }

    private void SetOffline(string message)
    {
        IsConnected = false;
        CurrentHello = null;
        CurrentHints = null;
        CurrentReservation = null;
        ConnectionStatus = "离线（保留最近快照）";
        LastErrorMessage = message;
        NotifyUi(() => ConnectionChanged?.Invoke());
    }

    private int _spectrumUiPending;
    private void NotifySpectrumUi()
    {
        if (Interlocked.Exchange(ref _spectrumUiPending, 1) != 0) return;
        if (!_dispatcher.TryEnqueue(() =>
        {
            Interlocked.Exchange(ref _spectrumUiPending, 0);
            SpectrumUpdated?.Invoke();
        })) Interlocked.Exchange(ref _spectrumUiPending, 0);
    }

    private void NotifyUi(Action action) => _dispatcher.TryEnqueue(() => action());

    private int _mockWeatherSceneIndex = -1;

    public void CycleMockWeatherScene()
    {
        if (!_config.UseMockMode) return;
        var scenes = new (int Code, bool IsDay, string Label, double Temp, int Rain, int Humidity, double Wind)[]
        {
            (0, false, "晴夜星空", 9, 0, 48, 8),
            (63, false, "中雨 · 玻璃水滴", 13, 85, 86, 18),
            (95, false, "雷阵雨", 15, 95, 90, 26),
            (73, false, "中雪", -1, 70, 78, 14),
            (0, true, "晴朗暖阳", 21, 5, 40, 10),
            (45, true, "轻雾", 10, 15, 92, 5),
            (2, true, "多云", 16, 20, 58, 12)
        };
        _mockWeatherSceneIndex = (_mockWeatherSceneIndex + 1) % scenes.Length;
        var s = scenes[_mockWeatherSceneIndex];
        if (CurrentState.Weather.Forecast == null) return;
        CurrentState.Weather.Forecast.Code = s.Code;
        CurrentState.Weather.Forecast.IsDay = s.IsDay;
        CurrentState.Weather.Forecast.Temperature = s.Temp;
        CurrentState.Weather.Forecast.FeelsLike = s.Temp - 1;
        CurrentState.Weather.Forecast.Rain = s.Rain;
        CurrentState.Weather.Forecast.Humidity = s.Humidity;
        CurrentState.Weather.Forecast.Wind = s.Wind;
        CurrentState.Weather.Status = $"示例场景 · {s.Label}";
        NotifyUi(() => StateUpdated?.Invoke());
    }

    private static StateDto CreateMockState() => new()
    {
        ProtocolVersion = 1,
        CapturedAt = DateTime.UtcNow.ToString("O"),
        Appearance = new AppearanceDto
        {
            Preference = "System",
            PopupTheme = Win32WindowHelper.IsSystemAppsDark() ? "Dark" : "Light",
            TaskbarTheme = Win32WindowHelper.IsSystemTaskbarDark() ? "Dark" : "Light",
            CoverGlow = true,
            Glow = 0.28,
            GlassOpacity = 0.62,
            Animations = true
        },
        Weather = new WeatherSnapshotDto
        {
            City = new CityDto
            {
                Name = "奎文区",
                Region = "山东省 · 潍坊市",
                Latitude = 36.7066306,
                Longitude = 119.1268375,
                TimeZone = "Asia/Shanghai"
            },
            Status = "示例数据 · 更新于 17:00",
            PrecipitationNextHour = new PrecipitationTrendDto
            {
                WindowStart = "2026-11-14T17:00:00+08:00",
                WindowEnd = "2026-11-14T18:00:00+08:00",
                Source = "Mock · 15分钟趋势",
                Kind = "model-trend",
                IntervalMinutes = 15,
                PrecisionNote = "15 分钟示例趋势（非实时雷达）",
                Complete = true,
                Intervals = new List<RainIntervalDto>
                {
                    new() { Start = "2026-11-14T17:00:00+08:00", End = "2026-11-14T17:15:00+08:00", AmountMm = 0.2 },
                    new() { Start = "2026-11-14T17:15:00+08:00", End = "2026-11-14T17:30:00+08:00", AmountMm = 0.6 },
                    new() { Start = "2026-11-14T17:30:00+08:00", End = "2026-11-14T17:45:00+08:00", AmountMm = 0.9 },
                    new() { Start = "2026-11-14T17:45:00+08:00", End = "2026-11-14T18:00:00+08:00", AmountMm = 0.3 }
                }
            },
            Forecast = new ForecastDto
            {
                Temperature = 12,
                FeelsLike = 11,
                Code = 0,
                Humidity = 52,
                Wind = 12,
                WindDirection = 120,
                Rain = 10,
                Aqi = 83,
                Pm25 = 25,
                Pm10 = 41,
                Source = "Mock · 示例数据",
                Updated = DateTime.Now.ToString("yyyy-MM-ddTHH:mm:sszzz"),
                TimeZoneLabel = "城市当地时间",
                Sunrise = "2026-11-14T06:38:00+08:00",
                Sunset = "2026-11-14T17:04:00+08:00",
                IsDay = !(DateTime.Now.Hour < 6 || DateTime.Now.Hour >= 18),
                Hours = new List<HourForecastDto>
                {
                    new() { Time = "2026-11-14T17:00:00", Temperature = 12, Code = 0, Rain = 10, IsDay = true },
                    new() { Time = "2026-11-14T18:00:00", Temperature = 11, Code = 1, Rain = 10, IsDay = false },
                    new() { Time = "2026-11-14T19:00:00", Temperature = 10, Code = 2, Rain = 20, IsDay = false },
                    new() { Time = "2026-11-14T20:00:00", Temperature = 9, Code = 3, Rain = 20, IsDay = false },
                    new() { Time = "2026-11-14T21:00:00", Temperature = 9, Code = 2, Rain = 15, IsDay = false },
                    new() { Time = "2026-11-14T22:00:00", Temperature = 8, Code = 1, Rain = 5, IsDay = false },
                    new() { Time = "2026-11-14T23:00:00", Temperature = 7, Code = 0, Rain = 0, IsDay = false },
                    new() { Time = "2026-11-15T00:00:00", Temperature = 7, Code = 0, Rain = 0, IsDay = false }
                },
                Days = new List<DayForecastDto>
                {
                    new() { Date = "2026-11-14T00:00:00", Min = 6, Max = 14, Code = 0, Rain = 10 },
                    new() { Date = "2026-11-15T00:00:00", Min = 7, Max = 15, Code = 2, Rain = 20 },
                    new() { Date = "2026-11-16T00:00:00", Min = 8, Max = 14, Code = 3, Rain = 30 },
                    new() { Date = "2026-11-17T00:00:00", Min = 5, Max = 12, Code = 61, Rain = 65 },
                    new() { Date = "2026-11-18T00:00:00", Min = 4, Max = 13, Code = 1, Rain = 10 }
                }
            }
        },
        Media = new MediaDto
        {
            Key = "mock-lumilight",
            Title = "微光",
            Artist = "Lumi Studio",
            Album = "窗边的光",
            Source = "示例播放器",
            Playing = true,
            Position = 84,
            Duration = 222,
            HasArtwork = false,
            Accent = "#CB8FB9",
            ShuffleEnabled = false,
            RepeatMode = "None",
            Controls = new MediaControlsDto { Toggle = true, Previous = true, Next = true, Seek = true, Shuffle = true, Repeat = true }
        },
        Lyrics = new LyricsDto
        {
            TrackKey = "mock-lumilight",
            Status = "示例歌词",
            OffsetSeconds = 0,
            Lines = new List<LyricLineDto>
            {
                new() { Seconds = 70, Text = "把光留在窗边\nLeave the warm light by the window" },
                new() { Seconds = 80, Text = "让旋律慢慢靠近\nLet the gentle melody drift closer" },
                new() { Seconds = 90, Text = "此刻只听见你\nNothing else in the quiet glass" },
                new() { Seconds = 102, Text = "任务栏尽头亮起一抹微光" },
                new() { Seconds = 115, Text = "随音浪一起轻轻呼吸" }
            }
        },
        Settings = new SettingsDto
        {
            City = new CityDto { Name = "奎文区", Region = "山东省 · 潍坊市", Latitude = 36.7066306, Longitude = 119.1268375, TimeZone = "Asia/Shanghai" },
            Theme = "System",
            Glow = 0.28,
            GlassOpacity = 0.62,
            CoverGlow = true,
            Animations = true,
            ShowWeather = true,
            ShowMusic = true,
            Spectrum = true,
            WeatherRefreshMinutes = 20,
            Gap = 8,
            MusicWidth = 238,
            LyricsFontSize = 22,
            LyricsOffset = 0,
            Sensitivity = 1.0
        }
    };

    public void Dispose()
    {
        _uiSettings.ColorValuesChanged -= OnSystemColorValuesChanged;
        _cts.Cancel();
        _stateClient?.Dispose();
        _spectrumClient?.Dispose();
        _eventsClient?.Dispose();
        _cts.Dispose();
    }
}
