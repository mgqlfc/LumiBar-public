import AppKit
import ApplicationServices
import CoreLocation
import SwiftUI

@MainActor
@Observable
final class WeatherModel: NSObject, CLLocationManagerDelegate {
    static let favoriteLimit = 30
    private static let defaultTintStrength = 0.65
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
    var showWeather: Bool {
        didSet { defaults.set(showWeather, forKey: "weather.enabled"); onChange?() }
    }
    var capsuleGlow: Bool {
        didSet { defaults.set(capsuleGlow, forKey: "weather.capsuleGlow"); onChange?() }
    }
    var capsuleTint: Bool {
        didSet { defaults.set(capsuleTint, forKey: "weather.capsuleTint"); onChange?() }
    }
    var capsuleTintStrength: Double {
        didSet { defaults.set(capsuleTintStrength, forKey: "weather.capsuleTintStrength"); onChange?() }
    }
    var capsuleTintSaturation: Double {
        didSet { defaults.set(capsuleTintSaturation, forKey: "weather.capsuleTintSaturation"); onChange?() }
    }
    var tintScale: Double { capsuleTint ? capsuleTintStrength / Self.defaultTintStrength : 0 }
    var liteCapsule: Bool {
        didSet {
            defaults.set(liteCapsule, forKey: "weather.liteCapsule")
            onChange?()
        }
    }
    var fahrenheit: Bool {
        didSet { defaults.set(fahrenheit, forKey: "weather.fahrenheit"); onChange?() }
    }
    var windUnit: WeatherWindUnit {
        didSet { defaults.set(windUnit.rawValue, forKey: "weather.windUnit") }
    }
    private(set) var hiddenDetailItems: Set<WeatherDetailItem> {
        didSet { defaults.set(hiddenDetailItems.map(\.rawValue).sorted(), forKey: "weather.hiddenDetailItems") }
    }
    var locatedCity: City?
    var usingLocation: Bool {
        didSet { defaults.set(usingLocation, forKey: "weather.location.selected") }
    }
    var city: City?
    var favorites: [City] {
        didSet { defaults.set(try? JSONEncoder().encode(favorites), forKey: "weather.favorites") }
    }
    var proxy: String {
        didSet { defaults.set(proxy, forKey: "weather.proxy") }
    }
    var proxyEnabled: Bool {
        didSet { defaults.set(proxyEnabled, forKey: "weather.proxy.enabled") }
    }
    var onChange: (() -> Void)?

    init(defaults: UserDefaults) {
        self.defaults = defaults
        showWeather = defaults.object(forKey: "weather.enabled") as? Bool ?? true
        capsuleGlow = defaults.object(forKey: "weather.capsuleGlow") as? Bool ?? true
        capsuleTint = defaults.object(forKey: "weather.capsuleTint") as? Bool ?? true
        capsuleTintStrength = Self.tintValue(defaults.object(forKey: "weather.capsuleTintStrength") as? Double,
                                             fallback: Self.defaultTintStrength)
        capsuleTintSaturation = Self.tintValue(defaults.object(forKey: "weather.capsuleTintSaturation") as? Double, fallback: 1)
        liteCapsule = defaults.bool(forKey: "weather.liteCapsule")
        fahrenheit = defaults.bool(forKey: "weather.fahrenheit")
        windUnit = defaults.string(forKey: "weather.windUnit").flatMap(WeatherWindUnit.init(rawValue:))
            ?? (defaults.bool(forKey: "weather.windMetersPerSecond") ? .metersPerSecond : .kilometersPerHour)
        hiddenDetailItems = Set((defaults.stringArray(forKey: "weather.hiddenDetailItems") ?? [])
            .compactMap(WeatherDetailItem.init(rawValue:)))
        let wasLocated = defaults.bool(forKey: "weather.location.selected")
        usingLocation = wasLocated
        let decoder = JSONDecoder()
        let savedCity = defaults.data(forKey: "weather.city").flatMap { try? decoder.decode(City.self, from: $0) }
        city = savedCity
        let savedFavorites = defaults.data(forKey: "weather.favorites").flatMap { try? decoder.decode([City].self, from: $0) } ?? []
        favorites = Array(savedFavorites.uniqued().prefix(Self.favoriteLimit))
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

    private static func tintValue(_ value: Double?, fallback: Double) -> Double {
        guard let value, value.isFinite else { return fallback }
        return min(1, max(0, value))
    }

    func temperature(_ celsius: Double) -> String {
        let physical = fahrenheit ? celsius * 1.8 + 32 : celsius
        let value = physical.rounded(.toNearestOrEven)
        return (value == 0 ? "0" : value.formatted(.number.precision(.fractionLength(0)).grouping(.never))) + "°"
    }

    func saveAndSelect(_ city: City) {
        addFavorite(city)
        select(city)
    }

    func showsDetail(_ item: WeatherDetailItem) -> Bool { !hiddenDetailItems.contains(item) }

    func setDetail(_ item: WeatherDetailItem, visible: Bool) {
        if visible { hiddenDetailItems.remove(item) }
        else { hiddenDetailItems.insert(item) }
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

    func isFavorite(_ city: City) -> Bool { favorites.contains { $0.id == city.id } }

    func addFavorite(_ city: City) {
        guard favorites.count < Self.favoriteLimit, !isFavorite(city) else { return }
        favorites.append(city)
    }

    func removeFavorite(_ city: City) {
        favorites.removeAll { $0.id == city.id }
    }

    func moveFavorite(_ city: City, by offset: Int) {
        guard let index = favorites.firstIndex(where: { $0.id == city.id }),
              favorites.indices.contains(index + offset) else { return }
        favorites.swapAt(index, index + offset)
    }

    func toggleFavorite(_ city: City) {
        if isFavorite(city) { removeFavorite(city) }
        else { addFavorite(city) }
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

private struct LyricGlowClock {
    private var anchorTime = Date.now.timeIntervalSinceReferenceDate
    private var anchorUptime = ProcessInfo.processInfo.systemUptime

    func time(at uptime: TimeInterval, speed: Double) -> TimeInterval {
        anchorTime + (uptime - anchorUptime) * speed
    }

    mutating func reanchor(at uptime: TimeInterval, previousSpeed: Double) {
        // Carry the current phase forward before changing how fast it advances.
        anchorTime = time(at: uptime, speed: previousSpeed)
        anchorUptime = uptime
    }
}

@MainActor
@Observable
final class MusicModel {
    enum LyricSource: String, CaseIterable, Sendable, Codable {
        case automatic, netease, amll, qqMusic, kugou, kuwo, lrclib, megalobiz, migu, soda, deezer, appleMusic, local

        static let defaultOrder: [Self] = [.appleMusic, .amll, .netease, .kugou, .qqMusic, .soda, .deezer, .migu, .lrclib, .kuwo, .megalobiz]
        fileprivate static let previousDefaultOrder: [Self] = [.netease, .amll, .kugou, .qqMusic, .kuwo, .lrclib, .megalobiz, .migu]

        var supportsWordTiming: Bool {
            switch self {
            case .netease, .amll, .kugou, .qqMusic, .soda, .deezer, .appleMusic: true
            default: false
            }
        }

        var title: String {
            switch self {
            case .automatic: "自动选择"
            case .netease: "网易云音乐"
            case .amll: "AMLL"
            case .qqMusic: "QQ 音乐"
            case .kugou: "酷狗音乐"
            case .kuwo: "酷我音乐"
            case .lrclib: "LRCLIB"
            case .megalobiz: "Megalobiz"
            case .migu: "咪咕音乐"
            case .soda: "汽水音乐"
            case .deezer: "Deezer"
            case .appleMusic: "Apple Music"
            case .local: "本地歌词"
            }
        }
    }
    private struct LockedLyrics: Codable {
        let source: LyricSource
        let candidateID: String
        let lines: [Lyric]
        let translationSource: LyricSource?
    }
    private static let lyricLockByteLimit = 4_194_304
    static let playbackCalibrationRange = -3.0...3.0
    static let capsuleSpectrumResponseRange = 0.5...2.0
    private let defaults: UserDefaults
    private var polling: Task<Void, Never>?
    private var artworkTask: Task<Void, Never>?
    private var lyricsTask: Task<Void, Never>?
    private var importedLyrics: [Lyric] = []
    private var importedLyricsEdited = false
    private var removedCurrentLyrics: LyricLibraryEntry?
    private var removingLyrics: LyricLibraryEntry?
    private var lastSystemSnapshot: SystemMedia.Snapshot?
    private var deniedPlayers = Set<Player>()
    private var lastSpectrumPID: pid_t?
    var track: Track?
    var artwork: NSImage?
    var colors = neutralCoverColors
    var lyrics: [Lyric] = []
    var previewSpectrum: [Double]?
    var lyricsProvider: String?
    var lyricsAreLocal: Bool {
        lyricsProvider?.trimmingCharacters(in: .whitespaces) == LyricSource.local.title
    }
    private(set) var lyricSourceMessage: String?
    private(set) var rankedLyrics: [RankedLyric] = []
    private(set) var activeLyricCandidateID: String?
    private(set) var activeLyricTranslationSource: LyricSource?
    private(set) var lyricCandidateLocked = false
    private(set) var appleMusicLyricConnection = AppleMusicLyrics.Connection.disconnected

    private var availableOnlineLyricSources: [LyricSource] {
        lyricSourceOrder.filter { $0 != .appleMusic || appleMusicLyricConnection.canSearch }
    }

    var lyricSourceChoices: [LyricSource] {
        guard track != nil else { return [] }
        return [.automatic] + availableOnlineLyricSources + (importedLyrics.isEmpty ? [] : [.local])
    }
    var selectedLyricSource: LyricSource {
        guard let track, let source = lyricSourcePreference(for: track),
              source != .local || !importedLyrics.isEmpty else { return .automatic }
        return source
    }
    var lyricsCaption: String {
        if lyricsLoading && lyrics.isEmpty { return "查找歌词中" }
        if lyrics.isEmpty { return "暂无歌词" }
        if lyricsAreLocal { return LyricSource.local.title }
        return (lyricsProvider ?? "同步") + "歌词"
    }
    var lyricsLoading = false
    var automationError: String?
    var neteaseStatus: String?
    private var playbackCommand: SystemMedia.Command?
    @ObservationIgnored private var trackNavigationDeadline: ContinuousClock.Instant?
    @ObservationIgnored private var compactGlowClock = LyricGlowClock()
    @ObservationIgnored private var expandedGlowClock = LyricGlowClock()
    @ObservationIgnored private var webDurationProbe: Task<Void, Never>?
    @ObservationIgnored private var lastWebDurationProbe: (track: Track, time: Date)?
    var awaitingTrackNavigation: Bool {
        playbackCommand == .previous || playbackCommand == .next || (trackNavigationDeadline.map { .now < $0 } ?? false)
    }
    var audioError: String?
    var onTrackChange: (() -> Void)?
    var onDisplayChange: (() -> Void)?
    let spectrum = Spectrum()
    let lyricLibrary = LyricLibrary()

    var showMusic: Bool {
        didSet {
            defaults.set(showMusic, forKey: "music.enabled")
            if showMusic != oldValue { updatePlaybackObservation() }
        }
    }
    var capsuleGlow: Bool {
        didSet { defaults.set(capsuleGlow, forKey: "music.capsuleGlow"); onDisplayChange?() }
    }
    private var savedCapsuleSpectrumResponse: Double
    var capsuleSpectrumResponse: Double {
        get { savedCapsuleSpectrumResponse }
        set {
            savedCapsuleSpectrumResponse = Self.boundedSpectrumResponse(newValue)
            defaults.set(savedCapsuleSpectrumResponse, forKey: "music.capsuleSpectrumResponse")
        }
    }
    var minimumCompactCameraOffset = NotchLyricsOptions.compactCameraOffsetRange.lowerBound
    var notchLyrics: NotchLyricsOptions {
        didSet {
            let uptime = ProcessInfo.processInfo.systemUptime
            if notchLyrics.compactGlowSpeed != oldValue.compactGlowSpeed {
                compactGlowClock.reanchor(at: uptime, previousSpeed: oldValue.compactGlowSpeed)
            }
            if notchLyrics.expandedGlowSpeed != oldValue.expandedGlowSpeed {
                expandedGlowClock.reanchor(at: uptime, previousSpeed: oldValue.expandedGlowSpeed)
            }
            notchLyrics.save(to: defaults)
            if notchLyrics.enabled != oldValue.enabled { updatePlaybackObservation() }
        }
    }

    func lyricGlowTime(expanded: Bool) -> TimeInterval {
        let uptime = ProcessInfo.processInfo.systemUptime
        return expanded ? expandedGlowClock.time(at: uptime, speed: notchLyrics.expandedGlowSpeed)
            : compactGlowClock.time(at: uptime, speed: notchLyrics.compactGlowSpeed)
    }

    private var playbackDisplayEnabled: Bool { showMusic || notchLyrics.enabled }
    var spectrumEnabled: Bool {
        didSet {
            defaults.set(spectrumEnabled, forKey: "music.spectrum")
            lastSpectrumPID = nil
            updateSpectrum()
        }
    }
    var playbackCalibration: Double {
        didSet { defaults.set(playbackCalibration, forKey: "music.playbackCalibration") }
    }
    var onlineLyrics: Bool {
        didSet {
            defaults.set(onlineLyrics, forKey: "music.lyrics.online")
            loadLyrics()
        }
    }
    private static let lyricOrderKey = "music.lyrics.sourceOrder"
    private static let lyricOrderVersionKey = "music.lyrics.sourceOrder.version"
    private(set) var lyricSourceOrder: [LyricSource] {
        didSet { defaults.set(lyricSourceOrder.map(\.rawValue), forKey: Self.lyricOrderKey) }
    }
    private var lyricSourcePreferences: [String: String] {
        didSet { defaults.set(lyricSourcePreferences, forKey: "music.lyrics.sources") }
    }
    private var rejectedLyricCandidates: [String: [String]] {
        didSet { defaults.set(rejectedLyricCandidates, forKey: "music.lyrics.rejectedCandidates") }
    }
    @ObservationIgnored private var lyricSelectionRevisions: [String: UInt] = [:]
    private var offsets: [String: Double] {
        didSet { defaults.set(offsets, forKey: "music.offsets") }
    }
    var offset: Double { track.map { lyricOffset(for: lyricLibrary.id(for: $0)) } ?? 0 }

    func lyricOffset(for key: String) -> Double {
        lyricLibrary.keys(for: key).lazy.compactMap { self.offsets[$0] }.first(where: \.isFinite) ?? 0
    }

    func libraryLyricsAreAdjusted(_ entry: LyricLibraryEntry) -> Bool {
        entry.locked || entry.edited == true || entry.manuallySelectedSource == true ||
            lyricOffset(for: entry.id) != 0 ||
            lyricSourcePreference(for: entry.track).map { $0 != .automatic } == true
    }

    func libraryRematchGroup(for entry: LyricLibraryEntry) -> LyricLibraryRematchGroup {
        if entry.locked { return .manual }
        if libraryLyricsAreAdjusted(entry) { return .adjusted }
        return entry.lines.isEmpty ? .missing : .matched
    }

    private func lyricKeys(for track: Track) -> [String] {
        let keys = lyricLibrary.keys(for: lyricLibrary.id(for: track))
        return keys.contains(track.key) ? keys : keys + [track.key]
    }

    init(defaults: UserDefaults) {
        self.defaults = defaults
        showMusic = defaults.object(forKey: "music.enabled") as? Bool ?? true
        capsuleGlow = defaults.object(forKey: "music.capsuleGlow") as? Bool ?? true
        savedCapsuleSpectrumResponse = Self.boundedSpectrumResponse(
            defaults.object(forKey: "music.capsuleSpectrumResponse") as? Double ?? 1)
        notchLyrics = NotchLyricsOptions.load(from: defaults)
        spectrumEnabled = defaults.bool(forKey: "music.spectrum")
        let calibration = defaults.double(forKey: "music.playbackCalibration")
        playbackCalibration = calibration.isFinite ? min(3, max(-3, calibration)) : 0
        onlineLyrics = defaults.object(forKey: "music.lyrics.online") as? Bool ?? true
        let saved = (defaults.stringArray(forKey: Self.lyricOrderKey) ?? []).compactMap(LyricSource.init(rawValue:))
        var seen = Set<LyricSource>()
        let configured = saved.filter {
            LyricSource.defaultOrder.contains($0) && seen.insert($0).inserted
        }
        let shouldMigrate = defaults.integer(forKey: Self.lyricOrderVersionKey) == 0
        // Earlier releases could persist the default list before all providers were available.
        let followsOldDefault = shouldMigrate && LyricSource.previousDefaultOrder.starts(with: configured)
        let order = followsOldDefault ? LyricSource.defaultOrder : configured + LyricSource.defaultOrder.filter { !seen.contains($0) }
        lyricSourceOrder = order
        if shouldMigrate {
            if followsOldDefault && !configured.isEmpty { defaults.set(order.map(\.rawValue), forKey: Self.lyricOrderKey) }
            defaults.set(1, forKey: Self.lyricOrderVersionKey)
        }
        offsets = defaults.dictionary(forKey: "music.offsets") as? [String: Double] ?? [:]
        lyricSourcePreferences = defaults.dictionary(forKey: "music.lyrics.sources") as? [String: String] ?? [:]
        rejectedLyricCandidates = defaults.dictionary(forKey: "music.lyrics.rejectedCandidates") as? [String: [String]] ?? [:]
    }

    private static func boundedSpectrumResponse(_ value: Double) -> Double {
        guard value.isFinite else { return 1 }
        return min(capsuleSpectrumResponseRange.upperBound, max(capsuleSpectrumResponseRange.lowerBound, value))
    }

    func start() {
        guard previewSpectrum == nil else { return }
        guard playbackDisplayEnabled, polling == nil else { return }
        if let executable = SystemMedia.executable {
            polling = Task { [weak self] in
                guard let self else { return }
                await lyricLibrary.load()
                guard !Task.isCancelled else { return }
                await observeSystemMedia(using: executable)
            }
            return
        }
        polling = Task {
            await lyricLibrary.load()
            while !Task.isCancelled {
                await poll()
                try? await Task.sleep(for: .seconds(track == nil ? 2 : 1))
            }
        }
    }

    private func updatePlaybackObservation() {
        if playbackDisplayEnabled {
            start()
        } else if previewSpectrum == nil {
            stop()
            lastSystemSnapshot = nil
            select(nil)
        }
        onDisplayChange?()
    }

    func stop() {
        polling?.cancel()
        polling = nil
        webDurationProbe?.cancel()
    }

    private func observeSystemMedia(using executable: URL) async {
        while !Task.isCancelled {
            let stream = SystemMedia.stream(using: executable)
            let initial = await SystemMedia.snapshot(using: executable)
            guard !Task.isCancelled else { return }
            if let initial { applySystemSnapshot(initial) }
            var pending = Data()
            var hasSession = initial?.bundleIdentifier != nil
            for await bytes in stream {
                guard !Task.isCancelled else { return }
                pending.append(bytes)
                while let newline = pending.firstIndex(of: 10) {
                    let line = pending[..<newline]
                    pending.removeSubrange(...newline)
                    guard let event = try? JSONDecoder().decode(SystemMedia.Event.self, from: line),
                          event.type == "data" else { continue }
                    if let snapshot = event.payload, snapshot.bundleIdentifier != nil {
                        hasSession = true
                        applySystemSnapshot(snapshot)
                    } else if hasSession {
                        // The helper can emit an empty event before the next player takes over.
                        let current = await SystemMedia.snapshot(using: executable)
                        guard !Task.isCancelled else { return }
                        if let current {
                            applySystemSnapshot(current)
                        } else {
                            lastSystemSnapshot = nil
                            select(nil)
                        }
                        hasSession = current?.bundleIdentifier != nil
                    }
                }
                if pending.count > 1_048_576 { pending.removeAll() }
            }
            guard !Task.isCancelled else { return }
            neteaseStatus = "系统播放连接已中断，正在重连。"
            try? await Task.sleep(for: .seconds(2))
        }
    }

    private func applySystemSnapshot(_ snapshot: SystemMedia.Snapshot) {
        let now = Date.now
        var next = snapshot.track(at: now)
        if var incoming = next, AppleMusicWebPlayback.reads(incoming) {
            if let previous = track, previous.webDurationVerified, AppleMusicWebPlayback.sameTrack(previous, incoming) {
                incoming.duration = previous.duration
                incoming.webDurationVerified = true
            } else if appleMusicLyricConnection.canSearch, let previous = track,
                      AppleMusicWebPlayback.sameTrack(previous, incoming), incoming.position <= previous.duration {
                // Firefox may grow the system-reported duration throughout the same web track.
                incoming.duration = previous.duration
            }
            next = incoming
        }
        if var incoming = next, let previous = track, incoming.player == previous.player, incoming.pid == previous.pid,
           previous.systemSession, let last = lastSystemSnapshot {
            if let incomingTimestamp = snapshot.timestampEpochMicros, let lastTimestamp = last.timestampEpochMicros,
               incomingTimestamp < lastTimestamp { return }
            if incoming.key == previous.key, last.timestampEpochMicros == snapshot.timestampEpochMicros {
                if last.elapsedTimeMicros == snapshot.elapsedTimeMicros {
                    if incoming.playing != previous.playing || incoming.playbackRate != previous.playbackRate {
                        incoming.setPlaybackPosition(previous.position(at: now), at: now)
                    } else {
                        incoming.setPlaybackPosition(previous.position, at: previous.observedAt)
                    }
                } else {
                    // Some players change elapsed time on seek without supplying a new timestamp.
                    incoming.setPlaybackPosition(incoming.position, at: now)
                }
            }
            next = incoming
        }
        lastSystemSnapshot = snapshot
        neteaseStatus = next?.player == .netease ? "已通过系统播放连接网易云音乐。"
            : "通过 macOS 正在播放自动识别播放器。"
        select(next, checksAnchorOrder: false)
        checkAppleMusicWebDuration()
    }

    func checkAppleMusicWebDuration(force: Bool = false) {
        guard appleMusicLyricConnection.canSearch, webDurationProbe == nil, let current = track,
              AppleMusicWebPlayback.reads(current), !current.webDurationVerified else { return }
        if !force, let last = lastWebDurationProbe, AppleMusicWebPlayback.sameTrack(current, last.track),
           Date.now.timeIntervalSince(last.time) < 4 { return }
        lastWebDurationProbe = (current, .now)
        webDurationProbe = Task { [weak self] in
            if force { try? await Task.sleep(for: .milliseconds(120)) }
            let duration = await Task.detached(priority: .utility) { AppleMusicWebPlayback.duration(for: current) }.value
            guard let self else { return }
            self.webDurationProbe = nil
            guard !Task.isCancelled, self.appleMusicLyricConnection.canSearch, let duration,
                  var latest = self.track, AppleMusicWebPlayback.sameTrack(latest, current) else { return }
            latest.duration = duration
            latest.webDurationVerified = true
            self.rememberLyrics(for: latest, lines: [], source: nil)
            self.select(latest, checksAnchorOrder: false)
        }
    }

    @discardableResult
    func control(_ command: SystemMedia.Command, trackKey: String, expectedPID: pid_t? = nil) async -> Bool {
        guard previewSpectrum == nil, let executable = SystemMedia.executable else { return false }
        if case .seek = command {
            // A newer seek can abandon the last confirmation while its helper is still returning.
            while playbackCommand != nil {
                do { try await Task.sleep(for: .milliseconds(20)) } catch { return false }
            }
        }
        guard !Task.isCancelled, playbackCommand == nil, let current = track, current.key == trackKey,
              current.systemSession, expectedPID == nil || current.pid == expectedPID else { return false }
        playbackCommand = command
        trackNavigationDeadline = nil
        let result = await SystemMedia.send(command, to: current, using: executable)
        playbackCommand = nil
        guard let receipt = result, !Task.isCancelled else { return false }
        if (command == .previous || command == .next), track == nil || track?.key == trackKey {
            trackNavigationDeadline = .now + .seconds(3)
        }
        if case .seek(let target) = command {
            let baseline = receipt.before.track(at: receipt.sentAt).flatMap {
                $0.key == current.key && $0.pid == current.pid && !$0.estimatedPosition && $0.duration > 0 ? $0 : nil
            } ?? current
            let confirmation = SystemMedia.SeekConfirmation(before: baseline, target: target, sentAt: receipt.sentAt)
            return await confirmSeek(confirmation, initialSnapshot: receipt.before, using: executable)
        }
        return true
    }

    private func confirmSeek(_ seek: SystemMedia.SeekConfirmation, initialSnapshot: SystemMedia.Snapshot,
                             using executable: URL) async -> Bool {
        let snapshotTime = ContinuousClock.now + .milliseconds(950)
        let deadline = snapshotTime + .milliseconds(1_350)
        var checkedSnapshot = false
        while !Task.isCancelled {
            if let reported = track {
                guard reported.key == seek.before.key, reported.pid == seek.before.pid else { return false }
                if seek.alreadyAtTarget || seek.accepts(reported) { return true }
            }
            let now = ContinuousClock.now
            guard now < deadline else { break }
            if !checkedSnapshot, now >= snapshotTime {
                checkedSnapshot = true
                if let snapshot = await SystemMedia.snapshot(using: executable, timeout: 0.65), !Task.isCancelled,
                   track.map({ $0.key == seek.before.key && $0.pid == seek.before.pid }) != false,
                   snapshot.matches(seek.before) {
                    let receivedAt = Date.now
                    if var reported = snapshot.track(at: receivedAt) {
                        let last = lastSystemSnapshot ?? initialSnapshot
                        if snapshot.timestampEpochMicros == last.timestampEpochMicros,
                           snapshot.elapsedTimeMicros != last.elapsedTimeMicros {
                            reported.setPlaybackPosition(reported.position, at: receivedAt)
                        }
                        if seek.accepts(reported) { applySystemSnapshot(snapshot) }
                    }
                }
            } else {
                do { try await Task.sleep(until: min(now + .milliseconds(80), deadline), clock: .continuous) }
                catch { break }
            }
        }
        return false
    }

    func retryAutomation() {
        deniedPlayers = []
        automationError = nil
    }

    func connectNetease() {
        guard previewSpectrum == nil else { return }
        if SystemMedia.executable != nil {
            start()
            neteaseStatus = track?.player == .netease ? "已通过系统播放连接网易云音乐。"
                : "请在网易云音乐里播放一首歌，LumiBar 会通过系统播放会话识别。"
            return
        }
        let options = ["AXTrustedCheckOptionPrompt": true] as CFDictionary
        if AXIsProcessTrustedWithOptions(options) {
            neteaseStatus = "已授权。请在网易云音乐中播放一首歌。"
        } else {
            neteaseStatus = "请在「隐私与安全性 → 辅助功能」打开 LumiBar，再在网易云音乐中播放一首歌。"
        }
    }

    func adjustOffset(_ delta: Double) {
        guard let track else { return }
        adjustOffset(delta, for: lyricLibrary.id(for: track))
    }

    func adjustOffset(_ delta: Double, for key: String) {
        guard delta.isFinite else { return }
        // Keep earlier half-second offsets while allowing the island's 0.2-second steps.
        let bounded = max(-60, min(60, lyricOffset(for: key) + delta))
        let next = (bounded * 10).rounded() / 10
        var updated = offsets
        for alias in lyricLibrary.keys(for: key) { updated.removeValue(forKey: alias) }
        updated[lyricLibrary.id(for: key)] = next == 0 ? nil : next
        offsets = updated
    }

    func chooseLyricSource(_ source: LyricSource) {
        guard previewSpectrum == nil, let track, lyricSourceChoices.contains(source), !isErasingLyrics(for: track) else { return }
        guard clearLyricLocks(for: track) else {
            lyricSourceMessage = "无法取消锁定。"
            return
        }
        let key = lyricLibrary.id(for: track)
        removedCurrentLyrics = nil
        lyricLibrary.unlock(key)
        setLyricSourcePreference(source == .automatic ? nil : source, for: track)
        loadLyrics()
    }

    func unlockLibraryLyrics(for entry: LyricLibraryEntry) -> Bool {
        guard previewSpectrum == nil, entry.locked, entry.source != .local, clearLyricLocks(for: entry.track) else { return false }
        setLyricSourcePreference(nil, for: entry.track)
        lyricLibrary.unlock(entry.id)
        if track.map({ lyricLibrary.id(for: $0) }) == entry.id { loadLyrics() }
        return true
    }

    func lockLyricCandidate(_ selection: RankedLyric, for track: Track) -> Bool {
        let key = lyricLibrary.id(for: track)
        guard previewSpectrum == nil, !isErasingLyrics(for: track), selection.score != nil, selection.candidate.score(for: track) != nil,
              !lyricRejections(for: track).contains(selection.id),
              let url = Self.lyricLockURL(key) else { return false }
        let candidate = selection.candidate
        let lock = LockedLyrics(source: candidate.source, candidateID: candidate.id, lines: candidate.lines,
                                translationSource: selection.translationSource)
        do {
            let data = try JSONEncoder().encode(lock)
            guard data.count <= Self.lyricLockByteLimit else {
                lyricSourceMessage = "这份歌词过大，无法锁定。"
                return false
            }
            try data.write(to: url, options: .atomic)
        } catch {
            lyricSourceMessage = "无法保存手选歌词。"
            return false
        }
        lyricLibrary.unlock(key)
        setLyricSourcePreference(lock.source, for: track)
        rememberLyrics(for: track, lines: lock.lines, source: lock.source, id: lock.candidateID,
                       translation: lock.translationSource, locked: true)
        guard self.track.map({ lyricLibrary.id(for: $0) }) == lyricLibrary.id(for: track) else { return true }
        lyricsTask?.cancel()
        lyricsTask = nil
        lyricsLoading = false
        lyricSourceMessage = nil
        applyLyricSelection(lock.lines, source: lock.source, id: lock.candidateID,
                            translationSource: lock.translationSource, locked: true)
        return true
    }

    func lyricRejections(for track: Track) -> Set<String> {
        Set(lyricKeys(for: track).flatMap { rejectedLyricCandidates[$0] ?? [] })
    }

    func displayedLibraryLyrics(_ entry: LyricLibraryEntry) -> LyricLibraryEntry {
        guard let selected = entry.selectionID, lyricRejections(for: entry.track).contains(selected) else { return entry }
        var hidden = entry
        hidden.source = nil
        hidden.translationSource = nil
        hidden.selectionID = nil
        hidden.lines = []
        hidden.locked = false
        hidden.manuallySelectedSource = nil
        return hidden
    }

    func setLyricMatchRejected(_ rejected: Bool, id: String, for entry: LyricLibraryEntry) {
        let basis = entry.track
        guard !libraryEntryIsRemoved(basis), let prefix = id.split(separator: ":", maxSplits: 1).first,
              let source = LyricSource(rawValue: String(prefix)), source != .automatic, source != .local else { return }
        let key = lyricLibrary.id(for: basis)
        var excluded = lyricRejections(for: basis)
        if rejected { guard excluded.insert(id).inserted else { return } }
        else { guard excluded.remove(id) != nil else { return } }

        var updated = rejectedLyricCandidates
        for alias in lyricKeys(for: basis) {
            updated.removeValue(forKey: alias)
            lyricSelectionRevisions[alias, default: 0] &+= 1
        }
        updated[key] = excluded.isEmpty ? nil : excluded.sorted()
        rejectedLyricCandidates = updated

        let playing = track.map { lyricLibrary.id(for: $0) } == key
        if rejected, [entry.selectionID, playing ? activeLyricCandidateID : nil].contains(id) {
            setLyricSourcePreference(nil, for: basis)
            if playing { lyrics = []; lyricsProvider = nil }
        }
        if playing { loadLyrics() }
    }

    private func applyLyricSelection(_ lines: [Lyric], source: LyricSource, id: String? = nil,
                                    translationSource: LyricSource? = nil, locked: Bool = false) {
        // Preserve the current row identity when the user pins the lyrics already on screen.
        if id == nil || activeLyricCandidateID != id || activeLyricTranslationSource != translationSource || lyrics.isEmpty {
            lyrics = lines
        }
        lyricsProvider = (source == .netease ? "网易云" : source.title) + " "
        activeLyricCandidateID = id
        activeLyricTranslationSource = translationSource
        lyricCandidateLocked = locked
    }

    func moveLyricSource(_ source: LyricSource, by offset: Int) {
        guard let index = lyricSourceOrder.firstIndex(of: source), lyricSourceOrder.indices.contains(index + offset) else { return }
        lyricSourceOrder.swapAt(index, index + offset)
    }

    func resetLyricSourceOrder() {
        lyricSourceOrder = LyricSource.defaultOrder
    }

    func refreshAppleMusicLyricConnection(reload: Bool = false) async {
        guard previewSpectrum == nil else { return }
        let update = await AppleMusicLyrics.shared.connection()
        if appleMusicLyricConnection != update {
            appleMusicLyricConnection = update
            if reload {
                checkAppleMusicWebDuration(force: true)
                loadLyrics()
            }
        }
    }

    func importLyrics(for requestedTrack: Track? = nil, onResult: ((String) -> Void)? = nil) {
        guard previewSpectrum == nil, let track = requestedTrack ?? track else { return }
        let picker = NSOpenPanel()
        picker.title = "导入同步歌词"
        picker.allowedContentTypes = LocalLyricFile.allCases.map(\.contentType)
        picker.begin { [weak self] response in
            MainActor.assumeIsolated {
                guard response == .OK, let url = picker.url, let self,
                      !self.isErasingLyrics(for: track),
                      requestedTrack != nil || self.track.map({ self.lyricLibrary.id(for: $0) }) == self.lyricLibrary.id(for: track)
                else { return }
                let message = self.importLyricFile(at: url, for: track)
                if let onResult { onResult(message) } else { self.lyricSourceMessage = message }
            }
        }
    }

    private func importLyricFile(at url: URL, for track: Track) -> String {
        guard let format = LocalLyricFile(rawValue: url.pathExtension.lowercased()),
              let values = try? url.resourceValues(forKeys: [.fileSizeKey]),
              let size = values.fileSize, size <= LocalLyricFile.maximumBytes,
              let data = try? Data(contentsOf: url), data.count <= LocalLyricFile.maximumBytes else {
            return "无法读取这个歌词文件。"
        }
        let decoded = format.decode(data)
        guard Lyric.hasSyncedVocals(in: decoded.lines, for: track, acceptsManualText: decoded.isLumiBarExport || format == .ttml) else {
            return "这个文件没有可用的同步歌词。"
        }
        guard let folder = Self.lyricFolder() else { return "无法保存本地歌词。" }
        let key = lyricLibrary.id(for: track)
        do {
            try data.write(to: folder.appending(path: Self.lyricFilename(key, format: format)), options: .atomic)
        } catch { return "无法保存本地歌词。" }

        let previousOffset = lyricOffset(for: key)
        let usedFileTimeline = decoded.isLumiBarExport && previousOffset != 0
        if usedFileTimeline { adjustOffset(-previousOffset, for: key) }
        lyricLibrary.unlock(key)
        setLyricSourcePreference(.local, for: track)
        if self.track.map({ lyricLibrary.id(for: $0) }) == key {
            removedCurrentLyrics = nil
            importedLyrics = decoded.lines
            importedLyricsEdited = false
            loadLyrics()
        } else {
            rememberLyrics(for: track, lines: decoded.lines, source: .local, locked: true)
        }
        return usedFileTimeline ? "已导入歌词，采用文件时间轴，本曲时移已归零。" : "已导入本地歌词。"
    }

    func libraryEntryIsRemoved(_ track: Track) -> Bool {
        isErasingLyrics(for: track) || removedCurrentLyrics.map { matchesRemovedEntry($0, track: track) } == true
    }

    private func isErasingLyrics(for track: Track) -> Bool {
        removingLyrics.map { matchesRemovedEntry($0, track: track) } == true
    }

    private func matchesRemovedEntry(_ entry: LyricLibraryEntry, track: Track) -> Bool {
        let key = lyricLibrary.id(for: track)
        if key == entry.id { return true }
        if lyricLibrary.entries[key] != nil { return false }
        return entry.canGroup(with: LyricLibraryEntry(track: track))
    }

    func saveEditedLyrics(_ lines: [Lyric], for entry: LyricLibraryEntry) -> Bool {
        let track = entry.track
        let key = lyricLibrary.id(for: track)
        guard previewSpectrum == nil, !isErasingLyrics(for: track),
              Lyric.hasSyncedVocals(in: lines, for: track, acceptsManualText: true),
              let folder = Self.lyricFolder(), let data = try? JSONEncoder().encode(lines),
              data.count <= Self.lyricLockByteLimit else { return false }
        do {
            try data.write(to: folder.appending(path: Self.editedLyricFilename(key)), options: .atomic)
        } catch { return false }
        lyricLibrary.unlock(key)
        setLyricSourcePreference(.local, for: track)
        if self.track.map({ lyricLibrary.id(for: $0) }) == key {
            removedCurrentLyrics = nil
            importedLyrics = lines
            importedLyricsEdited = true
            loadLyrics()
        } else {
            rememberLyrics(for: track, lines: lines, source: .local, locked: true, edited: true)
        }
        return true
    }

    func deleteLibrarySong(_ entry: LyricLibraryEntry) async -> Bool {
        guard previewSpectrum == nil, removingLyrics == nil, let folder = Self.lyricFolder() else { return false }
        let current = track.flatMap { matchesRemovedEntry(entry, track: $0) ? $0 : nil }
        let keys = Array(Set(lyricKeys(for: entry.track) + (current.map(lyricKeys(for:)) ?? [])))
        removingLyrics = entry
        if current != nil {
            lyricsTask?.cancel()
            lyricsTask = nil
            lyricsLoading = false
            artworkTask?.cancel()
            artworkTask = nil
        }
        let files = keys.flatMap { key in
            (Self.localLyricFiles(for: key).map(\.name) + [lyricCacheKey(key) + ".selected.json"])
                .map { folder.appending(path: $0) }
        }
        do {
            try await lyricLibrary.erase(keys: keys, localFiles: files)
        } catch {
            removingLyrics = nil
            if track.map({ matchesRemovedEntry(entry, track: $0) }) == true { loadMetadata() }
            return false
        }
        var updatedOffsets = offsets
        var updatedSources = lyricSourcePreferences
        var updatedRejections = rejectedLyricCandidates
        for key in keys {
            updatedOffsets.removeValue(forKey: key)
            updatedSources.removeValue(forKey: key)
            updatedRejections.removeValue(forKey: key)
            lyricSelectionRevisions[key, default: 0] &+= 1
        }
        offsets = updatedOffsets
        lyricSourcePreferences = updatedSources
        rejectedLyricCandidates = updatedRejections
        if track.map({ matchesRemovedEntry(entry, track: $0) }) == true {
            removedCurrentLyrics = entry
            importedLyrics = []
            importedLyricsEdited = false
            lyrics = []
            lyricsProvider = nil
            rankedLyrics = []
            activeLyricCandidateID = nil
            activeLyricTranslationSource = nil
            lyricCandidateLocked = false
            lyricSourceMessage = nil
        }
        removingLyrics = nil
        return true
    }

    private func poll() async {
        var candidates: [Track] = []
        for player in Player.allCases where !deniedPlayers.contains(player) {
            guard let running = NSRunningApplication.runningApplications(withBundleIdentifier: player.rawValue).first else { continue }
            if player == .netease {
                let trusted = AXIsProcessTrusted()
                let menuBarPID = trusted ? NSRunningApplication.runningApplications(
                    withBundleIdentifier: "com.apple.MenuBarAgent").first?.processIdentifier : nil
                if let netease = await NeteaseBridge.shared.read(pid: running.processIdentifier, trusted: trusted,
                                                                 menuBarPID: menuBarPID) {
                    candidates.append(netease)
                    if !trusted {
                        neteaseStatus = "已识别当前曲目。允许「辅助功能」后可读取播放器或系统「正在播放」的进度。"
                    } else if netease.estimatedPosition {
                        neteaseStatus = "已连接网易云音乐，正在使用估算进度。"
                    } else {
                        neteaseStatus = "已连接网易云音乐。"
                    }
                } else {
                    neteaseStatus = trusted ? "已授权，等待网易云主窗口的当前曲目。"
                                           : "网易云音乐已打开。允许「辅助功能」后可读取播放进度。"
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
        guard !Task.isCancelled else { return }
        let playing = candidates.filter(\.playing)
        select(playing.first(where: { $0.player == .netease })
               ?? playing.first(where: { $0.player == track?.player }) ?? playing.first
               ?? candidates.first(where: { $0.player == track?.player }) ?? candidates.first)
    }

    private func select(_ next: Track?, checksAnchorOrder: Bool = true) {
        let previous = track
        if checksAnchorOrder, let previous, let next, previous.key == next.key, previous.pid == next.pid,
           previous.observedAt > next.observedAt { return }
        track = next
        if let next, let removedCurrentLyrics, !matchesRemovedEntry(removedCurrentLyrics, track: next) {
            self.removedCurrentLyrics = nil
        }
        let trackChanged = previous?.key != next?.key
        if trackChanged {
            if next != nil { trackNavigationDeadline = nil }
            artwork = nil
            colors = neutralCoverColors
            lyrics = []
            lyricsProvider = nil
            loadMetadata()
        }
        if previous?.playing != next?.playing || previous?.pid != next?.pid { updateSpectrum() }
        if trackChanged || previous?.playing != next?.playing || previous?.title != next?.title
            || previous?.artist != next?.artist || previous?.album != next?.album {
            onTrackChange?()
        }
    }

    private func loadMetadata() {
        guard previewSpectrum == nil else { return }
        artworkTask?.cancel()
        artworkTask = nil
        (importedLyrics, importedLyricsEdited) = track.map(readLocalLyrics(for:)) ?? ([], false)
        loadLyrics()
        guard let track, !libraryEntryIsRemoved(track) else { return }
        artworkTask = Task { [weak self] in
            guard let self, let data = await artworkData(for: track), !Task.isCancelled,
                  self.track?.key == track.key, !libraryEntryIsRemoved(track), let image = NSImage(data: data) else { return }
            artwork = image
            colors = coverColors(image)
            await lyricLibrary.storeCover(data, for: track.key)
        }
    }

    private func loadLyrics() {
        guard previewSpectrum == nil else { return }
        lyricsTask?.cancel()
        lyricsTask = nil
        lyricsLoading = false
        lyricSourceMessage = nil
        rankedLyrics = []
        activeLyricCandidateID = nil
        activeLyricTranslationSource = nil
        lyricCandidateLocked = false
        guard let track, !libraryEntryIsRemoved(track) else { return }
        let source = selectedLyricSource
        let rejectedIDs = lyricRejections(for: track)
        let usesLocalByDefault = source == .automatic && lyricSourcePreference(for: track) != .automatic
        if (usesLocalByDefault || source == .local) &&
            Lyric.hasSyncedVocals(in: importedLyrics, for: track, acceptsManualText: true) {
            applyLyricSelection(importedLyrics, source: .local)
            rememberLyrics(for: track, lines: importedLyrics, source: .local, locked: true, edited: importedLyricsEdited)
            return
        }
        if source == .local {
            lyrics = []
            lyricsProvider = nil
            lyricSourceMessage = "本地文件没有可用的同步歌词。"
            return
        }
        if source != .automatic,
           let saved = lyricKeys(for: track).lazy.compactMap({ Self.readLyricLock(track, key: $0) })
               .first(where: { $0.source == source && !rejectedIDs.contains($0.candidateID) }) {
            applyLyricSelection(saved.lines, source: source, id: saved.candidateID,
                                translationSource: saved.translationSource, locked: true)
            rememberLyrics(for: track, lines: saved.lines, source: source, id: saved.candidateID,
                           translation: saved.translationSource, locked: true)
            return
        }
        guard onlineLyrics else {
            lyrics = []
            lyricsProvider = nil
            if source != .automatic { lyricSourceMessage = "在线查找同步歌词已关闭。" }
            return
        }
        let sourceOrder = availableOnlineLyricSources
        let usesAutomaticSearch = source == .automatic || (source == .appleMusic && !appleMusicLyricConnection.canSearch)
        let sources = usesAutomaticSearch ? sourceOrder : [source]
        lyricsLoading = true
        lyricsTask = Task { [weak self] in
            let ranked = await LyricLookup.ranked(for: track, sources: sources, sourceOrder: sourceOrder, rejecting: rejectedIDs) { [weak self] result in
                guard let self, !Task.isCancelled, self.track?.key == track.key, self.lyrics.isEmpty else { return }
                let candidate = result.candidate
                self.applyLyricSelection(candidate.lines, source: candidate.source, id: candidate.id)
                self.rememberLyrics(for: track, lines: candidate.lines, source: candidate.source, id: candidate.id)
            }
            guard let self, !Task.isCancelled, self.track?.key == track.key else { return }
            self.rankedLyrics = ranked
            self.lyricsLoading = false
            if let winner = ranked.first(where: { $0.score != nil }) {
                let candidate = winner.candidate
                self.applyLyricSelection(candidate.lines, source: candidate.source, id: candidate.id,
                                         translationSource: winner.translationSource)
                self.rememberLyrics(for: track, lines: candidate.lines, source: candidate.source, id: candidate.id,
                                    translation: winner.translationSource, decisions: ranked)
            } else {
                self.lyrics = []
                self.lyricsProvider = nil
                if !usesAutomaticSearch {
                    self.lyricSourceMessage = "未找到\(source.title)的同步歌词。"
                }
                self.rememberLyrics(for: track, lines: [], source: nil, decisions: ranked)
            }
            if sources.contains(.appleMusic) { await self.refreshAppleMusicLyricConnection() }
        }
    }

    func searchLibraryLyrics(for entry: LyricLibraryEntry, rematch: Bool,
                             batchScope: Set<LyricLibraryRematchGroup>? = nil) async -> (candidates: [RankedLyric], applied: Bool) {
        guard onlineLyrics, previewSpectrum == nil else { return ([], false) }
        let track = entry.track
        guard !libraryEntryIsRemoved(track) else { return ([], false) }
        if let batchScope, !batchScope.contains(libraryRematchGroup(for: lyricLibrary.entries[entry.id] ?? entry)) {
            return ([], false)
        }
        let order = availableOnlineLyricSources
        let initialRevision = lyricSelectionRevisions[entry.id, default: 0]
        let ranked = await LyricLookup.ranked(for: track, sources: order, sourceOrder: order, rejecting: lyricRejections(for: track))
        if order.contains(.appleMusic) { await refreshAppleMusicLyricConnection() }
        guard !Task.isCancelled, !libraryEntryIsRemoved(track),
              initialRevision == lyricSelectionRevisions[entry.id, default: 0] else { return ([], false) }
        let current = displayedLibraryLyrics(lyricLibrary.entries[entry.id] ?? entry)
        let canReplace = batchScope.map { $0.contains(libraryRematchGroup(for: current)) } ?? !current.locked
        guard rematch, onlineLyrics, canReplace,
              lyricLibrary.id(for: track) == entry.id,
              let winner = ranked.first(where: { $0.score != nil }) else {
            if batchScope == nil { lyricLibrary.recordDecisions(ranked, for: entry) }
            return (ranked, false)
        }
        let candidate = winner.candidate
        // An explicit automatic preference keeps a replaced local file available without reselecting it on the next play.
        let preference = lyricSourcePreference(for: track)
        let replacesLocal = current.source == .local || preference == .local || preference == .automatic
        setLyricSourcePreference(replacesLocal ? .automatic : nil, for: track)
        rememberLyrics(for: track, lines: candidate.lines, source: candidate.source, id: candidate.id,
                       translation: winner.translationSource, decisions: ranked, replacingGroup: batchScope != nil)
        if self.track.map({ lyricLibrary.id(for: $0) }) == entry.id {
            lyricsTask?.cancel()
            lyricsTask = nil
            lyricsLoading = false
            lyricSourceMessage = nil
            rankedLyrics = ranked
            // A fresh match may have different timing even when the recording ID is unchanged.
            activeLyricCandidateID = nil
            applyLyricSelection(candidate.lines, source: candidate.source, id: candidate.id,
                                translationSource: winner.translationSource)
        }
        return (ranked, true)
    }

    private func lyricSourcePreference(for track: Track) -> LyricSource? {
        lyricKeys(for: track).lazy.compactMap { self.lyricSourcePreferences[$0] }
            .compactMap(LyricSource.init(rawValue:)).first
    }

    private func setLyricSourcePreference(_ source: LyricSource?, for track: Track) {
        let key = lyricLibrary.id(for: track)
        var updated = lyricSourcePreferences
        for alias in lyricKeys(for: track) {
            updated.removeValue(forKey: alias)
            lyricSelectionRevisions[alias, default: 0] &+= 1
        }
        updated[key] = source?.rawValue
        lyricSourcePreferences = updated
    }

    private func rememberLyrics(for track: Track, lines: [Lyric], source: LyricSource?, id: String? = nil,
                                translation: LyricSource? = nil, locked: Bool = false, decisions: [RankedLyric]? = nil,
                                edited: Bool = false, replacingGroup: Bool = false) {
        guard !libraryEntryIsRemoved(track) else { return }
        let chosen = lyricKeys(for: track).lazy.compactMap { self.lyricSourcePreferences[$0] }.first
        lyricLibrary.record(track: track, lines: lines, source: source, translation: translation,
                            candidateID: id, locked: locked, decisions: decisions,
                            manuallySelectedSource: source.map { $0 != .automatic && $0.rawValue == chosen } == true,
                            edited: edited, replacingGroup: replacingGroup)
    }

    private func clearLyricLocks(for track: Track) -> Bool {
        for key in lyricKeys(for: track) {
            guard let path = Self.lyricLockURL(key) else { return false }
            do {
                if FileManager.default.fileExists(atPath: path.path) { try FileManager.default.removeItem(at: path) }
            } catch { return false }
        }
        return true
    }

    private static func lyricLockURL(_ key: String) -> URL? {
        lyricFolder()?.appending(path: lyricCacheKey(key) + ".selected.json")
    }

    private static func readLyricLock(_ track: Track, key: String) -> LockedLyrics? {
        guard let url = lyricLockURL(key),
              let size = try? url.resourceValues(forKeys: [.fileSizeKey]).fileSize, size <= Self.lyricLockByteLimit,
              let data = try? Data(contentsOf: url),
              let saved = try? JSONDecoder().decode(LockedLyrics.self, from: data),
              Lyric.hasSyncedVocals(in: saved.lines, for: track) else { return nil }
        return saved
    }

    private func artworkData(for track: Track) async -> Data? {
        if track.systemSession, let executable = SystemMedia.executable {
            guard let snapshot = await SystemMedia.snapshot(using: executable, artwork: true),
                  snapshot.matches(track), let artwork = snapshot.artworkData else { return nil }
            return Data(base64Encoded: artwork)
        }
        return await PlayerBridge.artwork(for: track)
    }

    private static func lyricFolder() -> URL? {
        guard let root = try? FileManager.default.url(for: .applicationSupportDirectory, in: .userDomainMask,
                                                     appropriateFor: nil, create: true) else { return nil }
        let folder = root.appending(path: "LumiBar/Lyrics")
        do { try FileManager.default.createDirectory(at: folder, withIntermediateDirectories: true) }
        catch { return nil }
        return folder
    }

    private static func lyricFilename(_ key: String, format: LocalLyricFile) -> String {
        lyricCacheKey(key) + "." + format.rawValue
    }

    private static func editedLyricFilename(_ key: String) -> String {
        lyricCacheKey(key) + ".edited.json"
    }

    private static func localLyricFiles(for key: String) -> [(name: String, format: LocalLyricFile?)] {
        [(editedLyricFilename(key), nil)] + LocalLyricFile.allCases.map { (lyricFilename(key, format: $0), $0) }
    }

    private func readLocalLyrics(for track: Track) -> ([Lyric], Bool) {
        guard let folder = Self.lyricFolder() else { return ([], false) }
        let files = lyricKeys(for: track).flatMap(Self.localLyricFiles).compactMap { name, format -> (URL, LocalLyricFile?, Date)? in
            let url = folder.appending(path: name)
            guard let info = try? url.resourceValues(forKeys: [.contentModificationDateKey, .fileSizeKey]),
                  let size = info.fileSize, size <= (format == nil ? Self.lyricLockByteLimit : LocalLyricFile.maximumBytes) else { return nil }
            return (url, format, info.contentModificationDate ?? .distantPast)
        }.sorted { $0.2 > $1.2 }
        for (file, format, _) in files {
            guard let data = try? Data(contentsOf: file) else { continue }
            let decoded = format?.decode(data)
            let lines = decoded?.lines ?? (try? JSONDecoder().decode([Lyric].self, from: data)) ?? []
            let edited = format == nil
            let acceptsManualText = edited || format == .ttml || decoded?.isLumiBarExport == true
            if Lyric.hasSyncedVocals(in: lines, for: track, acceptsManualText: acceptsManualText) { return (lines, edited) }
        }
        return ([], false)
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
