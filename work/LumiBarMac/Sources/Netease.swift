import ApplicationServices
import Foundation
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

    private struct Progress {
        let position: Double
        let duration: Double
        let slider: AXUIElement?
    }

    private struct PlayerView {
        let progress: Progress
        let labels: [String]
    }

    private static let playNames = ["播放", "开始播放", "Play", "Resume"]
    private static let pauseNames = ["暂停", "Pause"]
    private static let previousNames = ["上一首", "上一曲", "上一首歌曲", "上一个", "Previous", "Previous Track", "Previous Song"]
    private static let nextNames = ["下一首", "下一曲", "下一首歌曲", "下一个", "Next", "Next Track", "Next Song"]
    private var lastTrack: Track?
    private var lastProgress: Progress?
    private var cachedPlayer: (pid_t, AXUIElement)?

    func read(pid: pid_t) -> Track? {
        guard AXIsProcessTrusted() else { return nil }
        let app = application(pid)
        let items = controls(app)
        let pause = item(named: Self.pauseNames, in: items)
        let play = item(named: Self.playNames, in: items)
        let state = pause.map { enabled($0) } ?? play.map { _ in false }

        let view = playerView(app)
        let inferred = playStateFromProgress(view?.progress, previous: lastTrack, pid: pid)
        lastProgress = view?.progress
        guard let playing = state ?? inferred, let view else { return nil }
        let progress = view.progress
        guard progress.duration > 0 else { return nil }

        let catalog = historyTrack(matching: view)
        let fromUI = trackLabels(view.labels)
        guard let title = catalog?.name ?? fromUI?.title else { return nil }
        let artists = catalog?.artists.map(\.name).filter { !$0.isEmpty }.joined(separator: " / ")
        let artwork = catalog?.album?.picUrl.flatMap(Self.artworkURL)
        let track = Track(player: .netease, title: title, artist: artists ?? fromUI?.artist ?? "",
                          album: catalog?.album?.name ?? "", duration: progress.duration,
                          position: progress.position, playing: playing, artworkURL: artwork, pid: pid,
                          observedAt: .now, catalogID: catalog?.id,
                          canSeek: progress.slider.map(isWritable) ?? false)
        lastTrack = track
        return track
    }

    func control(_ command: PlaybackCommand, pid: pid_t) -> Bool {
        guard AXIsProcessTrusted() else { return false }
        let app = application(pid)
        if case .seek(let seconds) = command {
            guard let progress = playerView(app)?.progress, let slider = progress.slider,
                  isWritable(slider), seconds.isFinite else { return false }
            return AXUIElementSetAttributeValue(slider, kAXValueAttribute as CFString,
                                               NSNumber(value: min(progress.duration, max(0, seconds)))) == .success
        }
        let items = controls(app)
        let names: [String] = switch command {
        case .toggle: Self.pauseNames + Self.playNames
        case .previous: Self.previousNames
        case .next: Self.nextNames
        case .seek: []
        }
        for name in names {
            if let control = item(named: [name], in: items), enabled(control),
               AXUIElementPerformAction(control, kAXPressAction as CFString) == .success { return true }
        }
        return false
    }

    private func application(_ pid: pid_t) -> AXUIElement {
        if let (cachedPID, application) = cachedPlayer, cachedPID == pid { return application }
        let application = AXUIElementCreateApplication(pid)
        AXUIElementSetMessagingTimeout(application, 0.35)
        lastTrack = nil
        lastProgress = nil
        cachedPlayer = (pid, application)
        return application
    }

    private func controls(_ app: AXUIElement) -> [AXUIElement] {
        guard let bar = element(app, kAXMenuBarAttribute) else { return [] }
        let titles = ["控制", "播放控制", "Controls", "Playback"]
        guard let menuItem = item(named: titles, in: children(bar)),
              let menu = children(menuItem).first else { return [] }
        return children(menu)
    }

    private func item(named names: [String], in items: [AXUIElement]) -> AXUIElement? {
        items.first { item in
            guard let title = attribute(item, kAXTitleAttribute) as? String else { return false }
            return names.contains(title.trimmingCharacters(in: .whitespacesAndNewlines))
        }
    }

    private func playerView(_ app: AXUIElement) -> PlayerView? {
        let windows = (attribute(app, kAXWindowsAttribute) as? [AXUIElement] ?? []).prefix(3)
        for window in windows {
            let candidates = nodes(window, depth: 5, budget: 130).filter {
                guard attribute($0, kAXRoleAttribute) as? String == kAXGroupRole,
                      let size = size($0) else { return false }
                return size.width >= 275 && (40...108).contains(size.height)
            }
            for group in candidates {
                if let view = scan(group, depth: 5, budget: 100) { return view }
            }
            if let view = scan(window, depth: 6, budget: 180) { return view }
        }
        return nil
    }

    private func scan(_ root: AXUIElement, depth: Int, budget: Int) -> PlayerView? {
        var progress: Progress?
        var slider: Progress?
        var labels: [String] = []
        for node in nodes(root, depth: depth, budget: budget) {
            switch attribute(node, kAXRoleAttribute) as? String {
            case kAXStaticTextRole, "AXLink":
                let text = (attribute(node, kAXValueAttribute) as? String)
                    ?? (attribute(node, kAXTitleAttribute) as? String) ?? ""
                let trimmed = text.trimmingCharacters(in: .whitespacesAndNewlines)
                if let (position, duration) = Self.parseTimePair(trimmed) {
                    progress = .init(position: position, duration: duration, slider: nil)
                } else if !trimmed.isEmpty, trimmed.count <= 160, !labels.contains(trimmed) {
                    labels.append(trimmed)
                }
            case kAXSliderRole:
                let position = (attribute(node, kAXValueAttribute) as? NSNumber)?.doubleValue
                let maximum = (attribute(node, kAXMaxValueAttribute) as? NSNumber)?.doubleValue
                if let position, let maximum, maximum.isFinite, (30...86400).contains(maximum),
                   position.isFinite, (0...maximum).contains(position) {
                    slider = .init(position: position, duration: maximum, slider: node)
                }
            default: break
            }
        }
        if let progress {
            let matched = slider.flatMap { abs($0.duration - progress.duration) < 2 ? $0 : nil }
            return .init(progress: matched ?? progress, labels: labels)
        }
        // A bare slider could be volume. Require song text before using it.
        if let slider, labels.count >= 2, slider.duration != 100 {
            return .init(progress: slider, labels: labels)
        }
        return nil
    }

    static func parseTimePair(_ text: String) -> (Double, Double)? {
        let parts = text.split(separator: "/", omittingEmptySubsequences: false)
        func seconds(_ value: Substring) -> Double? {
            let chunks = value.trimmingCharacters(in: .whitespaces).split(separator: ":", omittingEmptySubsequences: false)
            let numbers = chunks.compactMap { Int($0) }
            guard chunks.count == numbers.count, (2...3).contains(numbers.count),
                  numbers.allSatisfy({ (0...1440).contains($0) }), numbers.dropFirst().allSatisfy({ $0 < 60 })
            else { return nil }
            return Double(numbers.reduce(0) { $0 * 60 + $1 })
        }
        guard parts.count == 2, let position = seconds(parts[0]), let duration = seconds(parts[1]),
              duration > 0, duration <= 86400, (0...duration).contains(position) else { return nil }
        return (position, duration)
    }

    private func trackLabels(_ labels: [String]) -> (title: String, artist: String)? {
        // The full window also exposes page labels. Only infer a pair from a small player bar.
        guard (2...4).contains(labels.count) else { return nil }
        let candidates = labels.filter { $0.range(of: #"^\d+:\d\d$"#, options: .regularExpression) == nil }
        guard candidates.count == 2 else { return nil }
        return (candidates[0], candidates[1])
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

    private func historyTrack(matching view: PlayerView) -> CatalogTrack? {
        let home = FileManager.default.homeDirectoryForCurrentUser
        let locations = ["Library/Application Support/com.netease.163music/Documents/storage",
                         "Library/Containers/com.netease.163music/Data/Documents/storage"]
        for location in locations {
            let db = home.appending(path: location).appending(path: "sqlite_storage.sqlite3")
            guard FileManager.default.isReadableFile(atPath: db.path) else { continue }
            let tracks = Self.recentTracks(db)
            let matches = tracks.filter { abs(Double($0.duration) / 1000 - view.progress.duration) <= 1.5 }
            if let exact = matches.first(where: { view.labels.contains($0.name) }) { return exact }
            if let newest = tracks.first, abs(Double(newest.duration) / 1000 - view.progress.duration) <= 1.5 {
                return newest
            }
        }
        return nil
    }

    static func recentTracks(_ url: URL) -> [CatalogTrack] {
        var handle: OpaquePointer?
        let status = sqlite3_open_v2(url.path, &handle, SQLITE_OPEN_READONLY | SQLITE_OPEN_NOMUTEX, nil)
        defer { if let handle { sqlite3_close(handle) } }
        guard status == SQLITE_OK, let handle else { return [] }
        sqlite3_busy_timeout(handle, 100)
        var statement: OpaquePointer?
        let sql = "SELECT jsonStr FROM historyTracks ORDER BY playtime DESC LIMIT 8"
        guard sqlite3_prepare_v2(handle, sql, -1, &statement, nil) == SQLITE_OK, let statement else { return [] }
        defer { sqlite3_finalize(statement) }
        var result: [CatalogTrack] = []
        while sqlite3_step(statement) == SQLITE_ROW {
            let count = Int(sqlite3_column_bytes(statement, 0))
            guard (1...500_000).contains(count), let bytes = sqlite3_column_blob(statement, 0),
                  let row = try? JSONDecoder().decode(CatalogTrack.self, from: Data(bytes: bytes, count: count)),
                  !row.name.isEmpty, row.duration > 0 else { continue }
            result.append(row)
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

    private func size(_ node: AXUIElement) -> CGSize? {
        guard let value = attribute(node, kAXSizeAttribute), CFGetTypeID(value) == AXValueGetTypeID() else { return nil }
        var result = CGSize.zero
        guard AXValueGetValue(value as! AXValue, .cgSize, &result) else { return nil }
        return result
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
        var queue = [(root, 0)]
        var visited = 0
        while visited < queue.count && queue.count < budget {
            let (node, level) = queue[visited]
            visited += 1
            if level < depth {
                queue.append(contentsOf: children(node).prefix(budget - queue.count).map { ($0, level + 1) })
            }
        }
        return queue.map { $0.0 }
    }
}
