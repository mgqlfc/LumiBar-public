import AppKit
import SwiftUI

struct LibraryGlassMenuIcon: View {
    let symbol: String
    let size: CGFloat
    var selected = false

    var body: some View {
        Image(systemName: symbol)
            .font(.system(size: 13, weight: .semibold)).foregroundStyle(.primary)
            .frame(width: size, height: size).contentShape(Circle())
            .glassEffect(.regular.tint(selected ? Color.accentColor.opacity(0.44) : nil).interactive(), in: Circle())
    }
}

struct LibraryMenu<Content: View>: View {
    @ViewBuilder let content: Content

    var body: some View {
        VStack(spacing: 2) { content }
            .frame(minWidth: 150).fixedSize(horizontal: true, vertical: false).padding(6)
    }
}

struct LibraryMenuItem: View {
    let title: String
    let symbol: String?
    let destructive: Bool
    let selected: Bool
    let action: () -> Void
    @Environment(\.isEnabled) private var enabled

    init(_ title: String, symbol: String?, destructive: Bool = false, selected: Bool = false, action: @escaping () -> Void) {
        self.title = title
        self.symbol = symbol
        self.destructive = destructive
        self.selected = selected
        self.action = action
    }

    var body: some View {
        Button(action: action) {
            HStack(spacing: 7) {
                Group {
                    if let symbol { Image(systemName: symbol) }
                    else { Color.clear.frame(height: 1) }
                }.frame(width: 16).foregroundStyle(selected ? Color.accentColor : .primary).accessibilityHidden(true)
                Text(title).lineLimit(1)
                Spacer(minLength: 12)
            }
            .font(.subheadline).foregroundStyle(destructive ? .red : .primary)
            .opacity(enabled ? 1 : 0.44)
            .padding(.horizontal, 7).frame(height: 29).contentShape(RoundedRectangle(cornerRadius: 8))
            .glassEffect(selected ? .regular.tint(Color.accentColor.opacity(0.26)).interactive() : .identity,
                         in: RoundedRectangle(cornerRadius: 8))
        }
        .buttonStyle(LibrarySegmentStyle())
        .accessibilityLabel(title)
    }
}

struct LibraryGlassDialog<Content: View>: View {
    @Environment(\.colorScheme) private var scheme
    @ViewBuilder let content: Content

    var body: some View {
        ZStack {
            Color.black.opacity(scheme == .dark ? 0.16 : 0.10)
                .accessibilityHidden(true)
            content
                .glassEffect(.regular, in: .rect(cornerRadius: 24))
                .accessibilityElement(children: .contain)
                .accessibilityAddTraits(.isModal)
        }
    }
}

struct LibraryGlassSegments<Choice: Hashable>: View {
    @Binding var selection: Choice
    let choices: [Choice]
    var stretches = false
    let title: (Choice) -> String
    @Namespace private var motion
    @Environment(\.accessibilityReduceMotion) private var reduceMotion

    var body: some View {
        HStack(spacing: 2) {
            ForEach(choices, id: \.self) { value in
                let selected = selection == value
                Button { selection = value } label: {
                    Text(title(value)).font(.caption.weight(selected ? .semibold : .medium))
                        .lineLimit(1).minimumScaleFactor(stretches ? 0.72 : 1).padding(.horizontal, stretches ? 3 : 9)
                        .frame(maxWidth: stretches ? .infinity : nil)
                        .frame(height: 23)
                        .background {
                            if selected {
                                Capsule().fill(.clear)
                                    .glassEffect(.regular.tint(Color.accentColor.opacity(0.44)), in: Capsule())
                                    .matchedGeometryEffect(id: "selection", in: motion)
                            }
                        }
                        .contentShape(Capsule())
                }
                .buttonStyle(LibrarySegmentStyle())
                .accessibilityAddTraits(selected ? .isSelected : [])
            }
        }
        .padding(3)
        .glassEffect(.regular.interactive(), in: Capsule())
        .animation(reduceMotion ? nil : .smooth(duration: 0.2), value: selection)
    }
}

private struct LibrarySegmentStyle: ButtonStyle {
    @State private var hovered = false

    func makeBody(configuration: Configuration) -> some View {
        configuration.label.background(.primary.opacity(configuration.isPressed ? 0.12 : hovered ? 0.045 : 0), in: Capsule())
            .onHover { hovered = $0 }
    }
}

struct LibraryArtwork: View {
    let image: NSImage?
    let size: CGFloat

    var body: some View {
        ZStack {
            if let image {
                Image(nsImage: image).resizable().aspectRatio(contentMode: .fill)
            } else {
                RoundedRectangle(cornerRadius: size * 0.19).fill(.primary.opacity(0.08))
                Image(systemName: "music.note").font(.system(size: size * 0.33)).foregroundStyle(.secondary)
            }
        }.frame(width: size, height: size).clipShape(.rect(cornerRadius: size * 0.19)).accessibilityHidden(true)
    }
}

struct LibraryAlbumLight: View {
    let artwork: NSImage?
    let archived: Data?
    let sidebarWidth: CGFloat
    @State private var palette: [Color] = []
    @Environment(\.colorScheme) private var scheme

    private struct ImageKey: Hashable {
        let live: ObjectIdentifier?
        let archived: Data?
    }

    var body: some View {
        GeometryReader { proxy in
            if let first = palette.first {
                let leading = min(proxy.size.width, max(0, sidebarWidth - 28))
                let width = max(0, proxy.size.width - leading)
                if width > 0 {
                    let second = palette.last ?? first
                    ZStack {
                        RadialGradient(colors: [first.opacity(scheme == .dark ? 0.38 : 0.25), .clear],
                                       center: UnitPoint(x: 0.18, y: 0.02), startRadius: 12, endRadius: width * 0.89)
                        RadialGradient(colors: [second.opacity(scheme == .dark ? 0.32 : 0.21), .clear],
                                       center: UnitPoint(x: 0.84, y: 0.13), startRadius: 0, endRadius: width * 0.73)
                    }
                    .mask {
                        LinearGradient(stops: [.init(color: .clear, location: 0),
                                               .init(color: .white, location: 0.14), .init(color: .white, location: 1)],
                                       startPoint: .leading, endPoint: .trailing)
                            .mask {
                                LinearGradient(stops: [.init(color: .white, location: 0),
                                                       .init(color: .white, location: 0.24),
                                                       .init(color: .clear, location: 1)],
                                               startPoint: .top, endPoint: .bottom)
                            }
                    }
                    .frame(width: width, height: min(340, proxy.size.height))
                    .offset(x: leading)
                }
            }
        }
        .allowsHitTesting(false)
        .accessibilityHidden(true)
        .task(id: ImageKey(live: artwork.map(ObjectIdentifier.init), archived: archived)) {
            palette = (artwork ?? archived.flatMap(NSImage.init(data:))).map(coverColors) ?? []
        }
    }
}

struct LibraryBadge: View {
    enum Kind {
        case source(MusicModel.LyricSource)
        case wordTiming
        case translation(MusicModel.LyricSource?)
        case locked
        case edited
        case unassigned(hasLyrics: Bool)

        var title: String {
            switch self {
            case .source(let source): source.title
            case .wordTiming: "逐字"
            case .translation(let source): source.map { "译文 · \($0.title)" } ?? "译文"
            case .locked: "已锁定"
            case .edited: "手动编辑"
            case .unassigned(let hasLyrics): hasLyrics ? "本地时间轴" : "暂无歌词"
            }
        }

        var appearance: (symbol: String, day: UInt32, night: UInt32) {
            switch self {
            case .wordTiming: ("text.word.spacing", 0x145493, 0x8BC8FF)
            case .translation: ("translate", 0x126653, 0x82DEC8)
            case .locked: ("lock", 0x6A4A18, 0xEDC48A)
            case .edited: ("pencil", 0x6A4A18, 0xEDC48A)
            case .unassigned: ("text.alignleft", 0x575960, 0xC6C9D4)
            case .source(let source):
                switch source {
                case .automatic: ("wand.and.stars", 0x575960, 0xC6C9D4)
                case .netease: ("music.note", 0x9E2C43, 0xFF9AAC)
                case .amll: ("text.bubble", 0x654390, 0xCFB7FF)
                case .qqMusic: ("music.note.list", 0x21653F, 0x8EDDAB)
                case .kugou: ("headphones", 0x035E7D, 0x8CD9EE)
                case .kuwo: ("speaker.wave.2", 0x79541D, 0xDEC68C)
                case .lrclib: ("books.vertical", 0x474DA0, 0xABB5FF)
                case .megalobiz: ("doc.text", 0x6D4D44, 0xE5B5A7)
                case .migu: ("radio", 0x86580B, 0xE8BB72)
                case .soda: ("bolt", 0x772D82, 0xE7A7ED)
                case .deezer: ("heart", 0x673EA9, 0xCBB3FA)
                case .appleMusic: ("music.note", 0xA22843, 0xFF9BB7)
                case .local: ("internaldrive", 0x495F73, 0xB2CDD9)
                }
            }
        }
    }

    let kind: Kind
    var compact = false
    var iconOnly = false
    @Environment(\.colorScheme) private var scheme

    var body: some View {
        let style = kind.appearance
        let ink = Color.rgb(scheme == .dark ? style.night : style.day)
        HStack(spacing: 3) {
            Image(systemName: style.symbol).accessibilityHidden(true)
            if !iconOnly { Text(kind.title).lineLimit(1) }
        }
        .font(.system(size: compact ? 10 : 11, weight: .medium))
        .foregroundStyle(ink)
        .padding(.horizontal, compact ? 4 : 6)
        .frame(height: compact ? 15 : 20)
        .background(ink.opacity(0.045), in: Capsule())
        .overlay(Capsule().strokeBorder(ink.opacity(0.36), lineWidth: 0.6))
        .help(kind.title)
        .accessibilityLabel(kind.title)
    }
}

struct LibraryKaraokeLine: View {
    let lyric: Lyric
    let active: Bool
    let position: Double?
    let showTranslation: Bool
    let width: CGFloat
    let scale: CGFloat
    @State private var drawing: AttachedLyricDrawing?
    @Environment(\.colorScheme) private var scheme
    @Environment(\.accessibilityReduceMotion) private var reduceMotion

    private struct Shaping: Equatable {
        let text: String
        let words: [Lyric.Word]
        let width: CGFloat
    }

    var body: some View {
        let key = Shaping(text: lyric.text, words: lyric.words, width: width.rounded(.down))
        let ink = scheme == .dark ? NSColor.white : NSColor.black
        let dormantInk = ink.withAlphaComponent(scheme == .dark ? 0.65 : 0.57)
        let sungInk = Self.accent(for: scheme)
        let fraction = position.map { drawing?.sungFraction(at: $0) ?? 1 } ?? 0
        VStack(alignment: .leading, spacing: 5 * scale) {
            if let drawing {
                let baseline = drawing.firstBaseline
                Canvas { context, size in
                    context.withCGContext { cg in
                        drawing.draw(in: cg, height: size.height, color: active ? sungInk : dormantInk,
                                     unsung: dormantInk, position: position, shadow: 0)
                    }
                }
                .frame(height: drawing.height)
                .alignmentGuide(.firstTextBaseline) { _ in baseline }
                .accessibilityLabel(lyric.text)
            } else {
                Text(LyricDisplayText(lyric.text).string).font(.system(size: 13 * scale))
                    .foregroundStyle(Color(nsColor: active ? sungInk : dormantInk))
                    .animation(reduceMotion ? nil : .easeInOut(duration: 0.16), value: active)
            }
            if showTranslation, let translation = lyric.translation {
                let color = ink.blended(withFraction: 0.68 * fraction, of: sungInk) ?? ink
                Text(LyricDisplayText(translation).string).font(.system(size: 12 * scale))
                    .foregroundStyle(Color(nsColor: color.withAlphaComponent(0.48 + 0.39 * fraction)))
            }
        }
        .onChange(of: key, initial: true) { _, key in
            drawing = key.words.isEmpty ? nil : AttachedLyricDrawing(
                text: key.text, words: key.words, font: .systemFont(ofSize: 13 * scale),
                width: key.width, balances: false)
        }
    }

    static func accent(for scheme: ColorScheme) -> NSColor {
        let accent = NSColor.controlAccentColor
        let base: NSColor = scheme == .dark ? .white : .black
        return accent.blended(withFraction: scheme == .dark ? 0.56 : 0.21, of: base) ?? accent
    }
}
