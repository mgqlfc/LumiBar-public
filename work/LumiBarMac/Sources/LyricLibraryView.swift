import AppKit
import SwiftUI

struct LyricLibraryView: View {
    let music: MusicModel
    @State private var query = ""
    @State private var filter: Filter = .all
    @State private var sourceFilter: MusicModel.LyricSource?
    @State private var selected: String?
    @State private var translated = true
    @State private var follow = true
    @State private var dialog: Dialog?
    @State private var searchingID: String?
    @State private var candidates: [RankedLyric] = []
    @State private var status: String?
    @State private var editingSong: LyricLibraryEntry?
    @State private var confirmingDeletion: LyricLibraryEntry?
    @State private var deleting = false
    @State private var deletionFailed = false
    @State private var showingSort = false
    @State private var showingFilter = false
    @State private var rematchScope: Set<LyricLibraryRematchGroup> = [.missing]
    @State private var batchTask: Task<Void, Never>?
    @State private var batchProcessed = 0
    @State private var batchTotal = 0
    @State private var libraryStatus: String?
    @State private var showingActions = false
    @State private var exportingLyrics = false
    @State private var sidebarWidth: CGFloat = 286
    @AppStorage("music.lyricLibrary.sort") private var sortOrder = SortOrder.updated
    @FocusState private var searchFocused: Bool
    @FocusState private var dialogFocused: Bool
    @Environment(\.accessibilityReduceMotion) private var reduceMotion
    @Environment(\.colorScheme) private var scheme

    private enum Dialog { case batch, candidates, export(LyricLibraryEntry) }

    static let windowIdentifier = NSUserInterfaceItemIdentifier("LumiBar Lyric Library")
    private static let rematchSymbol = "wand.and.stars"

    private enum SortOrder: String, CaseIterable {
        case updated, updatedOldest, title, artist, album

        var label: String {
            switch self {
            case .updated: "更新时间（新→旧）"
            case .updatedOldest: "更新时间（旧→新）"
            case .title: "歌名"
            case .artist: "歌手"
            case .album: "专辑"
            }
        }

        func precedes(_ a: LyricLibraryEntry, _ b: LyricLibraryEntry) -> Bool {
            let comparison: ComparisonResult
            switch self {
            case .updated, .updatedOldest: comparison = .orderedSame
            case .title: comparison = a.title.localizedStandardCompare(b.title)
            case .artist: comparison = a.artist.localizedStandardCompare(b.artist)
            case .album: comparison = a.album.localizedStandardCompare(b.album)
            }
            if comparison != .orderedSame { return comparison == .orderedAscending }
            if a.updated != b.updated {
                return self == .updatedOldest ? a.updated < b.updated : a.updated > b.updated
            }
            return a.id < b.id
        }
    }

    private enum Filter: String, CaseIterable {
        case all = "全部"
        case missing = "缺少歌词"
        case wordTiming = "逐字"
        case lineTiming = "逐行"
        case locked = "手动选择"
        case adjusted = "已调整"

        static let shortcuts: [Filter] = [.all, .missing, .wordTiming, .locked]
        static let supplemental: [Filter] = [.lineTiming, .adjusted]
    }

    private var tint: Color { .accentColor }
    private var library: LyricLibrary { music.lyricLibrary }
    private var currentID: String? { music.track.map { library.id(for: $0) } }
    private var supplementalFiltersActive: Bool { Filter.supplemental.contains(filter) || sourceFilter != nil }
    private var libraryBusy: Bool { library.loading || deleting || searchingID != nil || batchTask != nil || editingSong != nil }

    private var entries: [LyricLibraryEntry] {
        var result = library.entries.values.map(music.displayedLibraryLyrics)
        if let track = music.track, let currentID, !music.libraryEntryIsRemoved(track), library.entries[currentID] == nil {
            var current = LyricLibraryEntry(track: track)
            current.lines = music.lyrics
            current.locked = music.lyricCandidateLocked
            current.translationSource = music.activeLyricTranslationSource
            result.append(current)
        }
        return result.sorted(by: sortOrder.precedes)
    }

    private var adjustedLyrics: [LyricLibraryEntry] {
        entries.filter { !$0.lines.isEmpty && music.libraryLyricsAreAdjusted($0) }
    }

    private var visible: [LyricLibraryEntry] {
        let search = query.trimmingCharacters(in: .whitespacesAndNewlines)
        return entries.filter { entry in
            matchesSource(entry) && includes(entry, in: filter) && (search.isEmpty ||
                [entry.title, entry.artist, entry.album].contains { $0.localizedStandardContains(search) })
        }
    }

    private func matchesSource(_ entry: LyricLibraryEntry) -> Bool {
        sourceFilter == nil || entry.source == sourceFilter
    }

    private func includes(_ entry: LyricLibraryEntry, in filter: Filter) -> Bool {
        switch filter {
        case .all: true
        case .missing: entry.lines.isEmpty
        case .wordTiming: entry.hasWordTiming
        case .lineTiming: !entry.lines.isEmpty && !entry.hasWordTiming
        case .locked: entry.locked
        case .adjusted: music.libraryLyricsAreAdjusted(entry)
        }
    }

    private var selection: LyricLibraryEntry? {
        let resolved = selected.map { library.id(for: $0) }
        return visible.first(where: { $0.id == resolved }) ?? visible.first
    }

    var body: some View {
        // Sidebar selection may change while an edit stays pinned to its original song.
        let song = editingSong ?? selection
        let presentsDialog = dialog != nil
        HSplitView {
            sidebar.frame(minWidth: 242, idealWidth: 286, maxWidth: 355)
                .onGeometryChange(for: CGFloat.self, of: { $0.size.width }) { sidebarWidth = $0 }
            Group {
                if let song {
                    detail(song).id(song.id)
                } else if library.loading {
                    ProgressView("读取本地歌词…")
                } else if entries.isEmpty {
                    ContentUnavailableView("还没有歌词记录", systemImage: "music.note.list",
                                           description: Text("播放过的曲目会在歌词匹配后出现在这里。"))
                } else if query.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty {
                    ContentUnavailableView("没有符合此筛选的歌曲", systemImage: "line.3.horizontal.decrease")
                } else {
                    ContentUnavailableView("没有找到歌曲", systemImage: "magnifyingglass",
                                           description: Text("试试其他歌名、歌手或专辑。"))
                }
            }.frame(minWidth: 485, maxWidth: .infinity, maxHeight: .infinity)
        }
        .background {
            LibraryAlbumLight(artwork: song.flatMap { $0.id == currentID ? music.artwork : nil },
                              archived: song?.cover, sidebarWidth: sidebarWidth)
                .ignoresSafeArea(.container, edges: .top)
        }
        .overlay(alignment: .topTrailing) {
            if let song {
                matchingActions(song)
                    .disabled(editingSong != nil)
                    .padding(.trailing, 17)
                    .frame(height: 32)
                    .ignoresSafeArea(.container, edges: .top)
            }
        }
        .frame(minWidth: 770, minHeight: 490)
        .disabled(presentsDialog)
        .accessibilityHidden(presentsDialog)
        .overlay {
            ZStack {
                if let dialog {
                    LibraryGlassDialog {
                        switch dialog {
                        case .batch: rematchDialog
                        case .candidates:
                            if let song { candidateDetails(song) }
                        case let .export(entry):
                            LyricExportDialog(current: entry, adjusted: adjustedLyrics, cancel: { self.dialog = nil }) { songs, format in
                                self.dialog = nil
                                exportLyrics(songs, format: format, from: entry.id)
                            }
                        }
                    }
                    .ignoresSafeArea()
                    .focusable()
                    .focusEffectDisabled()
                    .focused($dialogFocused)
                    .defaultFocus($dialogFocused, true)
                    .onAppear { dialogFocused = true }
                    .onExitCommand { self.dialog = nil }
                    .transition(.opacity)
                }
            }
            .animation(reduceMotion ? nil : .easeInOut(duration: 0.14), value: presentsDialog)
        }
        .task {
            if music.previewSpectrum == nil { await library.load() }
            if selected == nil { selected = currentID ?? visible.first?.id }
        }
        .onChange(of: selection?.id) { _, _ in
            status = nil
            showingActions = false
            if case .candidates = dialog { dialog = nil }
        }
        .onChange(of: presentsDialog) { _, presented in
            if presented { searchFocused = false }
        }
        .onReceive(NotificationCenter.default.publisher(for: NSWindow.willCloseNotification)) { notification in
            guard (notification.object as? NSWindow)?.identifier == Self.windowIdentifier else { return }
            dialog = nil
            editingSong = nil
            showingActions = false
        }
        .onDisappear { batchTask?.cancel() }
        .alert("删除本曲记录？", isPresented: Binding(
            get: { confirmingDeletion != nil },
            set: { if !$0 { confirmingDeletion = nil } }
        ), presenting: confirmingDeletion) { song in
            Button("取消", role: .cancel) {}
            Button("删除本曲记录", role: .destructive) {
                deleting = true
                Task {
                    deletionFailed = !(await music.deleteLibrarySong(song))
                    deleting = false
                }
            }
        } message: { song in
            Text("将删除《\(song.title)》在 LumiBar 中的歌词、匹配记录和单曲时移。")
        }
        .alert("无法删除本地数据", isPresented: $deletionFailed) {
            Button("好", role: .cancel) {}
        }
    }

    private var sidebar: some View {
        let songs = visible
        let selectedID = selection?.id
        let allSongs = entries
        let sourceSongs = allSongs.filter(matchesSource)
        return VStack(alignment: .leading, spacing: 10) {
            sidebarToolbar(allSongs)
            HStack(spacing: 7) {
                Image(systemName: "magnifyingglass").foregroundStyle(.secondary)
                TextField("搜索歌曲、歌手或专辑", text: $query)
                    .textFieldStyle(.plain).focused($searchFocused)
                    .accessibilityLabel("搜索歌曲、歌手或专辑")
                if !query.isEmpty {
                    Button { query = "" } label: { Image(systemName: "xmark.circle.fill") }
                        .buttonStyle(.plain).foregroundStyle(.secondary).accessibilityLabel("清除搜索")
                }
            }
            .padding(.horizontal, 10).frame(height: 33)
            .glassEffect(.regular, in: Capsule())
            if let current = music.track, let currentID, !music.libraryEntryIsRemoved(current) {
                let credits = [current.artist, current.album].filter { !$0.isEmpty }.joined(separator: " · ")
                Button {
                    query = ""
                    filter = .all
                    sourceFilter = nil
                    selected = currentID
                } label: {
                    HStack(spacing: 8) {
                        LibraryArtwork(image: music.artwork ?? library.entries[currentID]?.cover.flatMap(NSImage.init(data:)), size: 28)
                        VStack(alignment: .leading, spacing: 1) {
                            Label {
                                Text(current.playing ? "正在播放" : "当前曲目").foregroundStyle(.primary)
                            } icon: {
                                Image(systemName: current.playing ? "waveform" : "pause")
                                    .foregroundStyle(Color(nsColor: LibraryKaraokeLine.accent(for: scheme)))
                            }
                            .font(.caption2.weight(.medium))
                            let title = Text(current.title).fontWeight(.semibold)
                            Group {
                                if credits.isEmpty { title }
                                else { Text("\(title) · \(Text(credits).foregroundStyle(.secondary))") }
                            }
                            .font(.caption).lineLimit(1).truncationMode(.tail)
                        }
                        Spacer(minLength: 0)
                        Image(systemName: "scope").foregroundStyle(.secondary)
                    }.padding(.horizontal, 8).frame(height: 39)
                        .contentShape(RoundedRectangle(cornerRadius: 12))
                        .glassEffect(.regular.interactive(), in: RoundedRectangle(cornerRadius: 12))
                }.buttonStyle(.plain).help("定位当前曲目")
                    .accessibilityLabel("定位当前曲目 \(current.title)").accessibilityValue(credits)
                    .contextMenu {
                        copySongMenu(title: current.title, artist: current.artist, album: current.album)
                    }
            }
            HStack(spacing: 6) {
                LibraryGlassSegments(selection: $filter, choices: Filter.shortcuts, stretches: true) { option in
                    let count = sourceSongs.filter { includes($0, in: option) }.count
                    return count > 0 ? "\(option.rawValue) \(count)" : option.rawValue
                }
                .accessibilityElement(children: .contain).accessibilityLabel("快捷筛选歌曲")
                Button { showingFilter.toggle() } label: {
                    LibraryGlassMenuIcon(symbol: "line.3.horizontal.decrease", size: 26,
                                         selected: supplementalFiltersActive)
                }
                .buttonStyle(.plain)
                .accessibilityLabel("更多筛选")
                .accessibilityValue([Filter.supplemental.contains(filter) ? filter.rawValue : nil, sourceFilter?.title]
                    .compactMap { $0 }.joined(separator: " · "))
                .help("更多筛选")
                .popover(isPresented: $showingFilter, arrowEdge: .top) { filterMenu(allSongs) }
            }
            if batchTask != nil {
                HStack(spacing: 6) {
                    ProgressView(value: Double(batchProcessed), total: Double(batchTotal))
                        .controlSize(.mini).accessibilityHidden(true)
                    Text("\(batchProcessed)/\(batchTotal)").monospacedDigit()
                        .accessibilityLabel("已检查 \(batchProcessed) 首，共 \(batchTotal) 首")
                    Button("停止") { batchTask?.cancel() }.buttonStyle(.glass).controlSize(.small)
                }.font(.caption).padding(.horizontal, 2)
            } else if let libraryStatus {
                Text(libraryStatus).font(.caption).foregroundStyle(.secondary)
            }
            ScrollView {
                LazyVStack(spacing: 3) {
                    ForEach(songs) { entry in
                        let isSelected = entry.id == selectedID
                        Button {
                            selected = entry.id
                            status = nil
                        } label: {
                            HStack(spacing: 9) {
                                cover(for: entry, size: 34)
                                VStack(alignment: .leading, spacing: 3) {
                                    Text(entry.title).font(.body.weight(.medium)).lineLimit(1)
                                    Text(entry.artist).font(.caption).foregroundStyle(.secondary).lineLimit(1)
                                }
                                Spacer(minLength: 0)
                                sidebarBadges(entry)
                            }.padding(6).contentShape(Rectangle())
                        }
                        .buttonStyle(.plain)
                        .glassEffect(isSelected ? .regular.tint(tint.opacity(0.35)).interactive() : .identity,
                                     in: RoundedRectangle(cornerRadius: 9))
                        .accessibilityAddTraits(isSelected ? .isSelected : [])
                        .contextMenu {
                            copySongMenu(title: entry.title, artist: entry.artist, album: entry.album)
                        }
                    }
                }
            }.scrollBounceBehavior(.basedOnSize)
        }
        .padding(14).frame(maxHeight: .infinity, alignment: .top)
        .background {
            Button("搜索歌曲") { searchFocused = true }.keyboardShortcut("f", modifiers: .command)
                .opacity(0).accessibilityHidden(true)
        }
    }

    private func sidebarToolbar(_ songs: [LyricLibraryEntry]) -> some View {
        HStack(spacing: 4) {
            Text("全部歌曲共 \(songs.count) 首").font(.subheadline.weight(.semibold)).monospacedDigit()
                .lineLimit(1).minimumScaleFactor(0.8)
            Spacer(minLength: 2)
            Button {
                libraryStatus = nil
                Task {
                    await library.load(refresh: true)
                    libraryStatus = "已刷新歌曲记录。"
                }
            } label: {
                LibraryGlassMenuIcon(symbol: "arrow.clockwise", size: 26)
            }
            .disabled(libraryBusy || music.previewSpectrum != nil)
            .accessibilityLabel("刷新歌曲记录").help("刷新歌曲记录")
            Button {
                rematchScope = [.missing]
                dialog = .batch
            } label: {
                LibraryGlassMenuIcon(symbol: Self.rematchSymbol, size: 26)
            }
            .disabled(libraryBusy || songs.isEmpty || !music.onlineLyrics || music.previewSpectrum != nil)
            .accessibilityLabel("批量重新匹配").help("批量重新匹配")
            Button { showingSort.toggle() } label: {
                LibraryGlassMenuIcon(symbol: "arrow.up.arrow.down", size: 26)
            }
            .accessibilityLabel("歌曲排序").accessibilityValue(sortOrder.label)
            .help("歌曲排序 · \(sortOrder.label)")
            .popover(isPresented: $showingSort, arrowEdge: .top) {
                LibraryMenu {
                    ForEach(SortOrder.allCases, id: \.self) { order in
                        LibraryMenuItem(order.label, symbol: order == sortOrder ? "checkmark" : nil,
                                        selected: order == sortOrder) {
                            sortOrder = order
                            showingSort = false
                        }.accessibilityAddTraits(order == sortOrder ? .isSelected : [])
                    }
                }
            }
        }.buttonStyle(.plain)
    }

    private func filterMenu(_ songs: [LyricLibraryEntry]) -> some View {
        let present = Set(songs.compactMap(\.source))
        let sources = MusicModel.LyricSource.allCases.filter { $0 != .automatic && (present.contains($0) || $0 == sourceFilter) }
        return VStack(alignment: .leading, spacing: 2) {
            ForEach(Filter.supplemental, id: \.self) { choice in
                LibraryMenuItem(choice.rawValue,
                                symbol: choice == filter ? "checkmark" : nil, selected: choice == filter) {
                    filter = filter == choice ? .all : choice
                }.accessibilityAddTraits(choice == filter ? .isSelected : [])
            }
            Divider().padding(.horizontal, 5).padding(.vertical, 4)
            Text("歌词来源").font(.caption).foregroundStyle(.secondary)
                .padding(.leading, 7).padding(.bottom, 2)
            ScrollView {
                VStack(spacing: 2) {
                    ForEach([nil] + sources.map(Optional.some), id: \.self) { source in
                        LibraryMenuItem(source?.title ?? "所有来源", symbol: source == sourceFilter ? "checkmark" : nil,
                                        selected: source == sourceFilter) {
                            sourceFilter = source
                        }.accessibilityAddTraits(source == sourceFilter ? .isSelected : [])
                    }
                }
            }
            .frame(height: min(122, CGFloat(sources.count + 1) * 31 - 2))
            .scrollBounceBehavior(.basedOnSize)
        }.padding(6).frame(width: 126)
    }

    private var rematchDialog: some View {
        let groups = LyricLibraryRematchGroup.allCases
        let songs = Dictionary(grouping: entries, by: music.libraryRematchGroup(for:))
        let count = rematchScope.reduce(0) { $0 + (songs[$1]?.count ?? 0) }
        let allSelected = Binding(get: { rematchScope.count == groups.count }, set: {
            rematchScope = $0 ? Set(groups) : [.missing]
        })
        return VStack(alignment: .leading, spacing: 12) {
            Text("批量重新匹配歌词").font(.title2.weight(.semibold))
            Text("勾选要重新匹配的歌曲。找到可用歌词后才会替换当前版本。")
                .font(.subheadline).foregroundStyle(.secondary)
            VStack(alignment: .leading, spacing: 9) {
                ForEach(groups, id: \.self) { group in
                    Toggle("\(group.rawValue)（\(songs[group]?.count ?? 0) 首）", isOn: Binding(
                        get: { rematchScope.contains(group) },
                        set: { if $0 { rematchScope.insert(group) } else { rematchScope.remove(group) } }
                    ))
                }
                Divider()
                Toggle("全部重新匹配", isOn: allSelected)
            }.toggleStyle(.checkbox)
            HStack {
                Spacer()
                Button("取消") { dialog = nil }.keyboardShortcut(.cancelAction)
                Button("重新匹配 \(count) 首") {
                    let scope = rematchScope
                    let tracks = entries.filter { scope.contains(music.libraryRematchGroup(for: $0)) }
                    dialog = nil
                    rematch(tracks, scope: scope)
                }.keyboardShortcut(.defaultAction).buttonStyle(.glassProminent)
                    .disabled(count == 0 || libraryBusy || !music.onlineLyrics)
            }.buttonStyle(.glass)
        }.padding(21).frame(width: 386)
    }

    private func rematch(_ tracks: [LyricLibraryEntry], scope: Set<LyricLibraryRematchGroup>) {
        guard !libraryBusy, !tracks.isEmpty else { return }
        batchProcessed = 0
        batchTotal = tracks.count
        libraryStatus = nil
        batchTask = Task {
            var updated = 0
            for track in tracks {
                guard !Task.isCancelled, music.onlineLyrics else { break }
                let result = await music.searchLibraryLyrics(for: track, rematch: true, batchScope: scope)
                guard !Task.isCancelled else { break }
                batchProcessed += 1
                if result.applied { updated += 1 }
            }
            let outcome = batchProcessed == batchTotal ? "匹配完成" : "匹配已停止"
            libraryStatus = "\(outcome) · 已更新 \(updated)/\(batchProcessed) 首"
            batchTask = nil
        }
    }

    private func detail(_ entry: LyricLibraryEntry) -> some View {
        VStack(alignment: .leading, spacing: 0) {
            VStack(alignment: .leading, spacing: 9) {
                HStack(alignment: .top, spacing: 13) {
                    cover(for: entry, size: 62)
                    VStack(alignment: .leading, spacing: 5) {
                        Text(entry.title).font(.system(size: 22, weight: .semibold))
                            .lineLimit(2).fixedSize(horizontal: false, vertical: true).help(entry.title)
                        let credits = [entry.artist, entry.album].filter { !$0.isEmpty }.joined(separator: " · ")
                        if !credits.isEmpty {
                            Text(credits).font(.subheadline).foregroundStyle(.secondary).lineLimit(1).help(credits)
                        }
                        ViewThatFits(in: .horizontal) {
                            HStack(spacing: 12) { trackMetadata(entry) }
                                .fixedSize(horizontal: true, vertical: false)
                            VStack(alignment: .leading, spacing: 3) { trackMetadata(entry) }
                        }
                    }
                    .frame(maxWidth: .infinity, alignment: .leading)
                }
                .contentShape(Rectangle())
                .contextMenu {
                    copySongMenu(title: entry.title, artist: entry.artist, album: entry.album)
                }
                ViewThatFits(in: .horizontal) {
                    detailBadges(entry, showTranslationSource: true)
                    detailBadges(entry, showTranslationSource: false)
                }
                if let status { Text(status).font(.caption).foregroundStyle(.secondary) }
                if currentID == entry.id, let message = music.lyricSourceMessage {
                    Text(message).font(.caption).foregroundStyle(.secondary)
                }
            }
            .padding(.horizontal, 17).padding(.top, 12).padding(.bottom, 5)
            .transaction { $0.animation = nil }
            if let editingSong, editingSong.id == entry.id {
                Divider().padding(.top, 6)
                LyricLibraryEditor(entry: editingSong, selectionChanged: selection?.id != entry.id, save: { lines in
                    guard music.saveEditedLyrics(lines, for: editingSong) else { return false }
                    libraryStatus = "已保存《\(editingSong.title)》的歌词。"
                    return true
                }, close: { self.editingSong = nil })
                .id(editingSong.id)
            } else {
                timelineToolbar(entry).padding(.horizontal, 17).padding(.vertical, 7)
                Divider()
                if entry.lines.isEmpty {
                    ContentUnavailableView("没有本地时间轴", systemImage: "text.alignleft",
                                           description: Text("可以重新匹配，或从右上角导入歌词文件。"))
                } else {
                    LyricLibraryTimeline(entry: entry, music: music, showTranslation: translated, follow: follow)
                }
            }
        }.frame(maxHeight: .infinity, alignment: .top)
    }

    @ViewBuilder
    private func copySongMenu(title: String, artist: String, album: String) -> some View {
        copyTextButton("复制歌名", text: title)
        copyTextButton("复制歌手名", text: artist)
        copyTextButton("复制专辑名", text: album)
        Divider()
        let combined = [title, artist, album]
            .filter { !$0.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty }
            .joined(separator: " · ")
        copyTextButton("全部复制", text: combined)
    }

    private func copyTextButton(_ label: String, text: String) -> some View {
        Button(label) {
            let pasteboard = NSPasteboard.general
            pasteboard.clearContents()
            pasteboard.setString(text, forType: .string)
        }
        .disabled(text.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty)
    }

    private func matchingActions(_ entry: LyricLibraryEntry) -> some View {
        GlassEffectContainer(spacing: 5) {
            HStack(spacing: 8) {
                if searchingID == entry.id { ProgressView().controlSize(.small) }
                Button("重新自动匹配", systemImage: Self.rematchSymbol) { lookUp(entry, rematch: true) }
                    .disabled(libraryBusy || entry.locked || !music.onlineLyrics || music.previewSpectrum != nil)
                Button("搜索候选", systemImage: "magnifyingglass") { lookUp(entry, rematch: false) }
                    .disabled(libraryBusy || !music.onlineLyrics || music.previewSpectrum != nil)
                Button("匹配详情", systemImage: "list.number") {
                    candidates = currentID == entry.id ? music.rankedLyrics : []
                    dialog = .candidates
                }.disabled(entry.decisions.isEmpty && music.lyricRejections(for: entry.track).isEmpty)
                Button { showingActions.toggle() } label: {
                    Image(systemName: "ellipsis").frame(height: 14)
                }
                .accessibilityLabel("更多歌词操作").help("更多歌词操作")
                .popover(isPresented: $showingActions, arrowEdge: .bottom) { actionMenu(for: entry) }
            }
            .lineLimit(1).fixedSize(horizontal: true, vertical: false)
            .controlSize(.small).buttonStyle(.glass)
        }
    }

    private func timelineToolbar(_ entry: LyricLibraryEntry) -> some View {
        let offset = music.lyricOffset(for: entry.id)
        return HStack(spacing: 12) {
            HStack(spacing: 4) {
                Button { music.adjustOffset(-0.2, for: entry.id) } label: {
                    Image(systemName: "minus").frame(width: 32, height: 26).contentShape(Rectangle())
                }.help("歌词延后 0.2 秒").accessibilityLabel("歌词延后 0.2 秒")
                Button { music.adjustOffset(-offset, for: entry.id) } label: {
                    HStack(spacing: 4) {
                        Image(systemName: "arrow.counterclockwise").font(.caption2).foregroundStyle(.secondary)
                        Text("时移").font(.caption2).foregroundStyle(.secondary)
                        Text(offset.formatted(.number.precision(.fractionLength(1))
                            .sign(strategy: .always(includingZero: false))) + "\u{00a0}秒").monospacedDigit()
                    }
                    .lineLimit(1).fixedSize(horizontal: true, vertical: false)
                    .frame(minWidth: 58, minHeight: 26).contentShape(Rectangle())
                }.help("重置本曲歌词时移").accessibilityLabel("重置本曲歌词时移")
                Button { music.adjustOffset(0.2, for: entry.id) } label: {
                    Image(systemName: "plus").frame(width: 32, height: 26).contentShape(Rectangle())
                }.help("歌词提前 0.2 秒").accessibilityLabel("歌词提前 0.2 秒")
            }
            .buttonStyle(.borderless).padding(.horizontal, 3)
            .glassEffect(.regular, in: Capsule())
            .disabled(entry.lines.isEmpty)
            .transaction { $0.animation = nil }
            if entry.hasTranslation {
                LibraryGlassSegments(selection: $translated, choices: [false, true]) { $0 ? "原文与译文" : "原文" }
                    .accessibilityElement(children: .contain).accessibilityLabel("显示译文")
            }
            Spacer(minLength: 0)
            if entry.id == currentID, !entry.lines.isEmpty {
                Button { follow.toggle() } label: {
                    Label("跟随播放", systemImage: follow ? "checkmark" : "waveform")
                        .font(.caption.weight(.medium))
                        .padding(.horizontal, 9).frame(height: 25).contentShape(Capsule())
                        .glassEffect(.regular.tint(follow ? tint.opacity(0.34) : nil).interactive(), in: Capsule())
                }.buttonStyle(.plain).accessibilityAddTraits(follow ? .isSelected : [])
            }
        }
        .controlSize(.small)
    }

    private func actionMenu(for entry: LyricLibraryEntry) -> some View {
        LibraryMenu {
            LibraryMenuItem("手动编辑歌词…", symbol: "pencil") {
                showingActions = false
                follow = false
                searchFocused = false
                editingSong = entry
            }.disabled(libraryBusy || music.previewSpectrum != nil)
            LibraryMenuItem("导入本地歌词…", symbol: "square.and.arrow.down") {
                showingActions = false
                music.importLyrics(for: entry.track) { message in
                    if selection?.id == entry.id { status = message }
                }
            }.disabled(libraryBusy || music.previewSpectrum != nil)
            LibraryMenuItem("导出歌词文件…", symbol: "square.and.arrow.up") {
                showingActions = false
                dialog = .export(entry)
            }.disabled(libraryBusy || exportingLyrics || (entry.lines.isEmpty && adjustedLyrics.isEmpty))
            if let id = entry.selectionID, entry.source != .local {
                LibraryMenuItem("标记当前歌词匹配错误", symbol: "flag") {
                    showingActions = false
                    markMatch(true, id: id, entry: entry)
                }.disabled(libraryBusy)
            }
            if entry.locked, entry.source != .local {
                LibraryMenuItem("恢复自动匹配", symbol: "arrow.counterclockwise") {
                    showingActions = false
                    status = music.unlockLibraryLyrics(for: entry) ? "已取消锁定。" : "无法取消锁定。"
                }.disabled(libraryBusy || music.previewSpectrum != nil)
            }
            Divider()
            LibraryMenuItem("删除本曲记录…", symbol: "trash", destructive: true) {
                showingActions = false
                confirmingDeletion = entry
            }.disabled(libraryBusy || music.previewSpectrum != nil)
        }
    }

    private func exportLyrics(_ entries: [LyricLibraryEntry], format: LocalLyricFile, from entryID: String) {
        guard !exportingLyrics, !entries.isEmpty else { return }
        exportingLyrics = true
        let songs = entries.map { entry in
            LyricExportPanel.Song(track: entry.track, lines: entry.lines, offset: music.lyricOffset(for: entry.id))
        }
        let window = NSApp.windows.first { $0.identifier == Self.windowIdentifier && $0.isVisible }
        LyricExportPanel.begin(songs: songs, format: format, attachedTo: window) { message in
            exportingLyrics = false
            if let message, selection?.id == entryID { status = message }
        }
    }

    private func candidateDetails(_ entry: LyricLibraryEntry) -> some View {
        let rejected = music.lyricRejections(for: entry.track)
        let unlisted = rejected.subtracting(entry.decisions.map(\.id)).sorted()
        return VStack(alignment: .leading, spacing: 12) {
            HStack {
                Text("歌词匹配").font(.title2.weight(.semibold))
                Spacer()
                Button("完成") { dialog = nil }.buttonStyle(.glass).keyboardShortcut(.defaultAction)
            }
            if entry.decisions.isEmpty && unlisted.isEmpty {
                Text("本次没有找到可用的候选歌词。").foregroundStyle(.secondary)
            }
            ScrollView {
                LazyVStack(alignment: .leading, spacing: 6) {
                    ForEach(entry.decisions) { decision in
                        let marked = rejected.contains(decision.id)
                        HStack(spacing: 10) {
                            VStack(alignment: .leading, spacing: 3) {
                                LibraryBadge(kind: .source(decision.source))
                                Text([decision.artist, decision.album].filter { !$0.isEmpty }.joined(separator: " · "))
                                    .font(.caption).foregroundStyle(.secondary)
                                Text(marked ? "已标记为匹配错误" : decision.reasons.joined(separator: " · "))
                                    .font(.caption).foregroundStyle(.secondary)
                            }
                            Spacer()
                            if decision.id == entry.selectionID {
                                Label("已采用", systemImage: "checkmark").font(.caption).foregroundStyle(tint)
                            }
                            if !marked { Text(decision.points.map { "得分 \($0)" } ?? "已排除").font(.caption.monospacedDigit()) }
                            if !marked, let match = candidates.first(where: { $0.id == decision.id && $0.score != nil }) {
                                Button("选用并锁定") {
                                    if music.lockLyricCandidate(match, for: entry.track) {
                                        dialog = nil
                                        status = "已锁定这份歌词。"
                                    }
                                }.buttonStyle(.glass)
                            }
                            if decision.source != .local && decision.source != .automatic {
                                matchFlag(marked, id: decision.id, source: decision.source, entry: entry)
                            }
                        }.padding(9).background(.primary.opacity(0.045), in: RoundedRectangle(cornerRadius: 8))
                    }
                    ForEach(unlisted, id: \.self) { id in
                        if let provider = id.split(separator: ":", maxSplits: 1).first.flatMap({ MusicModel.LyricSource(rawValue: String($0)) }) {
                            HStack(spacing: 10) {
                                LibraryBadge(kind: .source(provider))
                                Text("已标记为匹配错误").font(.caption).foregroundStyle(.secondary)
                                Spacer()
                                matchFlag(true, id: id, source: provider, entry: entry)
                            }.padding(9).background(.primary.opacity(0.045), in: RoundedRectangle(cornerRadius: 8))
                        }
                    }
                }
            }.scrollBounceBehavior(.basedOnSize)
        }.padding(20).frame(width: 560, height: 383)
    }

    private func matchFlag(_ marked: Bool, id: String, source: MusicModel.LyricSource, entry: LyricLibraryEntry) -> some View {
        let label = marked ? "撤销匹配错误标记" : "标记为匹配错误"
        return Button { markMatch(!marked, id: id, entry: entry) } label: {
            Image(systemName: marked ? "arrow.uturn.backward" : "flag").frame(minWidth: 14)
        }
        .buttonStyle(.glass).controlSize(.small).help(label).accessibilityLabel(source.title + " · " + label)
    }

    private func markMatch(_ rejected: Bool, id: String, entry: LyricLibraryEntry) {
        music.setLyricMatchRejected(rejected, id: id, for: entry)
        status = rejected ? "已标记，自动匹配会跳过这份歌词。" : "已撤销匹配错误标记。"
    }

    private func lookUp(_ entry: LyricLibraryEntry, rematch: Bool) {
        searchingID = entry.id
        status = nil
        Task {
            let result = await music.searchLibraryLyrics(for: entry, rematch: rematch)
            searchingID = nil
            guard !Task.isCancelled, selection?.id == entry.id else { return }
            if result.candidates.isEmpty {
                status = "本次没有找到可用歌词。"
            } else if rematch {
                status = result.applied ? "已更新匹配结果。"
                    : (result.candidates.contains { $0.score != nil } ? "已保留当前选用的歌词。" : "本次没有找到可用歌词。")
            } else {
                candidates = result.candidates
                dialog = .candidates
            }
        }
    }

    private func cover(for entry: LyricLibraryEntry, size: CGFloat) -> some View {
        let image = entry.id == currentID ? music.artwork ?? entry.cover.flatMap(NSImage.init(data:))
            : entry.cover.flatMap(NSImage.init(data:))
        return LibraryArtwork(image: image, size: size)
    }

    private func sidebarBadges(_ entry: LyricLibraryEntry) -> some View {
        VStack(alignment: .trailing, spacing: 3) {
            if entry.edited == true { LibraryBadge(kind: .edited, compact: true) }
            else if let source = entry.source { LibraryBadge(kind: .source(source), compact: true) }
            if entry.hasWordTiming || entry.hasTranslation || entry.locked {
                HStack(spacing: 3) {
                    if entry.hasWordTiming { LibraryBadge(kind: .wordTiming, compact: true, iconOnly: true) }
                    if entry.hasTranslation {
                        LibraryBadge(kind: .translation(entry.translationSource), compact: true, iconOnly: true)
                    }
                    if entry.locked && entry.edited != true { LibraryBadge(kind: .locked, compact: true, iconOnly: true) }
                }
            }
        }.fixedSize(horizontal: true, vertical: false)
    }

    private func detailBadges(_ entry: LyricLibraryEntry, showTranslationSource: Bool) -> some View {
        HStack(spacing: 5) {
            LibraryBadge(kind: entry.edited == true ? .edited
                : entry.source.map(LibraryBadge.Kind.source) ?? .unassigned(hasLyrics: !entry.lines.isEmpty))
            if entry.hasWordTiming { LibraryBadge(kind: .wordTiming) }
            if entry.hasTranslation {
                LibraryBadge(kind: .translation(showTranslationSource && entry.edited != true ? entry.translationSource : nil))
            }
            if entry.locked && entry.edited != true { LibraryBadge(kind: .locked) }
        }.fixedSize(horizontal: true, vertical: false)
    }

    @ViewBuilder
    private func trackMetadata(_ entry: LyricLibraryEntry) -> some View {
        labeledMetadata("歌曲时长", duration(entry.duration), symbol: "clock")
        if entry.source != nil {
            labeledMetadata("歌词更新", entry.updated.formatted(.dateTime.month().day()), symbol: "calendar")
        }
    }

    private func labeledMetadata(_ label: String, _ value: String, symbol: String) -> some View {
        Label {
            HStack(spacing: 5) {
                Text(label).foregroundStyle(.secondary)
                Text(value).monospacedDigit().foregroundStyle(.primary)
            }
        } icon: {
            Image(systemName: symbol).foregroundStyle(.secondary)
        }.font(.caption)
    }

    private func duration(_ value: Double) -> String {
        guard value.isFinite, (0...86_400).contains(value), value > 0 else { return "—" }
        let seconds = Int(value)
        return String(format: "%d:%02d", seconds / 60, seconds % 60)
    }
}

private struct LyricLibraryTimeline: View {
    let entry: LyricLibraryEntry
    let music: MusicModel
    let showTranslation: Bool
    let follow: Bool
    @State private var expandedLine: Int?
    @State private var scrollWidth: CGFloat = 450
    @Environment(\.accessibilityReduceMotion) private var reduceMotion
    @Environment(\.colorScheme) private var scheme
    private static let timelineScale: CGFloat = 1.15
    private let timelineInset = 12 * Self.timelineScale
    private let horizontalRowInset = 3 * Self.timelineScale
    private let verticalRowInset = 8 * Self.timelineScale
    private let columnGap = 12 * Self.timelineScale
    private let timestampWidth: CGFloat = 86
    private let overlayClearance: CGFloat = 10
    private let timingFont = Font.system(size: 11 * Self.timelineScale).monospacedDigit()

    private var lyricWidth: CGFloat {
        max(110, scrollWidth - 2 * (timelineInset + horizontalRowInset) - timestampWidth - columnGap - overlayClearance)
    }

    var body: some View {
        let activeTrack = music.track.flatMap { music.lyricLibrary.id(for: $0) == entry.id && follow ? $0 : nil }
        TimelineView(.animation(minimumInterval: 1.0 / 30, paused: activeTrack?.playing != true)) { context in
            let position = activeTrack?.lyricPosition(at: context.date,
                offset: music.playbackCalibration + music.lyricOffset(for: entry.id))
            let active = position.flatMap { time in entry.lines.lastIndex(where: { $0.time <= time }) }
            ScrollViewReader { reader in
                ScrollView {
                    LazyVStack(alignment: .leading, spacing: 3 * Self.timelineScale) {
                        ForEach(entry.lines.indices, id: \.self) { index in
                            row(entry.lines[index], index: index, active: active == index, position: position)
                                .id(index)
                        }
                    }
                    .padding(timelineInset)
                    .padding(.trailing, overlayClearance)
                    .nativeOverlayScrollIndicator()
                }
                .onGeometryChange(for: CGFloat.self, of: { $0.size.width }) { scrollWidth = $0 }
                .onChange(of: active, initial: true) { previous, line in
                    guard let line else { return }
                    if !reduceMotion && previous.map({ abs($0 - line) == 1 }) == true {
                        withAnimation(.smooth(duration: 0.25)) { reader.scrollTo(line, anchor: .center) }
                    } else {
                        reader.scrollTo(line, anchor: .center)
                    }
                }
            }
        }
    }

    private func row(_ lyric: Lyric, index: Int, active: Bool, position: Double?) -> some View {
        HStack(alignment: .firstTextBaseline, spacing: columnGap) {
            Group {
                if lyric.words.isEmpty {
                    timestampLabel(lyric.time)
                } else {
                    Button {
                        expandedLine = expandedLine == index ? nil : index
                    } label: {
                        timestampLabel(lyric.time, arrow: expandedLine == index ? "chevron.down" : "chevron.right")
                            .frame(minHeight: 21 * Self.timelineScale)
                            .contentShape(Capsule())
                            .glassEffect(expandedLine == index ? .regular.interactive() : .identity, in: Capsule())
                    }.buttonStyle(.plain).help("查看这一行的逐字时间")
                }
            }.font(timingFont)
                .foregroundStyle(active ? Color(nsColor: LibraryKaraokeLine.accent(for: scheme)) : .secondary)
                .frame(width: timestampWidth, alignment: .leading)
            VStack(alignment: .leading, spacing: 5 * Self.timelineScale) {
                LibraryKaraokeLine(lyric: lyric, active: active, position: active ? position : nil,
                                   showTranslation: showTranslation, width: lyricWidth, scale: Self.timelineScale)
                if expandedLine == index {
                    Text(lyric.words.map { timestamp($0.time) + " " + $0.text }.joined(separator: "  ·  "))
                        .font(timingFont).foregroundStyle(.secondary).textSelection(.enabled)
                }
            }.frame(maxWidth: .infinity, alignment: .leading)
        }.padding(.horizontal, horizontalRowInset).padding(.vertical, verticalRowInset)
    }

    private func timestampLabel(_ time: Double, arrow: String? = nil) -> some View {
        HStack(spacing: 5) {
            Group {
                if let arrow { Image(systemName: arrow) }
                else { Color.clear.frame(height: 1) }
            }
            .frame(width: 11)
            .accessibilityHidden(true)
            Text(timestamp(time)).lineLimit(1).minimumScaleFactor(0.76)
        }
        .padding(.horizontal, 4 * Self.timelineScale)
    }

    private func timestamp(_ value: Double) -> String {
        guard value.isFinite, (0...86_400).contains(value) else { return "—" }
        let centiseconds = Int((value * 100).rounded())
        return String(format: "%02d:%02d.%02d", centiseconds / 6000, centiseconds / 100 % 60, centiseconds % 100)
    }
}
