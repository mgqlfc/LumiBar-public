import Foundation
import ImageIO
import Observation
import UniformTypeIdentifiers

enum LyricLibraryRematchGroup: String, CaseIterable {
    case missing = "缺少歌词"
    case matched = "已有歌词"
    case manual = "手动选择"
    case adjusted = "其他已调整"
}

struct LyricLibraryEntry: Codable, Identifiable, Sendable {
    struct Decision: Codable, Identifiable, Sendable {
        let id: String
        let source: MusicModel.LyricSource
        let album: String
        let artist: String
        let points: Int?
        let reasons: [String]

        init(_ match: RankedLyric) {
            id = match.id
            source = match.candidate.source
            album = match.candidate.recording.album
            artist = match.candidate.recording.artist
            points = match.score?.points
            reasons = match.rejected ? ["已标记为匹配错误"] : (match.score?.reasons ?? ["录音或时间轴不匹配"])
        }
    }

    let id: String
    let title: String
    let artist: String
    let album: String
    let duration: Double
    let playerID: String
    let playerName: String
    let catalogID: String?
    var webDurationVerified: Bool?
    var updated = Date.now
    var written: Date?
    var source: MusicModel.LyricSource?
    var translationSource: MusicModel.LyricSource?
    var selectionID: String?
    var locked = false
    var manuallySelectedSource: Bool?
    var edited: Bool?
    var lines: [Lyric] = []
    var decisions: [Decision] = []
    var cover: Data?

    init(track: Track) {
        id = track.key
        title = track.title
        artist = track.artist
        album = track.album
        duration = track.duration
        playerID = track.player.rawValue
        playerName = track.player.name
        catalogID = track.catalogID
        webDurationVerified = track.webDurationVerified ? true : nil
    }

    var track: Track {
        Track(player: Player(rawValue: playerID, name: playerName), title: title, artist: artist, album: album,
              duration: duration, position: 0, playing: false, artworkURL: nil, pid: 0,
              observedAt: updated, catalogID: catalogID, webDurationVerified: webDurationVerified == true)
    }

    var hasWordTiming: Bool {
        lines.contains { line in
            Set(line.words.lazy.filter { $0.time.isFinite && $0.duration > 0 }.map(\.time)).count > 1
        }
    }
    var hasTranslation: Bool { lines.contains { $0.translation?.isEmpty == false } }

    func retainingTimeline(from saved: Self) -> Self {
        var merged = lines.isEmpty && !saved.lines.isEmpty ? saved : self
        if !decisions.isEmpty { merged.decisions = decisions }
        merged.cover = cover ?? saved.cover
        merged.written = written ?? saved.written
        return merged
    }

    private var credits: [String] {
        artist.split { "/&、，;".contains($0) }.map { LyricRecording.identity(String($0)) }.filter { !$0.isEmpty }
    }

    fileprivate var identity: RecordingIdentity {
        RecordingIdentity(player: playerID, title: LyricRecording.identity(title), album: LyricRecording.identity(album))
    }

    fileprivate struct RecordingIdentity: Hashable {
        let player, title, album: String
    }

    func canGroup(with other: Self) -> Bool {
        guard identity == other.identity, !identity.title.isEmpty, !identity.album.isEmpty,
              catalogID == nil || other.catalogID == nil || catalogID == other.catalogID else { return false }
        if duration > 0, other.duration > 0, abs(duration - other.duration) > 1,
           !canAbsorbUnverifiedWebRecord(other) && !other.canAbsorbUnverifiedWebRecord(self) { return false }
        let left = credits
        let right = other.credits
        guard let first = left.first, first == right.first else { return false }
        let a = Set(left)
        let b = Set(right)
        return a == b || (first.count >= 4 && (a.isSubset(of: b) || b.isSubset(of: a)))
    }

    fileprivate func canAbsorbUnverifiedWebRecord(_ other: Self) -> Bool {
        webDurationVerified == true && other.webDurationVerified != true && playerID == AppleMusicWebPlayback.firefoxID &&
            identity == other.identity && credits == other.credits && !credits.isEmpty &&
            other.lines.isEmpty && !other.locked && other.edited != true && other.manuallySelectedSource != true
    }

    fileprivate static func preferredMetadata(_ lhs: Self, _ rhs: Self) -> Bool {
        if (lhs.webDurationVerified == true) != (rhs.webDurationVerified == true) { return lhs.webDurationVerified == true }
        if (lhs.catalogID != nil) != (rhs.catalogID != nil) { return lhs.catalogID != nil }
        if (lhs.duration > 0) != (rhs.duration > 0) { return lhs.duration > 0 }
        if lhs.credits.count != rhs.credits.count { return lhs.credits.count > rhs.credits.count }
        return lhs.id < rhs.id
    }

    fileprivate static func preferredLyrics(_ lhs: Self, _ rhs: Self) -> Bool {
        if lhs.lines.isEmpty != rhs.lines.isEmpty { return !lhs.lines.isEmpty }
        if lhs.locked != rhs.locked { return lhs.locked }
        if (lhs.manuallySelectedSource == true) != (rhs.manuallySelectedSource == true) { return lhs.manuallySelectedSource == true }
        if (lhs.duration > 0) != (rhs.duration > 0) { return lhs.duration > 0 }
        let left = lhs.decisions.first { $0.id == lhs.selectionID }?.points
        let right = rhs.decisions.first { $0.id == rhs.selectionID }?.points
        if lhs.hasWordTiming != rhs.hasWordTiming { return lhs.hasWordTiming }
        if lhs.hasTranslation != rhs.hasTranslation { return lhs.hasTranslation }
        if (left ?? -1) != (right ?? -1) { return (left ?? -1) > (right ?? -1) }
        if lhs.updated != rhs.updated { return lhs.updated > rhs.updated }
        return preferredMetadata(lhs, rhs)
    }

    fileprivate func displayingLyrics(from preferred: Self, alongside records: [Self]) -> Self {
        var visible = self
        visible.lines = preferred.lines
        visible.source = preferred.source
        visible.translationSource = preferred.translationSource
        visible.selectionID = preferred.selectionID
        visible.locked = preferred.locked
        visible.manuallySelectedSource = preferred.manuallySelectedSource
        visible.edited = preferred.edited
        visible.updated = preferred.updated
        visible.cover = preferred.cover ?? records.compactMap(\.cover).first
        var seen = Set<String>()
        visible.decisions = ([preferred] + records).flatMap(\.decisions).filter { seen.insert($0.id).inserted }
        return visible
    }
}

@MainActor
@Observable
final class LyricLibrary {
    private(set) var entries: [String: LyricLibraryEntry] = [:]
    private(set) var loading = false
    private var loaded = false
    private var pendingCover: (key: String, data: Data)?
    private var records: [String: LyricLibraryEntry] = [:]
    private var memberKeys: [String: [String]] = [:]
    private var groupIDs: [String: String] = [:]
    @ObservationIgnored private var deletingKeys = Set<String>()
    @ObservationIgnored private let disk: LyricLibraryDisk
    @ObservationIgnored private var loadTask: Task<Void, Never>?

    init(directory: URL? = nil) {
        disk = LyricLibraryDisk(folder: directory ?? URL.applicationSupportDirectory.appending(path: "LumiBar/LyricLibrary"))
    }

    func load(refresh: Bool = false) async {
        if let loadTask {
            await loadTask.value
            return
        }
        guard !loaded || refresh else { return }
        loading = true
        let previousKeys = Set(records.keys)
        let load = Task {
            let saved = await disk.readAll()
            for entry in saved where !deletingKeys.contains(entry.id) {
                if let current = records[entry.id] {
                    records[entry.id] = (current.written ?? current.updated) >= (entry.written ?? entry.updated)
                        ? current.retainingTimeline(from: entry) : entry.retainingTimeline(from: current)
                } else if !previousKeys.contains(entry.id) {
                    records[entry.id] = entry
                }
            }
            refreshGroups()
            loaded = true
            loading = false
            loadTask = nil
        }
        loadTask = load
        await load.value
    }

    func record(track: Track, lines: [Lyric], source: MusicModel.LyricSource?, translation: MusicModel.LyricSource?,
                candidateID: String?, locked: Bool, decisions: [RankedLyric]?, manuallySelectedSource: Bool = false,
                edited: Bool = false, replacingGroup: Bool = false) {
        guard track.duration.isFinite else { return }
        let old = records[track.key]
        // Retain the last usable local timeline when a source is temporarily unreachable.
        if lines.isEmpty, var old, !old.lines.isEmpty {
            if track.webDurationVerified, old.webDurationVerified != true {
                old.webDurationVerified = true
                save(old)
            }
            return
        }
        var entry = LyricLibraryEntry(track: track)
        entry.source = source
        entry.translationSource = translation ?? (lines.contains { $0.translation != nil } ? source : nil)
        entry.selectionID = candidateID
        entry.locked = locked
        entry.manuallySelectedSource = manuallySelectedSource ? true : nil
        entry.edited = edited ? true : nil
        entry.lines = lines
        entry.decisions = decisions?.map(LyricLibraryEntry.Decision.init) ?? old?.decisions ?? []
        entry.cover = (pendingCover?.key == entry.id ? pendingCover?.data : nil) ?? old?.cover
        if replacingGroup {
            let aliases = keys(for: entry.id).filter { $0 != entry.id }.compactMap { records[$0] }.map { record in
                var replacement = record.displayingLyrics(from: entry, alongside: [record])
                replacement.decisions = entry.decisions
                return replacement
            }
            save([entry] + aliases)
        } else {
            save(entry)
        }
    }

    func storeCover(_ data: Data, for key: String) async {
        guard let cover = await Self.thumbnail(data) else { return }
        if var entry = records[key] {
            guard entry.cover != cover else { return }
            entry.cover = cover
            save(entry)
        } else {
            pendingCover = (key, cover)
        }
    }

    func recordDecisions(_ decisions: [RankedLyric], for basis: LyricLibraryEntry) {
        guard !decisions.isEmpty || records[basis.id] == nil else { return }
        var entry = records[basis.id] ?? basis
        entry.decisions = decisions.map(LyricLibraryEntry.Decision.init)
        save(entry)
    }

    func unlock(_ id: String) {
        for key in keys(for: id) {
            guard var entry = records[key], entry.locked || entry.manuallySelectedSource == true else { continue }
            entry.locked = false
            entry.manuallySelectedSource = nil
            save(entry)
        }
    }

    func populatePreview(_ items: [LyricLibraryEntry]) {
        records = Dictionary(uniqueKeysWithValues: items.map { ($0.id, $0) })
        refreshGroups()
    }

    func id(for key: String) -> String { groupIDs[key] ?? key }

    func id(for track: Track) -> String {
        if let id = groupIDs[track.key] { return id }
        let entry = LyricLibraryEntry(track: track)
        var matches = entries.values.filter { group in
            group.identity == entry.identity && (group.canAbsorbUnverifiedWebRecord(entry) ||
                (memberKeys[group.id] ?? []).allSatisfy { records[$0]?.canGroup(with: entry) == true })
        }
        if entry.duration > 0 {
            let known = matches.filter { $0.duration > 0 }
            if !known.isEmpty { matches = known }
        }
        return matches.count == 1 ? matches[0].id : track.key
    }

    func keys(for key: String) -> [String] { memberKeys[id(for: key)] ?? [key] }

    func erase(keys: [String], localFiles: [URL]) async throws {
        let removed = Set(keys)
        deletingKeys.formUnion(removed)
        defer { deletingKeys.subtract(removed) }
        try await disk.erase(keys: keys, localFiles: localFiles, at: .now)
        for key in removed { records.removeValue(forKey: key) }
        if let pendingCover, removed.contains(pendingCover.key) { self.pendingCover = nil }
        refreshGroups()
    }

    private func refreshGroups() {
        var entries: [String: LyricLibraryEntry] = [:]
        var memberKeys: [String: [String]] = [:]
        var groupIDs: [String: String] = [:]
        for bucket in Dictionary(grouping: records.values, by: \.identity).values {
            var groups: [[LyricLibraryEntry]] = []
            for entry in bucket.sorted(by: LyricLibraryEntry.preferredMetadata) {
                // A sparse event must not link otherwise incompatible recordings.
                let matches = groups.indices.filter { index in
                    groups[index].first?.canAbsorbUnverifiedWebRecord(entry) == true ||
                        groups[index].allSatisfy { $0.canGroup(with: entry) }
                }
                if matches.count == 1 { groups[matches[0]].append(entry) }
                else { groups.append([entry]) }
            }
            for group in groups {
                let metadata = group[0]
                let byLyrics = group.sorted(by: LyricLibraryEntry.preferredLyrics)
                entries[metadata.id] = metadata.displayingLyrics(from: byLyrics[0], alongside: byLyrics)
                memberKeys[metadata.id] = [metadata.id] + byLyrics.map(\.id).filter { $0 != metadata.id }
                for entry in group { groupIDs[entry.id] = metadata.id }
            }
        }
        self.entries = entries
        self.memberKeys = memberKeys
        self.groupIDs = groupIDs
    }

    private func save(_ entry: LyricLibraryEntry) { save([entry]) }

    private func save(_ updates: [LyricLibraryEntry]) {
        let now = Date.now
        let snapshots = updates.filter { !deletingKeys.contains($0.id) }.map { update in
            var entry = update
            entry.written = now
            records[entry.id] = entry
            if pendingCover?.key == entry.id { pendingCover = nil }
            return entry
        }
        guard !snapshots.isEmpty else { return }
        refreshGroups()
        Task {
            for snapshot in snapshots { await disk.save(snapshot) }
        }
    }

    @concurrent
    private static func thumbnail(_ data: Data) async -> Data? {
        guard let source = CGImageSourceCreateWithData(data as CFData, nil),
              let image = CGImageSourceCreateThumbnailAtIndex(source, 0, [
                kCGImageSourceCreateThumbnailFromImageAlways: true,
                kCGImageSourceCreateThumbnailWithTransform: true,
                kCGImageSourceThumbnailMaxPixelSize: 144,
              ] as CFDictionary) else { return nil }
        let buffer = NSMutableData()
        guard let target = CGImageDestinationCreateWithData(buffer, UTType.jpeg.identifier as CFString, 1, nil) else { return nil }
        CGImageDestinationAddImage(target, image, [kCGImageDestinationLossyCompressionQuality: 0.82] as CFDictionary)
        return CGImageDestinationFinalize(target) ? buffer as Data : nil
    }
}

private actor LyricLibraryDisk {
    let folder: URL
    private static let byteLimit = 4_194_304
    private var erasedAt: [String: Date] = [:]

    init(folder: URL) { self.folder = folder }

    func readAll() -> [LyricLibraryEntry] {
        let files = (try? FileManager.default.contentsOfDirectory(at: folder, includingPropertiesForKeys: [.fileSizeKey],
                                                                  options: .skipsHiddenFiles)) ?? []
        return files.filter { $0.pathExtension == "json" }.compactMap(read)
    }

    func save(_ entry: LyricLibraryEntry) {
        if let cutoff = erasedAt[entry.id], (entry.written ?? entry.updated) <= cutoff { return }
        let path = folder.appending(path: lyricCacheKey(entry.id) + ".json")
        var merged = entry
        if let previous = read(path) {
            if (previous.written ?? previous.updated) > (entry.written ?? entry.updated) { return }
            merged = entry.retainingTimeline(from: previous)
        }
        guard let data = try? JSONEncoder().encode(merged), data.count <= Self.byteLimit else { return }
        do {
            try FileManager.default.createDirectory(at: folder, withIntermediateDirectories: true)
            try data.write(to: path, options: .atomic)
        } catch {
            NSLog("LumiBar could not save the lyric library record: %@", error.localizedDescription)
        }
    }

    func erase(keys: [String], localFiles: [URL], at date: Date) throws {
        let manager = FileManager.default
        let cached = keys.map { folder.appending(path: lyricCacheKey($0) + ".json") }
        let files = (cached + localFiles).filter { manager.fileExists(atPath: $0.path) }
        if !files.isEmpty {
            let staging = folder.deletingLastPathComponent().appending(path: ".erase-lyrics-\(UUID().uuidString)")
            try manager.createDirectory(at: staging, withIntermediateDirectories: false)
            var moved: [(source: URL, destination: URL)] = []
            do {
                for (index, file) in files.enumerated() {
                    let destination = staging.appending(path: String(index))
                    try manager.moveItem(at: file, to: destination)
                    moved.append((file, destination))
                }
            } catch {
                for item in moved.reversed() {
                    do { try manager.moveItem(at: item.destination, to: item.source) }
                    catch { NSLog("LumiBar could not restore a staged lyric file: %@", error.localizedDescription) }
                }
                if (try? manager.contentsOfDirectory(atPath: staging.path).isEmpty) == true {
                    try? manager.removeItem(at: staging)
                }
                throw error
            }
            try? manager.removeItem(at: staging)
        }
        for key in keys { erasedAt[key] = date }
    }

    private func read(_ path: URL) -> LyricLibraryEntry? {
        guard let size = try? path.resourceValues(forKeys: [.fileSizeKey]).fileSize, size <= Self.byteLimit,
              let data = try? Data(contentsOf: path) else { return nil }
        return try? JSONDecoder().decode(LyricLibraryEntry.self, from: data)
    }
}
