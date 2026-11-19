import AppKit
import CoreLocation
import MapKit
import SwiftUI

@MainActor
@Observable
final class WeatherModel: NSObject, CLLocationManagerDelegate {
    private let defaults: UserDefaults
    private let locator = CLLocationManager()
    private var refreshTask: Task<Void, Never>?
    private var locationTask: Task<Void, Never>?
    private var locationPending = false

    var weather: Weather?
    var error: String?
    var locationError: String?
    var loading = false
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
        let decoder = JSONDecoder()
        city = defaults.data(forKey: "weather.city").flatMap { try? decoder.decode(City.self, from: $0) }
        favorites = defaults.data(forKey: "weather.favorites").flatMap { try? decoder.decode([City].self, from: $0) } ?? []
        proxy = defaults.string(forKey: "weather.proxy") ?? WeatherAPI.defaultProxy
        proxyEnabled = defaults.object(forKey: "weather.proxy.enabled") as? Bool ?? true
        super.init()
        locator.desiredAccuracy = kCLLocationAccuracyKilometer
        locator.delegate = self
    }

    func select(_ city: City) {
        locationPending = false
        locationTask?.cancel()
        locationError = nil
        if self.city?.id != city.id { weather = nil }
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
        guard let city else { return }
        refreshTask?.cancel()
        loading = true
        error = nil
        onChange?()
        refreshTask = Task {
            do {
                let result = try await WeatherAPI.forecast(city, proxy: proxyEnabled ? proxy : "")
                try Task.checkCancellation()
                weather = result
            } catch {
                guard !Task.isCancelled else { return }
                self.error = "天气更新失败，请检查网络后重试。"
            }
            loading = false
            onChange?()
        }
    }

    func locate() {
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
                let request = MKReverseGeocodingRequest(location: location)
                let results = try? await request?.mapItems
                guard !Task.isCancelled else { return }
                let item = results?.first
                let coordinate = location.coordinate
                select(City(name: item?.addressRepresentations?.cityName ?? item?.name ?? "当前位置",
                            latitude: coordinate.latitude, longitude: coordinate.longitude, region: nil))
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
    var lyricsLoading = false
    var mode: Mode = .compact
    var automationError: String?
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

    func control(_ command: PlaybackCommand) {
        guard let track, commandTask == nil else { return }
        commandTask = Task {
            if case .failure(let error) = await PlayerBridge.control(command, player: track.player), error.denied {
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
        select(playing.first(where: { $0.player == track?.player }) ?? playing.first
               ?? candidates.first(where: { $0.player == track?.player }) ?? candidates.first)
    }

    private func select(_ next: Track?) {
        let previous = track
        track = next
        if previous?.key != next?.key {
            artwork = nil
            colors = neutralCoverColors
            lyrics = []
            if next == nil { mode = .compact }
            loadMetadata()
        }
        if previous?.playing != next?.playing || previous?.pid != next?.pid { updateSpectrum() }
        if previous?.key != next?.key { onTrackChange?() }
    }

    private func loadMetadata() {
        metadataTask?.cancel()
        lyricsLoading = false
        guard let track else { return }
        lyricsLoading = true
        metadataTask = Task {
            async let cover = PlayerBridge.artwork(for: track)
            let local = Self.lyricFolder()?.appending(path: Self.lyricFilename(track))
            let cached = local.flatMap { try? String(contentsOf: $0, encoding: .utf8) }
            let shouldFetch = cached == nil && onlineLyrics
            async let remote = shouldFetch ? Lyric.fetch(track) : []
            if let data = await cover, !Task.isCancelled, let image = NSImage(data: data) {
                artwork = image
                colors = coverColors(image)
            }
            let result = await remote
            guard !Task.isCancelled else { return }
            lyrics = cached.map(Lyric.parse) ?? result
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
        let pid = spectrumEnabled && track?.playing == true ? track?.pid : nil
        guard lastSpectrumPID != pid else {
            if pid == nil { spectrum.stop() }
            return
        }
        lastSpectrumPID = pid
        if let pid {
            audioError = spectrum.start(pid: pid) ? nil : "频谱未能启动，请检查「屏幕与系统录音」权限。"
        } else {
            spectrum.stop()
        }
    }
}
