import SwiftUI

private enum LyricEditClock {
    static func parse(_ input: String) -> Double? {
        let fields = input.trimmingCharacters(in: .whitespaces).split(separator: ":", omittingEmptySubsequences: false)
        guard fields.count == 2, let minutes = Int(fields[0]), (0...1440).contains(minutes),
              let seconds = Double(fields[1]), (0..<60).contains(seconds) else { return nil }
        let value = Double(minutes) * 60 + seconds
        return value <= 86_400 ? (value * 1_000).rounded() / 1_000 : nil
    }

    static func format(_ time: Double) -> String {
        guard time.isFinite, (0...86_400).contains(time) else { return "00:00.000" }
        let milliseconds = Int((time * 1_000).rounded())
        return String(format: "%02d:%02d.%03d", milliseconds / 60_000, milliseconds / 1_000 % 60, milliseconds % 1_000)
    }
}

struct LyricWordEdit: Identifiable {
    let id = UUID()
    var text: String
    var start: String?
    var end: String?
    private let previous: Lyric.Word?

    init(_ word: Lyric.Word) {
        previous = word
        text = word.text
    }

    init(text: String, start: String) {
        previous = nil
        self.text = text
        self.start = start
        end = ""
    }

    private func previousStart(shift: Double) -> Double? {
        previous.map { max(0, $0.time + shift) }
    }

    func startClock(shift: Double) -> String {
        start ?? previousStart(shift: shift).map(LyricEditClock.format) ?? ""
    }

    func endClock(shift: Double) -> String {
        end ?? previous.flatMap { word in
            previousStart(shift: shift).map { LyricEditClock.format($0 + word.duration) }
        } ?? ""
    }

    func lyric(shift: Double) -> Lyric.Word? {
        let inheritedStart = previousStart(shift: shift)
        let unchangedStart = start == nil || start == inheritedStart.map(LyricEditClock.format)
        let inheritedEnd = previous.flatMap { word in inheritedStart.map { $0 + word.duration } }
        let unchangedEnd = end == nil || end == inheritedEnd.map(LyricEditClock.format)
        let startTime = unchangedStart ? inheritedStart : start.flatMap(LyricEditClock.parse)
        let endTime = unchangedEnd ? inheritedEnd : end.flatMap(LyricEditClock.parse)
        guard !text.isEmpty, let startTime, let endTime,
              (0...86_400).contains(startTime), (startTime...86_400).contains(endTime) else { return nil }
        guard endTime > startTime || (unchangedStart && unchangedEnd && previous?.duration == 0) else { return nil }
        let duration = if unchangedStart && unchangedEnd, let previous { previous.duration }
                       else { endTime - startTime }
        return Lyric.Word(time: startTime, duration: duration, text: text)
    }
}

struct LyricEditRow: Identifiable {
    enum Validation: Error {
        case lineTime, original, emptyWords, word(Int), wordOrdering(Int)

        var message: String {
            switch self {
            case .lineTime: "时间无效，请填写分:秒.毫秒。"
            case .original: "请输入歌词原文。"
            case .emptyWords: "请添加逐字时间，或切换为逐行。"
            case .word(let index): "第 \(index + 1) 段需要文本，结束时间须晚于开始时间。"
            case .wordOrdering(let index): "第 \(index + 1) 段的开始时间不能早于上一段。"
            }
        }
    }

    let id = UUID()
    var clock: String
    var original: String
    var translation: String
    var words: [LyricWordEdit]
    private(set) var wordTiming: Bool
    private let previous: Lyric?

    init(_ lyric: Lyric? = nil, at time: Double = 0) {
        previous = lyric
        clock = LyricEditClock.format(lyric?.time ?? time)
        original = lyric?.text ?? ""
        translation = lyric?.translation ?? ""
        words = lyric?.words.map(LyricWordEdit.init) ?? []
        wordTiming = !words.isEmpty
    }

    var time: Double? {
        if let previous, clock == LyricEditClock.format(previous.time) { return previous.time }
        return LyricEditClock.parse(clock)
    }

    var shift: Double {
        guard let time, let previous else { return 0 }
        return time - previous.time
    }

    var wordText: String {
        let text = words.map(\.text)
        if let previous, text == previous.words.map(\.text) { return previous.text }
        return text.joined()
    }

    mutating func switchTiming() {
        if wordTiming {
            if !words.isEmpty { original = wordText }
        } else if words.isEmpty || wordText != original {
            words = [LyricWordEdit(text: original, start: clock)]
        }
        wordTiming.toggle()
    }

    @discardableResult
    mutating func appendWord() -> UUID {
        let start = words.last.map { $0.endClock(shift: shift) }
        let word = LyricWordEdit(text: "", start: start.flatMap { $0.isEmpty ? nil : $0 } ?? clock)
        words.append(word)
        return word.id
    }

    func validatedLyric() throws(Validation) -> Lyric {
        guard let time, (0...86_400).contains(time) else { throw .lineTime }
        let translated = translation.trimmingCharacters(in: .whitespacesAndNewlines)
        let savedTranslation = if let previous, translation == (previous.translation ?? "") {
            previous.translation
        } else {
            translated.isEmpty ? nil : translated
        }
        if !wordTiming {
            let text = original.trimmingCharacters(in: .whitespacesAndNewlines)
            guard !text.isEmpty else { throw .original }
            let savedText = previous?.text == original ? original : text
            return Lyric(time: time, text: savedText, translation: savedTranslation)
        }
        guard !words.isEmpty else { throw .emptyWords }
        var parsed: [Lyric.Word] = []
        for (index, word) in words.enumerated() {
            guard let value = word.lyric(shift: shift) else { throw .word(index) }
            parsed.append(value)
        }
        let hasTimeEdits = parsed.map(\.time) != previous?.words.map { max(0, $0.time + shift) }
        if hasTimeEdits {
            for index in parsed.indices.dropFirst() where parsed[index].time < parsed[index - 1].time {
                throw .wordOrdering(index)
            }
        }
        let texts = parsed.map(\.text)
        let text = if let previous, texts == previous.words.map(\.text) { previous.text }
                   else { texts.joined() }
        guard !text.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty else { throw .original }
        return Lyric(time: time, text: text, translation: savedTranslation, words: parsed)
    }
}

struct LyricLibraryEditor: View {
    let track: Track
    let selectionChanged: Bool
    let save: ([Lyric]) -> Bool
    let close: () -> Void
    @State private var rows: [LyricEditRow]
    @State private var expanded: UUID?
    @State private var error: String?
    @FocusState private var wordFocus: UUID?

    init(entry: LyricLibraryEntry, selectionChanged: Bool, save: @escaping ([Lyric]) -> Bool, close: @escaping () -> Void) {
        track = entry.track
        self.selectionChanged = selectionChanged
        self.save = save
        self.close = close
        _rows = State(initialValue: entry.lines.isEmpty ? [LyricEditRow()] : entry.lines.map { LyricEditRow($0) })
    }

    var body: some View {
        VStack(alignment: .leading, spacing: 9) {
            HStack(spacing: 8) {
                Text("编辑歌词").font(.headline)
                Spacer(minLength: 8)
                Button("取消", action: close).keyboardShortcut(.cancelAction)
                Button("保存", action: commit).keyboardShortcut(.defaultAction).buttonStyle(.glassProminent)
            }.buttonStyle(.glass).controlSize(.small)
            Text(selectionChanged ? "保存或取消后会显示所选歌曲。" : "时间格式为 分:秒.毫秒。展开逐字可编辑每段的文本与起止时间。")
                .font(.caption).foregroundStyle(.secondary).fixedSize(horizontal: false, vertical: true)
            ScrollViewReader { reader in
                ScrollView {
                    LazyVStack(spacing: 8) {
                        ForEach($rows) { $row in
                            lineEditor($row).id(row.id)
                        }
                    }
                    .padding(2).padding(.trailing, 10)
                    .nativeOverlayScrollIndicator()
                }
                .scrollBounceBehavior(.basedOnSize)
                HStack(spacing: 12) {
                    Button("添加一行", systemImage: "plus") {
                        let row = LyricEditRow(at: rows.last.flatMap(\.time).map { min(86_400, $0 + 2) } ?? 0)
                        rows.append(row)
                        Task { @MainActor in reader.scrollTo(row.id, anchor: .bottom) }
                    }.buttonStyle(.glass)
                    if let error { Text(error).font(.caption).foregroundStyle(.red) }
                }
            }
        }
        .padding(.horizontal, 17).padding(.vertical, 10)
        .frame(maxWidth: .infinity, maxHeight: .infinity, alignment: .topLeading)
        .onExitCommand(perform: close)
    }

    private func lineEditor(_ row: Binding<LyricEditRow>) -> some View {
        let line = row.wrappedValue
        let opened = expanded == line.id
        return VStack(alignment: .leading, spacing: 7) {
            HStack(alignment: .top, spacing: 8) {
                clockField("行时间", text: row.clock)
                VStack(alignment: .leading, spacing: 4) {
                    if line.wordTiming {
                        Text(line.wordText.isEmpty ? "逐字原文" : line.wordText)
                            .lineLimit(opened ? 3 : 1)
                            .frame(maxWidth: .infinity, alignment: .leading)
                            .modifier(LibraryEditorField())
                            .accessibilityLabel("原文").accessibilityValue(line.wordText)
                    } else {
                        TextField("原文", text: row.original, axis: .vertical).lineLimit(1...3)
                            .modifier(LibraryEditorField())
                    }
                    TextField("译文", text: row.translation, axis: .vertical).lineLimit(1...3)
                        .modifier(LibraryEditorField())
                }
                if line.wordTiming {
                    Button {
                        expanded = opened ? nil : line.id
                    } label: {
                        Label("逐字", systemImage: opened ? "chevron.up" : "chevron.down")
                    }
                    .buttonStyle(.glass)
                    .accessibilityLabel(opened ? "收起逐字编辑" : "展开逐字编辑")
                    .accessibilityValue("\(line.words.count) 段")
                } else {
                    Button("改为逐字") {
                        row.wrappedValue.switchTiming()
                        expanded = line.id
                    }.buttonStyle(.glass)
                }
                Button {
                    rows.removeAll { $0.id == line.id }
                    if opened { expanded = nil }
                } label: {
                    Image(systemName: "minus").frame(width: 16, height: 16)
                }.buttonStyle(.glass).accessibilityLabel("移除这一行")
            }
            if opened && line.wordTiming {
                VStack(alignment: .leading, spacing: 6) {
                    HStack(spacing: 8) {
                        Text("开始").frame(width: 88, alignment: .leading)
                        Text("结束").frame(width: 88, alignment: .leading)
                        Text("词或字（空格会保留）")
                    }.font(.caption).foregroundStyle(.secondary)
                    ForEach(row.words) { word in
                        wordEditor(word, shift: line.shift) {
                            let id = word.wrappedValue.id
                            row.wrappedValue.words.removeAll { $0.id == id }
                        }
                    }
                    HStack {
                        Button("添加一段", systemImage: "plus") {
                            wordFocus = row.wrappedValue.appendWord()
                        }
                        Spacer()
                        Button("改为逐行") {
                            row.wrappedValue.switchTiming()
                            expanded = nil
                        }
                    }.buttonStyle(.glass)
                }
                .padding(10)
                .background(.primary.opacity(0.04), in: RoundedRectangle(cornerRadius: 11))
            }
        }.controlSize(.small)
    }

    private func wordEditor(_ word: Binding<LyricWordEdit>, shift: Double, remove: @escaping () -> Void) -> some View {
        HStack(spacing: 8) {
            clockField("开始时间", text: Binding(
                get: { word.wrappedValue.startClock(shift: shift) }, set: { word.wrappedValue.start = $0 }))
            clockField("结束时间", text: Binding(
                get: { word.wrappedValue.endClock(shift: shift) }, set: { word.wrappedValue.end = $0 }))
            TextField("词或字", text: word.text)
                .modifier(LibraryEditorField())
                .focused($wordFocus, equals: word.wrappedValue.id)
            Button(action: remove) {
                Image(systemName: "minus").frame(width: 16, height: 16)
            }.buttonStyle(.glass).accessibilityLabel("移除这一段")
        }
    }

    private func clockField(_ label: String, text: Binding<String>) -> some View {
        TextField(label, text: text).font(.body.monospacedDigit()).modifier(LibraryEditorField())
            .frame(width: 88)
    }

    private func commit() {
        guard !rows.isEmpty else {
            error = "至少保留一行歌词。"
            return
        }
        var completed: [Lyric] = []
        var seenTimes: Set<Double> = []
        for (index, row) in rows.enumerated() {
            do {
                let lyric = try row.validatedLyric()
                guard seenTimes.insert(lyric.time).inserted else {
                    error = "第 \(index + 1) 行与另一行的时间重复。"
                    return
                }
                completed.append(lyric)
            } catch {
                self.error = "第 \(index + 1) 行：\(error.message)"
                return
            }
        }
        let ordered = completed.sorted { $0.time < $1.time }
        guard Lyric.hasSyncedVocals(in: ordered, for: track, acceptsManualText: true) else {
            error = "至少需要一行位于歌曲时长内的同步歌词。"
            return
        }
        if save(ordered) { close() }
        else { error = "无法写入歌词，请检查文件大小或磁盘空间。" }
    }
}

private struct LibraryEditorField: ViewModifier {
    func body(content: Content) -> some View {
        content.textFieldStyle(.plain).padding(.horizontal, 7).padding(.vertical, 5)
            .glassEffect(.regular.tint(.primary.opacity(0.055)), in: .rect(cornerRadius: 7))
    }
}
