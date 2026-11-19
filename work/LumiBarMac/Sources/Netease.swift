import ApplicationServices
import Foundation
import OSLog
import SQLite3

actor NeteaseBridge {
    static let shared = NeteaseBridge()

    struct CatalogTrack: Decodable {
        struct Artist: Decodable { let name: String }
        struct Album: Decodable { let name: String?; let picUrl: String? }
        let id: String?
        let name: String
        let duration: Int
        let artists: [Artist]
        let album: Album?

        enum CodingKeys: String, CodingKey { case id, name, duration, artists, album }

        init(from decoder: Decoder) throws {
            let values = try decoder.container(keyedBy: CodingKeys.self)
            id = (try? values.decode(String.self, forKey: .id))
                ?? (try? values.decode(Int64.self, forKey: .id)).map(String.init)
            name = try values.decode(String.self, forKey: .name)
            duration = try values.decode(Int.self, forKey: .duration)
            artists = try values.decode([Artist].self, forKey: .artists)
            album = try values.decodeIfPresent(Album.self, forKey: .album)
        }
    }

    private struct History {
        let track: CatalogTrack
        let started: Date?
    }

    private struct Progress {
        let position: Double
        let duration: Double
        let slider: AXUIElement?
    }

    private struct PlayerView {
        let progress: Progress?
        let controls: [AXUIElement]
        let track: CatalogTrack?
    }

    private struct QueueFile: Decodable {
        struct Entry: Decodable {
            let track: CatalogTrack?
            enum CodingKeys: String, CodingKey { case track }
            init(from decoder: Decoder) throws {
                let container = try decoder.container(keyedBy: CodingKeys.self)
                track = try? container.decode(CatalogTrack.self, forKey: .track)
            }
        }
        let list: [Entry]
    }

    private static let playNames = ["播放", "开始播放", "Play", "Resume"]
    private static let pauseNames = ["暂停", "Pause"]
    private static let previousNames = ["上一首", "上一曲", "上一首歌曲", "上一个", "Previous", "Previous Track", "Previous Song"]
    private static let toggleNames = ["播放/暂停", "播放 / 暂停", "Play/Pause", "Play / Pause"]
    private static let nextNames = ["下一首", "下一曲", "下一首歌曲", "下一个", "Next", "Next Track", "Next Song"]
    private var lastTrack: Track?
    private var lastProgress: Progress?
    private let logger = Logger(subsystem: "com.lumibar.macos", category: "netease")
    private var lastDiagnostic: String?
    private var cachedPlayer: (pid_t, AXUIElement)?
    private var cachedBar: AXUIElement?
    private var accessibilityActivated = false
    private var queueCache: (url: URL, date: Date, tracks: [CatalogTrack])?

    func read(pid: pid_t, trusted: Bool) -> Track? {
        let now = Date.now
        let app = application(pid)
        if trusted && !accessibilityActivated {
            // Ask the application to expose its web-based playback bar to assistive clients.
            if AXUIElementSetAttributeValue(app, "AXManualAccessibility" as CFString, kCFBooleanTrue) != .success {
                AXUIElementSetAttributeValue(app, "AXEnhancedUserInterface" as CFString, kCFBooleanTrue)
            }
            accessibilityActivated = true
        }

        let history = readHistory()
        let recent = history.map(\.track)
        let catalog = trusted ? recent + queueTracks() : []
        let items = trusted ? controls(app) : []
        let view = trusted ? playerView(app, catalog: catalog) : nil
        let available = items + (view?.controls ?? [])
        let pause = item(named: Self.pauseNames, in: available)
        let play = item(named: Self.playNames, in: available)
        let state = pause.flatMap { enabled($0) ? true : nil } ?? play.map { _ in false }
        let canControl = pause != nil || play != nil || item(named: Self.toggleNames, in: available) != nil
        let playback = AudioPlayback.applicationObjects(pid: pid, bundleID: Player.netease.rawValue)
        let hasAudio = AudioPlayback.hasOutput(playback)
        let progress = view?.progress
        let inferred = playStateFromProgress(progress, previous: lastTrack, pid: pid)
        let playing = state ?? inferred ?? hasAudio
        lastProgress = progress

        let track: Track?
        if let current = view?.track {
            track = makeTrack(current, progress: progress, history: history, now: now, pid: pid,
                              playing: playing, canControl: canControl)
        } else if let newest = history.first, let started = newest.started,
                  let confirmed = estimate(newest.track, started: started, now: now, pid: pid,
                                           playing: playing, hasAudio: hasAudio, canControl: canControl) {
            track = confirmed
        } else {
            track = nil
        }

        let diagnostic = "authorized=\(trusted) menu=\(canControl) bar=\(view != nil) liveTitle=\(view?.track != nil) elapsed=\(progress != nil) audio=\(hasAudio) track=\(track != nil)"
        if lastDiagnostic != diagnostic {
            logger.notice("\(diagnostic, privacy: .public)")
            lastDiagnostic = diagnostic
        }
        if let track, track.key != lastTrack?.key {
            logger.notice("NetEase track changed")
        }
        lastTrack = track
        return track
    }

    private func makeTrack(_ catalog: CatalogTrack, progress: Progress?, history: [History], now: Date, pid: pid_t,
                           playing: Bool, canControl: Bool) -> Track {
        let duration = progress?.duration ?? Double(catalog.duration) / 1000
        let previous = lastTrack.flatMap { $0.catalogID != nil && $0.catalogID == catalog.id ? $0 : nil }
        let started = history.first(where: { $0.track.id != nil && $0.track.id == catalog.id })?.started
        let age = started.map { now.timeIntervalSince($0) }
        let recentPosition = age.flatMap { (0...max(0, duration)).contains($0) ? $0 : nil }
        let position = progress?.position ?? previous?.position(at: now) ?? recentPosition ?? 0
        return Track(player: .netease, title: catalog.name, artist: artist(catalog) ?? "",
                     album: catalog.album?.name ?? "", duration: duration, position: position,
                     playing: playing, artworkURL: catalog.album?.picUrl.flatMap(Self.artworkURL), pid: pid,
                     observedAt: now, catalogID: catalog.id, canSeek: progress?.slider.map(isWritable) ?? false,
                     canControl: canControl, estimatedPosition: progress == nil)
    }

    private func estimate(_ catalog: CatalogTrack, started: Date, now: Date, pid: pid_t, playing: Bool,
                          hasAudio: Bool, canControl: Bool) -> Track? {
        let duration = Double(catalog.duration) / 1000
        let age = now.timeIntervalSince(started)
        guard duration > 0, (-5...duration).contains(age) else { return nil }
        // A recent history entry on its own can be from a closed or paused player.
        let previous = lastTrack.flatMap { $0.catalogID == catalog.id ? $0 : nil }
        guard playing || hasAudio || previous != nil else { return nil }
        let seconds: Double
        if let previous {
            seconds = previous.position(at: now)
        } else {
            seconds = min(duration, max(0, age))
        }
        return Track(player: .netease, title: catalog.name, artist: artist(catalog) ?? "",
                     album: catalog.album?.name ?? "", duration: duration, position: seconds,
                     playing: playing, artworkURL: catalog.album?.picUrl.flatMap(Self.artworkURL),
                     pid: pid, observedAt: now, catalogID: catalog.id, canSeek: false,
                     canControl: canControl, estimatedPosition: true)
    }

    private func artist(_ catalog: CatalogTrack?) -> String? {
        catalog?.artists.map(\.name).filter { !$0.isEmpty }.joined(separator: " / ")
    }

    func control(_ command: PlaybackCommand, pid: pid_t) -> Bool {
        guard AXIsProcessTrusted() else { return false }
        let app = application(pid)
        let live = cachedBar.map { scan($0, catalog: []) }
        if case .seek(let seconds) = command {
            guard let progress = live?.progress, let slider = progress.slider,
                  isWritable(slider), seconds.isFinite else { return false }
            return AXUIElementSetAttributeValue(slider, kAXValueAttribute as CFString,
                                               NSNumber(value: min(progress.duration, max(0, seconds)))) == .success
        }
        let candidates = controls(app) + (live?.controls ?? [])
        let names: [String] = switch command {
        case .toggle: Self.pauseNames + Self.playNames + Self.toggleNames
        case .previous: Self.previousNames
        case .next: Self.nextNames
        case .seek: []
        }
        for name in names {
            if let control = item(named: [name], in: candidates), enabled(control),
               AXUIElementPerformAction(control, kAXPressAction as CFString) == .success {
                logger.notice("NetEase playback command accepted by the player")
                return true
            }
        }
        logger.notice("NetEase playback command has no matching accessible control")
        return false
    }

    private func application(_ pid: pid_t) -> AXUIElement {
        if let (cachedPID, application) = cachedPlayer, cachedPID == pid { return application }
        let application = AXUIElementCreateApplication(pid)
        AXUIElementSetMessagingTimeout(application, 0.35)
        lastTrack = nil
        lastProgress = nil
        cachedBar = nil
        accessibilityActivated = false
        cachedPlayer = (pid, application)
        return application
    }

    private func controls(_ app: AXUIElement) -> [AXUIElement] {
        guard let bar = element(app, kAXMenuBarAttribute) else { return [] }
        let titles = ["控制", "播放控制", "播放器", "Controls", "Playback"]
        let menus = children(bar)
        if let menuItem = item(named: titles, in: menus), let menu = children(menuItem).first {
            let items = children(menu)
            if !items.isEmpty { return items }
        }
        for menuItem in menus {
            guard let menu = children(menuItem).first else { continue }
            let items = children(menu)
            if item(named: Self.nextNames + Self.previousNames, in: items) != nil { return items }
        }
        return []
    }

    private func item(named names: [String], in items: [AXUIElement]) -> AXUIElement? {
        let normalized = Set(names.map(Self.normalizedText))
        return items.first { control in
            let labels = [kAXTitleAttribute, kAXDescriptionAttribute, kAXHelpAttribute].compactMap {
                attribute(control, $0) as? String
            }
            return labels.contains { normalized.contains(Self.normalizedText($0)) }
        }
    }

    private func playerView(_ app: AXUIElement, catalog: [CatalogTrack]) -> PlayerView? {
        if let cachedBar {
            let current = scan(cachedBar, catalog: catalog)
            if current.track != nil || current.progress != nil { return current }
        }
        let windows = attribute(app, kAXWindowsAttribute) as? [AXUIElement] ?? []
        let primary = element(app, kAXMainWindowAttribute) ?? element(app, kAXFocusedWindowAttribute)
        let ordered = primary.map { main in [main] + windows.filter { !CFEqual($0, main) } } ?? windows
        for window in ordered.prefix(2) {
            let frame = bounds(window)
            let hits = frame.map { footerGroups(app, frame: $0) } ?? []
            var roots = hits
            for group in nodes(window, depth: 8, budget: 155) {
                guard let rect = bounds(group), (30...133).contains(rect.height), rect.width >= 235 else { continue }
                if let frame, abs(rect.maxY - frame.maxY) > 132 { continue }
                if !roots.contains(where: { CFEqual($0, group) }) { roots.append(group) }
            }
            if roots.isEmpty, let frame, frame.height <= 145 { roots.append(window) }
            var progressCandidate: (AXUIElement, PlayerView)?
            for group in roots.prefix(9) {
                let view = scan(group, catalog: catalog)
                if view.track != nil {
                    cachedBar = group
                    return view
                }
                if view.progress != nil && progressCandidate == nil { progressCandidate = (group, view) }
            }
            if let (group, view) = progressCandidate {
                cachedBar = group
                return view
            }
        }
        cachedBar = nil
        return nil
    }

    private func footerGroups(_ app: AXUIElement, frame: CGRect) -> [AXUIElement] {
        guard frame.width >= 235, frame.height >= 32 else { return [] }
        var picks: [AXUIElement] = []
        for x in [frame.minX + 54, frame.midX, frame.maxX - 55] {
            var hit: AXUIElement?
            guard AXUIElementCopyElementAtPosition(app, Float(x), Float(frame.maxY - 27), &hit) == .success,
                  var node = hit else { continue }
            for _ in 0..<7 {
                if let rect = bounds(node), rect.width >= 235, (30...133).contains(rect.height),
                   rect.maxY >= frame.maxY - 125, !picks.contains(where: { CFEqual($0, node) }) {
                    picks.append(node)
                }
                guard let parent = element(node, kAXParentAttribute) else { break }
                node = parent
            }
        }
        return picks
    }

    private func scan(_ root: AXUIElement, catalog: [CatalogTrack]) -> PlayerView {
        var progress: Progress?
        var slider: Progress?
        var labels: [String] = []
        var clocks: [Double] = []
        var buttons: [AXUIElement] = []
        let deadline = ContinuousClock.now + .milliseconds(220)
        for node in nodes(root, depth: 8, budget: 110) {
            if ContinuousClock.now > deadline { break }
            let role = attribute(node, kAXRoleAttribute) as? String
            if role == kAXSliderRole {
                let position = (attribute(node, kAXValueAttribute) as? NSNumber)?.doubleValue
                let maximum = (attribute(node, kAXMaxValueAttribute) as? NSNumber)?.doubleValue
                if let position, let maximum, maximum.isFinite, (15...86400).contains(maximum),
                   position.isFinite, (0...maximum).contains(position) {
                    slider = .init(position: position, duration: maximum, slider: node)
                }
                continue
            }
            let accessible = [kAXTitleAttribute, kAXValueAttribute, kAXDescriptionAttribute]
            if role == kAXButtonRole || role == kAXCheckBoxRole { buttons.append(node) }
            if role == kAXStaticTextRole || role == kAXTextFieldRole || role == "AXLink" || role == kAXButtonRole {
                for key in accessible {
                    guard let value = attribute(node, key) as? String else { continue }
                    let trimmed = value.trimmingCharacters(in: .whitespacesAndNewlines)
                    if let (position, length) = Self.parseTimePair(trimmed) {
                        progress = .init(position: position, duration: length, slider: nil)
                    } else if trimmed.contains(":"), let seconds = Self.parseClock(trimmed[...]) {
                        if clocks.last != seconds { clocks.append(seconds) }
                    } else if !trimmed.isEmpty, trimmed.count <= 160, !labels.contains(trimmed) {
                        labels.append(trimmed)
                    }
                }
            }
        }
        if progress == nil, clocks.count == 2, clocks[1] > 0, clocks[0] <= clocks[1] {
            progress = .init(position: clocks[0], duration: clocks[1], slider: nil)
        }
        if let old = progress, let slider, abs(old.duration - slider.duration) < 2 { progress = slider }
        let candidate = Self.matchTrack(labels: labels, duration: progress?.duration, catalog: catalog)
        if progress == nil, let slider, let candidate,
           abs(slider.duration - Double(candidate.duration) / 1000) < 2 {
            progress = slider
        }
        return .init(progress: progress, controls: buttons, track: candidate)
    }

    private static func normalizedText(_ value: String) -> String {
        value.folding(options: [.caseInsensitive, .diacriticInsensitive, .widthInsensitive], locale: .current)
            .split(whereSeparator: \.isWhitespace).joined(separator: " ")
    }

    static func matchTrack(labels: [String], duration: Double?, catalog: [CatalogTrack]) -> CatalogTrack? {
        let normalized = Set(labels.map(normalizedText))
        var matches = catalog.filter { song in
            let name = normalizedText(song.name)
            return !name.isEmpty && normalized.contains(name)
        }
        guard !matches.isEmpty else { return nil }
        let withArtist = matches.filter { song in
            song.artists.contains { person in normalized.contains(normalizedText(person.name)) }
        }
        if !withArtist.isEmpty { matches = withArtist }
        if let duration {
            let sameLength = matches.filter { abs(Double($0.duration) / 1000 - duration) < 2 }
            if !sameLength.isEmpty { matches = sameLength }
        }
        let identifiers = Set(matches.compactMap(\.id))
        return identifiers.count <= 1 ? matches.first : nil
    }

    static func parseTimePair(_ text: String) -> (Double, Double)? {
        let parts = text.split(separator: "/", omittingEmptySubsequences: false)
        guard parts.count == 2, let position = parseClock(parts[0]), let duration = parseClock(parts[1]),
              duration > 0, duration <= 86400, (0...duration).contains(position) else { return nil }
        return (position, duration)
    }

    private static func parseClock(_ value: Substring) -> Double? {
        let chunks = value.trimmingCharacters(in: .whitespaces).split(separator: ":", omittingEmptySubsequences: false)
        let numbers = chunks.compactMap { Int($0) }
        guard chunks.count == numbers.count, (2...3).contains(numbers.count),
              numbers.allSatisfy({ (0...1440).contains($0) }), numbers.dropFirst().allSatisfy({ $0 < 60 })
        else { return nil }
        return Double(numbers.reduce(0) { $0 * 60 + $1 })
    }

    private func playStateFromProgress(_ progress: Progress?, previous: Track?, pid: pid_t) -> Bool? {
        guard let progress else { return nil }
        if let lastProgress, abs(lastProgress.duration - progress.duration) < 2 {
            if progress.position > lastProgress.position + 0.25 { return true }
            if progress.position == lastProgress.position { return false }
        }
        guard let previous, previous.pid == pid, abs(previous.duration - progress.duration) < 2 else { return nil }
        return previous.playing
    }

    private var storageFolders: [URL] {
        let home = FileManager.default.homeDirectoryForCurrentUser
        return ["Library/Application Support/com.netease.163music/Documents/storage",
                "Library/Containers/com.netease.163music/Data/Documents/storage"].map { home.appending(path: $0) }
    }

    private func queueTracks() -> [CatalogTrack] {
        for root in storageFolders {
            let queue = root.appending(path: "file_storage/webdata/file/playingList")
            guard let values = try? queue.resourceValues(forKeys: [.contentModificationDateKey, .fileSizeKey]),
                  let date = values.contentModificationDate, let size = values.fileSize, (1...8_000_000).contains(size)
            else { continue }
            if let cache = queueCache, cache.url == queue, cache.date == date { return cache.tracks }
            guard let bytes = try? Data(contentsOf: queue), bytes.count <= 8_000_000 else { continue }
            let archived = (try? PropertyListSerialization.propertyList(from: bytes, options: [], format: nil)) as? [String: Any]
            let strings = (archived?["$objects"] as? [Any])?.compactMap { $0 as? String }
            let payload = strings?.first(where: { $0.hasPrefix(#"{"list":"#) }).map { Data($0.utf8) } ?? bytes
            if let result = try? JSONDecoder().decode(QueueFile.self, from: payload) {
                let tracks = Array(result.list.prefix(2000)).compactMap(\.track)
                queueCache = (queue, date, tracks)
                return tracks
            }
        }
        return []
    }

    private func readHistory() -> [History] {
        for root in storageFolders {
            let db = root.appending(path: "sqlite_storage.sqlite3")
            guard FileManager.default.isReadableFile(atPath: db.path) else { continue }
            let rows = Self.history(db)
            if !rows.isEmpty { return rows }
        }
        return []
    }

    static func recentTracks(_ url: URL) -> [CatalogTrack] { history(url).map(\.track) }

    private static func history(_ url: URL) -> [History] {
        var handle: OpaquePointer?
        let status = sqlite3_open_v2(url.path, &handle, SQLITE_OPEN_READONLY | SQLITE_OPEN_NOMUTEX, nil)
        defer { if let handle { sqlite3_close(handle) } }
        guard status == SQLITE_OK, let handle else { return [] }
        sqlite3_busy_timeout(handle, 100)
        var statement: OpaquePointer?
        let sql = "SELECT jsonStr, playtime FROM historyTracks ORDER BY playtime DESC LIMIT 8"
        guard sqlite3_prepare_v2(handle, sql, -1, &statement, nil) == SQLITE_OK, let statement else { return [] }
        defer { sqlite3_finalize(statement) }
        var result: [History] = []
        while sqlite3_step(statement) == SQLITE_ROW {
            let count = Int(sqlite3_column_bytes(statement, 0))
            guard (1...500_000).contains(count), let bytes = sqlite3_column_blob(statement, 0),
                  let row = try? JSONDecoder().decode(CatalogTrack.self, from: Data(bytes: bytes, count: count)),
                  !row.name.isEmpty, row.duration > 0 else { continue }
            let timestamp = Double(sqlite3_column_int64(statement, 1))
            let seconds = timestamp > 10_000_000_000 ? timestamp / 1000 : timestamp
            let started = seconds > 0 ? Date(timeIntervalSince1970: seconds) : nil
            result.append(.init(track: row, started: started))
        }
        return result
    }

    private static func artworkURL(_ text: String) -> URL? {
        guard var parts = URLComponents(string: text),
              let host = parts.host?.lowercased(), host == "music.126.net" || host.hasSuffix(".music.126.net"),
              parts.user == nil, parts.password == nil else { return nil }
        parts.scheme = "https"
        return parts.url
    }

    static func syncLyrics(trackID: String) async -> [Lyric] {
        guard Int64(trackID).map({ $0 > 0 }) == true else { return [] }
        var endpoint = URLComponents(string: "https://music.163.com/api/song/lyric")!
        endpoint.queryItems = [.init(name: "id", value: trackID), .init(name: "lv", value: "-1")]
        struct SongLyrics: Decodable {
            struct LRC: Decodable { let lyric: String? }
            let lrc: LRC?
        }
        var request = URLRequest(url: endpoint.url!, timeoutInterval: 6)
        request.setValue("https://music.163.com/", forHTTPHeaderField: "Referer")
        guard let (data, response) = try? await URLSession.shared.data(for: request),
              (response as? HTTPURLResponse)?.statusCode == 200, data.count <= 1_048_576,
              let lyric = (try? JSONDecoder().decode(SongLyrics.self, from: data))?.lrc?.lyric else { return [] }
        return Lyric.parse(lyric)
    }

    private func attribute(_ node: AXUIElement, _ key: String) -> CFTypeRef? {
        var value: CFTypeRef?
        guard AXUIElementCopyAttributeValue(node, key as CFString, &value) == .success else { return nil }
        return value
    }

    private func element(_ node: AXUIElement, _ key: String) -> AXUIElement? {
        guard let value = attribute(node, key), CFGetTypeID(value) == AXUIElementGetTypeID() else { return nil }
        return (value as! AXUIElement)
    }

    private func bounds(_ node: AXUIElement) -> CGRect? {
        guard let dimensions = attribute(node, kAXSizeAttribute), CFGetTypeID(dimensions) == AXValueGetTypeID(),
              let coordinates = attribute(node, kAXPositionAttribute), CFGetTypeID(coordinates) == AXValueGetTypeID()
        else { return nil }
        var size = CGSize.zero
        var point = CGPoint.zero
        guard AXValueGetValue(dimensions as! AXValue, .cgSize, &size),
              AXValueGetValue(coordinates as! AXValue, .cgPoint, &point)
        else { return nil }
        return CGRect(origin: point, size: size)
    }

    private func enabled(_ node: AXUIElement) -> Bool {
        (attribute(node, kAXEnabledAttribute) as? NSNumber)?.boolValue ?? true
    }

    private func isWritable(_ node: AXUIElement) -> Bool {
        var writable = DarwinBoolean(false)
        return AXUIElementIsAttributeSettable(node, kAXValueAttribute as CFString, &writable) == .success && writable.boolValue
    }

    private func children(_ node: AXUIElement) -> [AXUIElement] {
        (attribute(node, kAXChildrenAttribute) as? [AXUIElement] ?? [])
    }

    private func nodes(_ root: AXUIElement, depth: Int, budget: Int) -> [AXUIElement] {
        var stack = [(root, 0)]
        var output: [AXUIElement] = []
        let deadline = ContinuousClock.now + .milliseconds(145)
        while let (node, level) = stack.popLast(), output.count < budget, ContinuousClock.now < deadline {
            output.append(node)
            if level < depth {
                stack.append(contentsOf: children(node).prefix(budget).map { ($0, level + 1) })
            }
        }
        return output
    }
}
