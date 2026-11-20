import AppKit
import ApplicationServices
import CoreLocation
import SwiftUI

@MainActor
@Observable
final class WeatherModel: NSObject, CLLocationManagerDelegate {
    private let defaults: UserDefaults
    private let locator = CLLocationManager()
    private var refreshTask: Task<Void, Never>?
    private var locationTask: Task<Void, Never>?
    private var locationPending = false

    var isPreview = false
    var weather: Weather?
    var error: String?
    var locationError: String?
    var loading = false
    var fahrenheit: Bool {
        didSet { defaults.set(fahrenheit, forKey: "weather.fahrenheit"); onChange?() }
    }
    var windMetersPerSecond: Bool {
        didSet { defaults.set(windMetersPerSecond, forKey: "weather.windMetersPerSecond") }
    }
    var locatedCity: City?
    var usingLocation: Bool {
        didSet { defaults.set(usingLocation, forKey: "weather.location.selected") }
    }
    var city: City?
    var favorites: [City]
    var proxy: String {
        didSet { defaults.set(proxy, forKey: "weather.proxy") }
    }
    var proxyEnabled: Bool {
        didSet { defaults.set(proxyEnabled, forKey: "weather.proxy.enabled") }
    }
    var onChange: (() -> Void)?

    init(defaults: UserDefaults) {
        self.defaults = defaults
        fahrenheit = defaults.bool(forKey: "weather.fahrenheit")
        windMetersPerSecond = defaults.bool(forKey: "weather.windMetersPerSecond")
        let wasLocated = defaults.bool(forKey: "weather.location.selected")
        usingLocation = wasLocated
        let decoder = JSONDecoder()
        let savedCity = defaults.data(forKey: "weather.city").flatMap { try? decoder.decode(City.self, from: $0) }
        city = savedCity
        favorites = defaults.data(forKey: "weather.favorites").flatMap { try? decoder.decode([City].self, from: $0) } ?? []
        if wasLocated { locatedCity = savedCity }
        if savedCity != nil {
            weather = defaults.data(forKey: "weather.cached").flatMap { try? decoder.decode(Weather.self, from: $0) }
        }
        proxy = defaults.string(forKey: "weather.proxy") ?? WeatherAPI.defaultProxy
        proxyEnabled = defaults.object(forKey: "weather.proxy.enabled") as? Bool ?? true
        super.init()
        locator.desiredAccuracy = kCLLocationAccuracyKilometer
        locator.delegate = self
    }

    func temperature(_ celsius: Double) -> String {
        let physical = fahrenheit ? celsius * 1.8 + 32 : celsius
        let value = physical.rounded(.toNearestOrEven)
        return (value == 0 ? "0" : value.formatted(.number.precision(.fractionLength(0)).grouping(.never))) + "°"
    }

    func saveAndSelect(_ city: City) {
        if !favorites.contains(where: { $0.id == city.id }) {
            favorites = Array((favorites + [city]).suffix(30))
            defaults.set(try? JSONEncoder().encode(favorites), forKey: "weather.favorites")
        }
        select(city)
    }

    func select(_ city: City, automatic: Bool = false) {
        if automatic { locatedCity = city }
        usingLocation = automatic
        locationPending = false
        locationTask?.cancel()
        locationError = nil
        if self.city?.id != city.id {
            weather = nil
            defaults.removeObject(forKey: "weather.cached")
        }
        self.city = city
        defaults.set(try? JSONEncoder().encode(city), forKey: "weather.city")
        refresh()
    }

    func toggleFavorite(_ city: City) {
        if let index = favorites.firstIndex(where: { $0.id == city.id }) {
            favorites.remove(at: index)
        } else if favorites.count < 30 {
            favorites.append(city)
        }
        defaults.set(try? JSONEncoder().encode(favorites), forKey: "weather.favorites")
    }

    func refresh() {
        guard !isPreview, let city else { return }
        refreshTask?.cancel()
        loading = true
        error = nil
        onChange?()
        refreshTask = Task {
            do {
                let result = try await WeatherAPI.forecast(city, proxy: proxyEnabled ? proxy : "")
                try Task.checkCancellation()
                weather = result
                defaults.set(try? JSONEncoder().encode(result), forKey: "weather.cached")
            } catch {
                guard !Task.isCancelled else { return }
                self.error = "天气更新失败，请检查网络后重试。"
            }
            loading = false
            onChange?()
        }
    }

    func locate() {
        guard !isPreview else { return }
        locationError = nil
        locationPending = true
        updateLocationAuthorization()
    }

    private func updateLocationAuthorization(requestPermission: Bool = true) {
        guard locationPending else { return }
        switch locator.authorizationStatus {
        case .notDetermined:
            if requestPermission { locator.requestWhenInUseAuthorization() }
        case .authorizedAlways, .authorizedWhenInUse:
            locator.requestLocation()
        default:
            locationPending = false
            locationError = "定位权限不可用，可以在系统设置中开启，或搜索城市。"
        }
    }

    nonisolated func locationManagerDidChangeAuthorization(_ manager: CLLocationManager) {
        Task { @MainActor in updateLocationAuthorization(requestPermission: false) }
    }

    nonisolated func locationManager(_ manager: CLLocationManager, didUpdateLocations locations: [CLLocation]) {
        guard let location = locations.last else { return }
        Task { @MainActor in
            guard locationPending else { return }
            locationPending = false
            locationTask?.cancel()
            locationTask = Task {
                guard let city = await WeatherAPI.district(for: location), !Task.isCancelled else { return }
                select(city, automatic: true)
            }
        }
    }

    nonisolated func locationManager(_ manager: CLLocationManager, didFailWithError error: any Error) {
        Task { @MainActor in
            guard locationPending else { return }
            locationPending = false
            locationError = "无法获取位置。请检查定位权限，或搜索城市。"
        }
    }
}

@MainActor
@Observable
final class MusicModel {
    enum Mode: Int { case compact, controls, lyrics }
    private let defaults: UserDefaults
    private var polling: Task<Void, Never>?
    private var metadataTask: Task<Void, Never>?
    private var commandTask: Task<Void, Never>?
    private var deniedPlayers = Set<Player>()
    private var lastSpectrumPID: pid_t?
    var track: Track?
    var artwork: NSImage?
    var colors = neutralCoverColors
    var lyrics: [Lyric] = []
    var previewSpectrum: [Double]?
    var lyricsProvider: String?
    var lyricsCaption: String {
        if lyricsLoading { return "查找歌词中" }
        if lyrics.isEmpty { return "暂无歌词" }
        return (lyricsProvider ?? "同步") + "歌词"
    }
    var lyricsLoading = false
    var mode: Mode = .compact
    var automationError: String?
    var neteaseStatus: String?
    var audioError: String?
    var onTrackChange: (() -> Void)?
    let spectrum = Spectrum()

    var showMusic: Bool {
        didSet {
            defaults.set(showMusic, forKey: "music.enabled")
            if showMusic {
                start()
            } else {
                polling?.cancel()
                polling = nil
                select(nil)
            }
        }
    }
    var spectrumEnabled: Bool {
        didSet {
            defaults.set(spectrumEnabled, forKey: "music.spectrum")
            lastSpectrumPID = nil
            updateSpectrum()
        }
    }
    var onlineLyrics: Bool {
        didSet {
            defaults.set(onlineLyrics, forKey: "music.lyrics.online")
            loadMetadata()
        }
    }
    private var offsets: [String: Double] {
        didSet { defaults.set(offsets, forKey: "music.offsets") }
    }
    var offset: Double { track.flatMap { offsets[$0.key] } ?? 0 }

    init(defaults: UserDefaults) {
        self.defaults = defaults
        showMusic = defaults.object(forKey: "music.enabled") as? Bool ?? true
        spectrumEnabled = defaults.bool(forKey: "music.spectrum")
        onlineLyrics = defaults.object(forKey: "music.lyrics.online") as? Bool ?? true
        offsets = defaults.dictionary(forKey: "music.offsets") as? [String: Double] ?? [:]
    }

    func start() {
        guard previewSpectrum == nil else { return }
        guard showMusic, polling == nil else { return }
        polling = Task {
            while !Task.isCancelled {
                await poll()
                try? await Task.sleep(for: .seconds(track == nil ? 2 : 1))
            }
        }
    }

    func retryAutomation() {
        deniedPlayers = []
        automationError = nil
    }

    func connectNetease() {
        guard previewSpectrum == nil else { return }
        let options = ["AXTrustedCheckOptionPrompt": true] as CFDictionary
        if AXIsProcessTrustedWithOptions(options) {
            neteaseStatus = "已授权。请在网易云音乐中播放一首歌。"
        } else {
            neteaseStatus = "请在「隐私与安全性 → 辅助功能」打开 LumiBar，再在网易云音乐中播放一首歌。"
        }
    }

    func control(_ command: PlaybackCommand) {
        guard previewSpectrum == nil else { return }
        guard let track, track.canControl, commandTask == nil else { return }
        commandTask = Task {
            if track.player == .netease {
                let success = await NeteaseBridge.shared.control(command, pid: track.pid)
                if !success { neteaseStatus = "网易云当前的播放控件不可用。请打开它的播放窗口后重试。" }
            } else if case .failure(let error) = await PlayerBridge.control(command, player: track.player), error.denied {
                automationError = "请在系统设置的「隐私与安全性 → 自动化」允许 LumiBar 控制\(track.player.name)。"
            }
            commandTask = nil
        }
    }

    func adjustOffset(_ delta: Double) {
        guard let track else { return }
        let next = max(-60, min(60, ((offset + delta) * 2).rounded() / 2))
        offsets[track.key] = next == 0 ? nil : next
    }

    func loadLRC() {
        guard previewSpectrum == nil else { return }
        guard let track else { return }
        let picker = NSOpenPanel()
        picker.title = "导入同步歌词"
        picker.allowedContentTypes = [.init(filenameExtension: "lrc") ?? .plainText]
        picker.begin { [weak self] response in
            MainActor.assumeIsolated {
                guard response == .OK, let url = picker.url, let self, self.track?.key == track.key,
                      let values = try? url.resourceValues(forKeys: [.fileSizeKey]),
                      let size = values.fileSize, size <= 1_048_576,
                      let string = try? String(contentsOf: url, encoding: .utf8) else { return }
                let parsed = Lyric.parse(string)
                guard !parsed.isEmpty else { return }
                self.lyrics = parsed
                self.lyricsProvider = "本地 LRC "
                self.lyricsLoading = false
                if let folder = Self.lyricFolder() {
                    try? Data(string.utf8).write(to: folder.appending(path: Self.lyricFilename(track)), options: .atomic)
                }
            }
        }
    }

    private func poll() async {
        guard commandTask == nil else { return }
        var candidates: [Track] = []
        for player in Player.allCases where !deniedPlayers.contains(player) {
            guard let running = NSRunningApplication.runningApplications(withBundleIdentifier: player.rawValue).first else { continue }
            if player == .netease {
                let trusted = AXIsProcessTrusted()
                if let netease = await NeteaseBridge.shared.read(pid: running.processIdentifier, trusted: trusted) {
                    candidates.append(netease)
                    if !trusted {
                        neteaseStatus = "已识别当前曲目。允许「辅助功能」后可使用播放控件。"
                    } else if !netease.canControl {
                        neteaseStatus = "已识别当前曲目。请保持网易云主窗口打开以读取播放控件。"
                    } else if netease.estimatedPosition {
                        neteaseStatus = "已连接网易云音乐，正在使用估算进度。"
                    } else {
                        neteaseStatus = "已连接网易云音乐。"
                    }
                } else {
                    neteaseStatus = trusted ? "已授权，等待网易云主窗口的当前曲目。"
                                           : "网易云音乐已打开。连接播放控件时需要允许辅助功能。"
                }
                continue
            }
            let result = await PlayerBridge.read(player, pid: running.processIdentifier)
            guard !Task.isCancelled else { return }
            switch result {
            case .success(let value):
                if let value { candidates.append(value) }
            case .failure(let error) where error.denied:
                deniedPlayers.insert(player)
                automationError = "请在系统设置的「隐私与安全性 → 自动化」允许 LumiBar 读取\(player.name)。"
            case .failure: break
            }
        }
        guard !Task.isCancelled, commandTask == nil else { return }
        let playing = candidates.filter(\.playing)
        select(playing.first(where: { $0.player == .netease })
               ?? playing.first(where: { $0.player == track?.player }) ?? playing.first
               ?? candidates.first(where: { $0.player == track?.player }) ?? candidates.first)
    }

    private func select(_ next: Track?) {
        let previous = track
        track = next
        if previous?.key != next?.key {
            artwork = nil
            colors = neutralCoverColors
            lyrics = []
            lyricsProvider = nil
            if next == nil { mode = .compact }
            loadMetadata()
        }
        if previous?.playing != next?.playing || previous?.pid != next?.pid { updateSpectrum() }
        if previous?.key != next?.key || previous?.playing != next?.playing { onTrackChange?() }
    }

    private func loadMetadata() {
        guard previewSpectrum == nil else { return }
        metadataTask?.cancel()
        lyricsLoading = false
        guard let track else { return }
        lyricsLoading = true
        metadataTask = Task {
            async let cover = PlayerBridge.artwork(for: track)
            let local = Self.lyricFolder()?.appending(path: Self.lyricFilename(track))
            let cached = local.flatMap { try? String(contentsOf: $0, encoding: .utf8) }
            let shouldFetch = cached == nil && onlineLyrics
            async let remote = shouldFetch ? Lyric.fetch(track) : nil
            if let data = await cover, !Task.isCancelled, let image = NSImage(data: data) {
                artwork = image
                colors = coverColors(image)
            }
            let result = await remote
            guard !Task.isCancelled else { return }
            lyrics = cached.map(Lyric.parse) ?? result?.lines ?? []
            lyricsProvider = cached == nil ? result?.provider : "本地 LRC "
            lyricsLoading = false
        }
    }

    private static func lyricFolder() -> URL? {
        guard let root = try? FileManager.default.url(for: .applicationSupportDirectory, in: .userDomainMask,
                                                     appropriateFor: nil, create: true) else { return nil }
        let folder = root.appending(path: "LumiBar/Lyrics")
        do { try FileManager.default.createDirectory(at: folder, withIntermediateDirectories: true) }
        catch { return nil }
        return folder
    }

    private static func lyricFilename(_ track: Track) -> String {
        lyricCacheKey(track.key) + ".lrc"
    }

    func restartSpectrum() {
        lastSpectrumPID = nil
        updateSpectrum()
    }

    private func updateSpectrum() {
        guard previewSpectrum == nil else { return }
        let pid = spectrumEnabled && track?.playing == true ? track?.pid : nil
        guard lastSpectrumPID != pid else {
            if pid == nil { spectrum.stop() }
            return
        }
        lastSpectrumPID = pid
        if let pid, let track {
            audioError = spectrum.start(pid: pid, bundleID: track.player.rawValue) ? nil : "频谱未能启动，请检查「屏幕与系统录音」权限。"
        } else {
            spectrum.stop()
        }
    }
}
