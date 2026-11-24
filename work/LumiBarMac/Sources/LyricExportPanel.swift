import AppKit
import SwiftUI

struct LyricExportDialog: View {
    private enum Scope: String, CaseIterable {
        case current = "本首歌曲"
        case adjusted = "已调整歌曲"
    }

    let current: LyricLibraryEntry
    let adjusted: [LyricLibraryEntry]
    let cancel: () -> Void
    let proceed: ([LyricLibraryEntry], LocalLyricFile) -> Void
    @State private var scope: Scope = .current
    @State private var checked: Set<String> = []
    @State private var formatPreference: LocalLyricFile?

    private var selected: [LyricLibraryEntry] {
        switch scope {
        case .current: current.lines.isEmpty ? [] : [current]
        case .adjusted: adjusted.filter { checked.contains($0.id) }
        }
    }

    private var format: LocalLyricFile {
        formatPreference ?? (selected.contains { LocalLyricFile.preferred(for: $0.lines) == .ttml } ? .ttml : .lrc)
    }

    var body: some View {
        VStack(alignment: .leading, spacing: 13) {
            Text("导出歌词文件").font(.title2.weight(.semibold))
            LibraryGlassSegments(selection: $scope, choices: Scope.allCases, stretches: true) { $0.rawValue }
                .accessibilityElement(children: .contain).accessibilityLabel("导出范围")

            switch scope {
            case .current:
                if current.lines.isEmpty { Text("这首歌暂无可导出的歌词。").foregroundStyle(.secondary) }
                else { songLabel(current).padding(.vertical, 5) }
            case .adjusted:
                if adjusted.isEmpty { Text("还没有带歌词的已调整歌曲。").foregroundStyle(.secondary) }
                else { songPicker }
            }

            HStack(spacing: 10) {
                Text("格式").font(.subheadline)
                LibraryGlassSegments(selection: Binding(get: { format }, set: { formatPreference = $0 }),
                                     choices: LocalLyricFile.allCases, stretches: true) { item in
                    item == .lrc ? "LRC（逐行）" : "TTML（逐字和译文）"
                }
                .accessibilityElement(children: .contain).accessibilityLabel("歌词文件格式")
            }

            HStack(spacing: 8) {
                Spacer(minLength: 0)
                Button("取消", action: cancel).keyboardShortcut(.cancelAction)
                Button("选择保存位置…") { proceed(selected, format) }
                    .buttonStyle(.glassProminent).keyboardShortcut(.defaultAction).disabled(selected.isEmpty)
            }
            .buttonStyle(.glass)
        }
        .padding(22).frame(width: 454)
        .onAppear { if current.lines.isEmpty { scope = .adjusted } }
    }

    private var songPicker: some View {
        VStack(alignment: .leading, spacing: 6) {
            HStack {
                Text("已选 \(selected.count) 首").font(.caption.monospacedDigit()).foregroundStyle(.secondary)
                Spacer()
                let allSelected = selected.count == adjusted.count
                Button(allSelected ? "取消全选" : "全选") {
                    checked = allSelected ? [] : Set(adjusted.map(\.id))
                }
                .buttonStyle(.glass).controlSize(.small)
            }
            ScrollView {
                LazyVStack(alignment: .leading, spacing: 5) {
                    ForEach(adjusted) { song in
                        Toggle(isOn: Binding(get: { checked.contains(song.id) }, set: {
                            if $0 { checked.insert(song.id) } else { checked.remove(song.id) }
                        })) { songLabel(song) }
                            .toggleStyle(.checkbox).padding(.vertical, 4)
                    }
                }
                .padding(.horizontal, 2).padding(.trailing, 10)
                .nativeOverlayScrollIndicator()
            }
            .frame(height: min(205, CGFloat(adjusted.count) * 43))
            .scrollBounceBehavior(.basedOnSize)
        }
    }

    private func songLabel(_ song: LyricLibraryEntry) -> some View {
        let credits = [song.artist, song.album].filter { !$0.isEmpty }.joined(separator: " · ")
        return VStack(alignment: .leading, spacing: 2) {
            Text(song.title).font(.subheadline.weight(.medium)).lineLimit(1).help(song.title)
            if !credits.isEmpty {
                Text(credits).font(.caption).foregroundStyle(.secondary).lineLimit(1).help(credits)
            }
        }
        .frame(maxWidth: .infinity, alignment: .leading)
    }
}

enum LyricExportPanel {
    struct Song: Sendable {
        let track: Track
        let lines: [Lyric]
        let offset: Double
    }

    @MainActor
    static func begin(songs: [Song], format: LocalLyricFile, attachedTo window: NSWindow?,
                      completion: @escaping (String?) -> Void) {
        guard let first = songs.first else { completion(nil); return }
        let panel: NSSavePanel
        if songs.count > 1 {
            let directoryPicker = NSOpenPanel()
            directoryPicker.title = "选择歌词导出位置"
            directoryPicker.prompt = "导出到此处"
            directoryPicker.canChooseFiles = false
            directoryPicker.canChooseDirectories = true
            directoryPicker.canCreateDirectories = true
            directoryPicker.message = "每首歌词单独保存到同一个新文件夹。"
            panel = directoryPicker
        } else {
            let filePicker = NSSavePanel()
            filePicker.title = "导出歌词文件"
            filePicker.prompt = "导出"
            filePicker.allowedContentTypes = [format.contentType]
            filePicker.allowsOtherFileTypes = false
            filePicker.isExtensionHidden = false
            filePicker.nameFieldStringValue = fileName(for: first.track) + "." + format.rawValue
            panel = filePicker
        }
        let finish: (NSApplication.ModalResponse) -> Void = { response in
            MainActor.assumeIsolated {
                guard response == .OK, let url = panel.url else { completion(nil); return }
                Task { @MainActor in
                    let message = await Task.detached(priority: .userInitiated) {
                        write(songs: songs, format: format, to: url)
                    }.value
                    completion(message)
                }
            }
        }
        if let window { panel.beginSheetModal(for: window, completionHandler: finish) }
        else { panel.begin(completionHandler: finish) }
    }

    static func write(songs: [Song], format: LocalLyricFile, to url: URL) -> String {
        let scoped = url.startAccessingSecurityScopedResource()
        defer { if scoped { url.stopAccessingSecurityScopedResource() } }
        if songs.count == 1, let song = songs.first {
            guard url.pathExtension.lowercased() == format.rawValue else {
                return "请使用 .\(format.rawValue) 文件后缀。"
            }
            guard let data = format.encode(lines: song.lines, track: song.track, offset: song.offset) else {
                return "无法导出歌词，文件过大或时间轴无效。"
            }
            do {
                try data.write(to: url, options: .atomic)
                return "已导出歌词。"
            } catch {
                return "无法保存歌词文件，请检查所选位置的写入权限。"
            }
        }

        var folder: URL?
        var usedNames = Set<String>()
        var exported = 0
        for song in songs {
            guard let data = format.encode(lines: song.lines, track: song.track, offset: song.offset) else { continue }
            do {
                if folder == nil { folder = try makeExportFolder(in: url) }
                guard let folder else { break }
                let base = fileName(for: song.track)
                var name = base
                var suffix = 2
                while !usedNames.insert(name.precomposedStringWithCanonicalMapping.lowercased()).inserted {
                    name = "\(base) (\(suffix))"
                    suffix += 1
                }
                let file = folder.appending(path: name + "." + format.rawValue)
                try data.write(to: file, options: .atomic)
                exported += 1
            } catch {
                if folder == nil { break }
            }
        }
        guard let folder, exported > 0 else { return "无法导出歌词，请检查时间轴和保存位置。" }
        if exported == songs.count { return "已将 \(exported) 首歌词导出到「\(folder.lastPathComponent)」。" }
        return "已导出 \(exported)/\(songs.count) 首歌词到「\(folder.lastPathComponent)」。"
    }

    private static func makeExportFolder(in parent: URL) throws -> URL {
        var index = 1
        while true {
            let name = index == 1 ? "LumiBar 歌词" : "LumiBar 歌词 (\(index))"
            let folder = parent.appending(path: name, directoryHint: .isDirectory)
            do {
                try FileManager.default.createDirectory(at: folder, withIntermediateDirectories: false)
                return folder
            } catch CocoaError.fileWriteFileExists {
                index += 1
            }
        }
    }

    private static func fileName(for track: Track) -> String {
        let invalid = CharacterSet(charactersIn: "/:\\").union(.controlCharacters).union(.newlines)
        let credits = [track.title, track.artist]
            .map { $0.components(separatedBy: invalid).joined(separator: " ").trimmingCharacters(in: .whitespacesAndNewlines) }
            .filter { !$0.isEmpty }.joined(separator: " - ")
        var bytes = 0
        let prefix = credits.prefix { character in
            bytes += String(character).decomposedStringWithCanonicalMapping.utf8.count
            return bytes <= 180
        }
        let fileName = prefix.trimmingCharacters(in: .whitespacesAndNewlines.union(CharacterSet(charactersIn: ".")))
        return fileName.isEmpty ? "歌词" : fileName
    }
}
