import AppKit
import CoreText
import SwiftUI

struct AttachedLyricsBackdrop: View {
    let music: MusicModel
    let appearance: AppearanceModel

    var body: some View {
        MusicView(model: music, appearance: appearance, surface: .attachedBackdrop, toggle: {})
    }
}

@MainActor @Observable
final class AttachedLyricsHeader {
    enum Item: Hashable {
        case trigger, options, volumeDown, volumeUp, player, source

        var adjustsVolume: Bool { self == .volumeDown || self == .volumeUp }
    }

    private static let restingOpacity = 0.62
    private static let hoveredOpacity = 0.88
    private static let restingTint = labelTint(opacity: restingOpacity)
    private static let hoveredTint = labelTint(opacity: hoveredOpacity)

    var hovered: Item?
    var pressed: Item?
    @ObservationIgnored var sourceBounds: [Item: CGRect] = [:]

    static func coverColor(music: MusicModel, appearance: AppearanceModel) -> NSColor? {
        guard appearance.coverTint, music.notchLyrics.useCoverColors, music.artwork != nil,
              let cover = music.colors.first else { return nil }
        return NSColor(cover).usingColorSpace(.sRGB)
    }

    func tint(for item: Item, cover: NSColor?) -> NSColor {
        let highlighted = hovered == item
        guard let cover else { return highlighted ? Self.hoveredTint : Self.restingTint }
        return Self.labelTint(opacity: highlighted ? Self.hoveredOpacity : Self.restingOpacity, cover: cover)
    }

    func backgroundOpacity(for item: Item) -> Double {
        guard hovered == item else { return 0 }
        return pressed == item ? 0.17 : 0.08
    }

    static func lyricSource(for music: MusicModel) -> String {
        guard !music.lyricsLoading, !music.lyrics.isEmpty,
              let provider = lyricProvider(for: music) else { return music.lyricsCaption }
        return provider
    }

    static func lyricSourceHint(for music: MusicModel) -> String {
        if music.lyricsLoading && music.activeLyricCandidateID == nil { return "歌词来源 · 查找歌词中" }
        guard !music.lyrics.isEmpty else { return "歌词来源 · \(music.lyricsCaption)" }

        let original = lyricProvider(for: music) ?? "未标明"
        let translation: String
        if let source = music.activeLyricTranslationSource {
            translation = "\(source.title)（补入）"
        } else if music.lyrics.contains(where: { $0.translation?.isEmpty == false }) {
            translation = original
        } else {
            translation = "暂无译文"
        }
        return "歌词来源 · \(original)\n译文来源 · \(translation)"
    }

    private static func lyricProvider(for music: MusicModel) -> String? {
        let provider = music.lyricsProvider?.trimmingCharacters(in: .whitespacesAndNewlines)
        return provider?.isEmpty == false ? provider : nil
    }

    private static func labelTint(opacity: Double, cover: NSColor? = nil) -> NSColor {
        NSColor(name: nil) { appearance in
            var color = NSColor.labelColor
            appearance.performAsCurrentDrawingAppearance {
                let ink = if let cover {
                    NSColor.labelColor.blended(withFraction: 0.36, of: cover) ?? .labelColor
                } else {
                    NSColor.labelColor
                }
                color = ink.withAlphaComponent(opacity)
            }
            return color
        }
    }
}

struct AttachedLyricsSources: View {
    private nonisolated static let sourceSpace = "lyrics-header-sources"

    let music: MusicModel
    let appearance: AppearanceModel
    let header: AttachedLyricsHeader
    @Environment(\.accessibilityReduceMotion) private var reduceMotion

    private func ink(for item: AttachedLyricsHeader.Item) -> Color {
        let cover = AttachedLyricsHeader.coverColor(music: music, appearance: appearance)
        return Color(nsColor: header.tint(for: item, cover: cover))
    }

    var body: some View {
        if let track = music.track {
            VStack(alignment: .trailing, spacing: 0) {
                sourceLine(track.player.name, item: .player)
                sourceLine(AttachedLyricsHeader.lyricSource(for: music), item: .source)
            }
            .lineLimit(1)
            .minimumScaleFactor(0.8)
            // Align the source labels' visible letterforms with the neighboring symbols.
            .padding(.top, 1.5)
            .accessibilityElement(children: .combine)
            .frame(maxWidth: .infinity, maxHeight: .infinity, alignment: .trailing)
            .coordinateSpace(name: Self.sourceSpace)
        }
    }

    private func sourceLine(_ title: String, item: AttachedLyricsHeader.Item) -> some View {
        appearance.styledText(title, size: 9.5)
            .foregroundStyle(ink(for: item))
            .padding(.horizontal, 4)
            .background { keyBackground(item, radius: 4) }
            .onGeometryChange(for: CGRect.self, of: { $0.frame(in: .named(Self.sourceSpace)) }) { bounds in
                header.sourceBounds[item] = bounds
            }
    }

    private func keyBackground(_ item: AttachedLyricsHeader.Item, radius: CGFloat) -> some View {
        RoundedRectangle(cornerRadius: radius).fill(.primary.opacity(header.backgroundOpacity(for: item)))
            .animation(appearance.animation && !reduceMotion ? .easeOut(duration: 0.13) : nil,
                       value: header.backgroundOpacity(for: item))
    }

}

struct AttachedLyricsStyle {
    static let adjacentOpacity = 0.70

    let font: NSFont
    let chineseFont: NSFont
    let translationFont: NSFont
    let translationChineseFont: NSFont
    let original: NSColor
    let unsung: NSColor
    let sung: NSColor
    let translation: NSColor
    let shadow: Double
    let options: NotchLyricsOptions
    let hdr: Bool

    @MainActor
    init(music: MusicModel, appearance: AppearanceModel, dark: Bool) {
        options = music.notchLyrics
        let neutral: CGFloat = dark ? 1 : 0.03
        hdr = dark && options.usesHDRLyricHighlights
        let ink = NSColor(srgbRed: neutral, green: neutral, blue: neutral, alpha: 1)
        original = ink.withAlphaComponent(0.72)
        unsung = original.withAlphaComponent(original.alphaComponent * Self.adjacentOpacity)
        sung = ink
        translation = ink.withAlphaComponent(original.alphaComponent * options.translationOpacity)
        shadow = options.textShadow(dark: dark)
        let typography = LyricTypography(options: options, appearance: appearance)
        let size = appearance.lyricsSize
        (font, chineseFont) = typography.fonts(size: size)
        (translationFont, translationChineseFont) = typography.fonts(size: size * 0.61, weight: .medium)
    }

    func translationInk(sungFraction: Double) -> NSColor {
        translation.withAlphaComponent(translation.alphaComponent + 0.085 * sungFraction)
    }

}

struct LyricTypography: Equatable {
    private let westernFamily: String?
    private let chineseFamily: String?
    private let weight: NSFont.Weight

    @MainActor
    init(options: NotchLyricsOptions, appearance: AppearanceModel) {
        westernFamily = options.westernFontFamily.isEmpty ? appearance.fontFamily(forChinese: false) : options.westernFontFamily
        chineseFamily = options.chineseFontFamily.isEmpty ? appearance.fontFamily(forChinese: true) : options.chineseFontFamily
        weight = switch options.fontWeight {
        case ...400: .regular
        case ...500: .medium
        case ...600: .semibold
        case ...700: .bold
        default: .heavy
        }
    }

    @MainActor
    func fonts(size: CGFloat, weight override: NSFont.Weight? = nil) -> (western: NSFont, chinese: NSFont) {
        let weight = override ?? weight
        return (Self.font(size: size, weight: weight, family: westernFamily),
                Self.font(size: size, weight: weight, family: chineseFamily))
    }

    @MainActor
    private static func font(size: CGFloat, weight: NSFont.Weight, family: String?) -> NSFont {
        let system = NSFont.systemFont(ofSize: size, weight: weight)
        guard let family else { return system }
        let selected = NSFont(name: family, size: size)
        let requestedWeight = NSFontManager.shared.weight(of: system)
        return NSFontManager.shared.font(withFamily: selected?.familyName ?? family, traits: [],
                                         weight: requestedWeight, size: size) ?? selected ?? system
    }

    static func attributedText(_ text: String, westernFont: NSFont, chineseFont: NSFont?) -> NSAttributedString {
        let attributes: [NSAttributedString.Key: Any] = [
            .font: westernFont, NSAttributedString.Key(kCTForegroundColorFromContextAttributeName as String): true
        ]
        guard let chineseFont, chineseFont != westernFont else {
            return NSAttributedString(string: text, attributes: attributes)
        }
        let result = NSMutableAttributedString(string: text, attributes: attributes)
        var start = 0
        for (run, isChinese) in AppearanceModel.scriptRuns(text) {
            let count = run.utf16.count
            if isChinese { result.addAttribute(.font, value: chineseFont, range: NSRange(location: start, length: count)) }
            start += count
        }
        return result
    }
}

struct AttachedLyricRow: View {
    let line: Lyric
    let track: Track
    let offset: Double
    let calibration: Double
    let width: CGFloat
    let style: AttachedLyricsStyle
    let active: Bool
    let frozen: Bool

    var body: some View {
        let original = AttachedLyricDrawing(text: line.text, words: line.words, font: style.font, width: width,
                                            balances: style.options.splitLongLines, chineseFont: style.chineseFont)
        let translated = (style.options.showTranslation ? line.translation : nil).flatMap { text in
            text.isEmpty ? nil : AttachedLyricDrawing(text: text, words: [], font: style.translationFont,
                                                       width: width, balances: style.options.splitLongLines,
                                                       chineseFont: style.translationChineseFont)
        }
        let tracksWords = active && style.options.karaoke && original.hasWordTiming

        VStack(alignment: .leading, spacing: 4) {
            if tracksWords {
                TimelineView(.animation(minimumInterval: 1.0 / 30, paused: !track.playing || frozen)) { _ in
                    let position = track.lyricPosition(offset: calibration + offset, frozen: frozen)
                    VStack(alignment: .leading, spacing: 4) {
                        row(original, color: style.sung, unsung: style.unsung, focus: position)
                        if let translated {
                            row(translated, color: style.translationInk(sungFraction: original.sungFraction(at: position)))
                        }
                    }
                }
            } else {
                row(original, color: style.original)
                if let translated { row(translated, color: style.translationInk(sungFraction: active ? 1 : 0)) }
            }
        }
        .frame(maxWidth: .infinity, alignment: .leading)
        .accessibilityElement(children: .ignore)
        .accessibilityLabel([line.text, style.options.showTranslation ? line.translation : nil].compactMap { $0 }.joined(separator: "\n"))
    }

    private func row(_ drawing: AttachedLyricDrawing, color: NSColor, unsung: NSColor? = nil,
                     focus: Double? = nil) -> some View {
        Canvas { context, size in
            context.withCGContext { cg in
                drawing.draw(in: cg, height: size.height, color: color, unsung: unsung, position: focus,
                             shadow: style.shadow)
            }
        }
        .frame(height: drawing.height)
        .overlay {
            if style.hdr, let focus, drawing.sungFraction(at: focus) > 0 {
                ExpandedHDRLyricInk(drawing: drawing, size: CGSize(width: width, height: drawing.height),
                                    color: color, position: focus)
                    .allowsHitTesting(false)
                    .accessibilityHidden(true)
            }
        }
        .accessibilityHidden(true)
    }
}

struct AttachedLyricDrawing {
    private struct Fragment {
        let line: CTLine
        let ascent: CGFloat
        let height: CGFloat
        let slices: [WordSlice]
    }

    private struct WordSlice {
        let time: Double
        let duration: Double
        let start: Double
        let span: Double
        let left: CGFloat
        let right: CGFloat

        var width: CGFloat { abs(right - left) }

        func fraction(at position: Double) -> Double {
            guard position >= time else { return 0 }
            let spoken = duration > 0 ? (position - time) / duration : 1
            return min(1, max(0, (spoken - start) / span))
        }

        func clip(at position: Double, y: CGFloat, height: CGFloat) -> CGRect? {
            let filledWidth = width * fraction(at: position)
            guard filledWidth > 0 else { return nil }
            return CGRect(x: left <= right ? left : left - filledWidth, y: y, width: filledWidth, height: height)
        }
    }

    private let fragments: [Fragment]
    let height: CGFloat
    var firstBaseline: CGFloat { fragments.first?.ascent ?? 0 }
    var hasWordTiming: Bool { fragments.contains { !$0.slices.isEmpty } }

    func sungFraction(at position: Double) -> Double {
        var sung = 0.0
        var total = 0.0
        for fragment in fragments {
            for slice in fragment.slices {
                total += slice.width
                sung += slice.width * slice.fraction(at: position)
            }
        }
        return total > 0 ? sung / total : 0
    }

    init(text: String, words: [Lyric.Word], font: NSFont, width: CGFloat, balances: Bool,
         chineseFont: NSFont? = nil) {
        let source = words.isEmpty ? text : words.map(\.text).joined()
        // Flatten line breaks without changing their UTF-16 length before remapping the word ranges.
        let prepared = LyricDisplayText(source.components(separatedBy: .newlines).joined(separator: " "))
        let display = prepared.string
        let marked = LyricTypography.attributedText(display, westernFont: font, chineseFont: chineseFont)
        let typesetter = CTTypesetterCreateWithAttributedString(marked)
        let whole = CTLineCreateWithAttributedString(marked)
        let fullWidth = CTLineGetTypographicBounds(whole, nil, nil, nil)
        let ranges: [NSRange]
        if fullWidth <= width {
            ranges = [.init(location: 0, length: marked.length)]
        } else if balances, let balanced = NotchLyricBreaks.ranges(for: display, using: typesetter, width: width, fullWidth: fullWidth) {
            ranges = balanced
        } else {
            var wrapped: [NSRange] = []
            var start = 0
            while start < marked.length {
                let length = max(1, CTTypesetterSuggestLineBreak(typesetter, start, width))
                let end = min(marked.length, start + length)
                wrapped.append(.init(location: start, length: end - start))
                start = end
            }
            ranges = wrapped
        }

        var cursor = 0
        let timedWords = prepared.timedWords(words).map { word in
            let length = word.text.utf16.count
            defer { cursor += length }
            return (word, NSRange(location: cursor, length: length))
        }
        fragments = ranges.map { range in
            let line = CTTypesetterCreateLine(typesetter, CFRange(location: range.location, length: range.length))
            var ascent: CGFloat = 0
            var descent: CGFloat = 0
            let lineWidth = CTLineGetTypographicBounds(line, &ascent, &descent, nil)
            let baseline = max(font.ascender, ascent)
            let lineHeight = ceil(baseline + max(-font.descender, descent)) + 2
            let slices = timedWords.compactMap { word, wordRange -> WordSlice? in
                let overlap = NSIntersectionRange(range, wordRange)
                guard overlap.length > 0, word.time.isFinite, word.duration.isFinite else { return nil }
                let start = Double(overlap.location - wordRange.location) / Double(wordRange.length)
                let span = Double(overlap.length) / Double(wordRange.length)
                let left = min(lineWidth, max(0, CTLineGetOffsetForStringIndex(line, overlap.location, nil)))
                let right = min(lineWidth, max(0, CTLineGetOffsetForStringIndex(line, NSMaxRange(overlap), nil)))
                return WordSlice(time: word.time, duration: word.duration, start: start, span: span, left: left, right: right)
            }
            return Fragment(line: line, ascent: baseline, height: lineHeight, slices: slices)
        }
        height = max(1, fragments.reduce(0) { $0 + $1.height })
    }

    func draw(in context: CGContext, height: CGFloat, color: NSColor, unsung: NSColor?, position: Double?, shadow: Double) {
        context.saveGState()
        context.translateBy(x: 0, y: height)
        context.scaleBy(x: 1, y: -1)
        context.textMatrix = .identity
        var y = height
        for fragment in fragments {
            let origin = CGPoint(x: 0, y: y - fragment.ascent)
            y -= fragment.height
            let timed = position != nil && !fragment.slices.isEmpty
            context.saveGState()
            if shadow > 0 {
                context.setShadow(offset: .init(width: 0, height: -1), blur: 5 * shadow,
                                  color: NSColor.black.withAlphaComponent(0.42 * shadow).cgColor)
            }
            context.setFillColor((timed ? unsung ?? color : color).cgColor)
            context.textPosition = origin
            CTLineDraw(fragment.line, context)
            context.restoreGState()
            if let position {
                paintSung(fragment, baseline: origin, bottom: y, in: context, ink: color.cgColor, at: position)
            }
        }
        context.restoreGState()
    }

    func drawSung(in context: CGContext, ink: CGColor, at position: Double) {
        context.textMatrix = .identity
        var y = height
        for fragment in fragments {
            let origin = CGPoint(x: 0, y: y - fragment.ascent)
            y -= fragment.height
            paintSung(fragment, baseline: origin, bottom: y, in: context, ink: ink, at: position)
        }
    }

    private func paintSung(_ fragment: Fragment, baseline: CGPoint, bottom: CGFloat, in context: CGContext,
                           ink: CGColor, at position: Double) {
        let clips = fragment.slices.compactMap { $0.clip(at: position, y: bottom, height: fragment.height) }
        guard !clips.isEmpty else { return }
        context.saveGState()
        context.clip(to: clips)
        context.setFillColor(ink)
        context.textPosition = baseline
        CTLineDraw(fragment.line, context)
        context.restoreGState()
    }
}

private struct ExpandedHDRLyricInk: NSViewRepresentable {
    let drawing: AttachedLyricDrawing
    let size: CGSize
    let color: NSColor
    let position: Double

    func makeNSView(context: Context) -> HDRLyricView { HDRLyricView(frame: .zero) }

    func updateNSView(_ view: HDRLyricView, context: Context) {
        view.render(size: size, color: color) { context, ink in
            drawing.drawSung(in: context, ink: ink, at: position)
        }
    }
}

struct AttachedPlaybackProgress: View {
    private struct SeekRequest {
        let id = UUID()
        let position: Double
        let delay: Duration
    }

    let track: Track
    let music: MusicModel
    let appearance: AppearanceModel
    @ViewState private var seekRequest: SeekRequest?
    @ViewState private var rejected = false
    @ViewState private var thumbHoverX: CGFloat?
    @GestureState private var dragPosition: Double?

    private var draft: Double? { dragPosition ?? seekRequest?.position }

    var body: some View {
        let duration = track.duration.isFinite ? max(0, track.duration) : 0
        let canSeek = duration > 0 && track.systemSession && !track.estimatedPosition && SystemMedia.executable != nil
        return TimelineView(.animation(minimumInterval: 0.06, paused: !track.playing)) { _ in
            let now = Date.now
            let position = track.displayPosition(at: now, calibration: music.playbackCalibration)
            let rail = MusicProgressRail(position: draft ?? position, duration: duration, estimated: track.estimatedPosition,
                                         appearance: appearance, colors: music.colors, showsThumb: canSeek,
                                         thumbHoverX: thumbHoverX, thumbPressed: dragPosition != nil) {
                if canSeek {
                    dragArea(position: track.position(at: now), duration: duration)
                }
            }
            Group {
                if canSeek {
                    rail
                } else {
                    rail.accessibilityElement(children: .ignore)
                        .accessibilityLabel("播放进度")
                        .accessibilityValue(rail.timeLabel)
                }
            }
            .overlay(alignment: .topLeading) {
                if rejected && dragPosition == nil {
                    Text("播放器未确认跳转").font(.caption2).foregroundStyle(.secondary)
                        .accessibilityAddTraits(.updatesFrequently)
                        .offset(y: -12)
                        .allowsHitTesting(false)
                }
            }
        }
        .task(id: seekRequest?.id) {
            guard let request = seekRequest else { return }
            await performSeek(request)
        }
    }

    private func dragArea(position: Double, duration: Double) -> some View {
        GeometryReader { geometry in
            let width = geometry.size.width
            Color.clear
                .contentShape(Rectangle())
                .onContinuousHover { phase in
                    switch phase {
                    case .active(let location): thumbHoverX = location.x
                    case .ended: thumbHoverX = nil
                    }
                }
                .gesture(DragGesture(minimumDistance: 0)
                    .updating($dragPosition) { value, position, _ in
                        position = seekPosition(at: value.location.x, width: width, duration: duration)
                    }
                    .onEnded { value in
                        requestSeek(to: seekPosition(at: value.location.x, width: width, duration: duration), after: .zero)
                    })
                .focusable()
                .focusEffectDisabled()
                .onKeyPress(.leftArrow) {
                    requestSeek(to: max(0, (draft ?? position) - 5))
                    return .handled
                }
                .onKeyPress(.rightArrow) {
                    requestSeek(to: min(duration, (draft ?? position) + 5))
                    return .handled
                }
                .accessibilityRepresentation {
                    Slider(value: Binding(get: { draft ?? position }, set: { requestSeek(to: $0) }), in: 0...duration)
                        .accessibilityLabel("播放进度")
                        .accessibilityIdentifier("LumiBar.attached.progress")
                }
                .help(draft == nil ? "拖动调整播放进度" : "")
        }
    }

    private func seekPosition(at x: CGFloat, width: CGFloat, duration: Double) -> Double {
        let fraction = width > 0 ? min(1, max(0, x / width)) : 0
        return duration * fraction
    }

    private func requestSeek(to position: Double, after delay: Duration = .milliseconds(135)) {
        rejected = false
        seekRequest = SeekRequest(position: position, delay: delay)
    }

    private func performSeek(_ request: SeekRequest) async {
        if request.delay > .zero {
            do { try await Task.sleep(for: request.delay) } catch { return }
        }
        guard !Task.isCancelled, seekRequest?.id == request.id else { return }
        let confirmed = await music.control(.seek(request.position), trackKey: track.key, expectedPID: track.pid)
        guard !Task.isCancelled, seekRequest?.id == request.id else { return }
        seekRequest = nil
        rejected = !confirmed && music.track?.key == track.key && music.track?.pid == track.pid
    }

}
