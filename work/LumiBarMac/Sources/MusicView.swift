import SwiftUI

struct MusicView: View {
    static let entryWidth: CGFloat = 50
    static let hoverHeight: CGFloat = 47
    nonisolated static let detailsSize = CGSize(width: 452, height: 682)
    private static let entryControlSize: CGFloat = 20
    private static let entrySpacing: CGFloat = 2
    private static let hoverCoverSize: CGFloat = 34
    private static let hoverPadding: CGFloat = 8
    private static let hoverSpacing: CGFloat = 8
    private static let hoverTitleSize: CGFloat = 12
    private static let hoverTitleWeight: Font.Weight = .medium
    private static let hoverArtistSize: CGFloat = 11
    private static let hoverAlbumSize: CGFloat = 10

    static func hoverWidth(for track: Track, appearance: AppearanceModel) -> CGFloat {
        let titleWidth = appearance.textWidth(track.title, size: hoverTitleSize, weight: hoverTitleWeight)
        let artistWidth = appearance.textWidth(track.artist, size: hoverArtistSize)
        let albumWidth = appearance.textWidth(track.album, size: hoverAlbumSize)
        let textWidth = ceil(max(titleWidth, artistWidth, albumWidth))
        let chromeWidth = 2 * hoverPadding + hoverCoverSize + hoverSpacing
        return min(320, chromeWidth + max(64, textWidth + 4))
    }

    struct EntryPlayback: Equatable {
        let playing: Bool
        let highlighted: Bool
        let enabled: Bool
    }

    static func entryControlFrame(in frame: CGRect) -> CGRect {
        let size = entryControlSize
        let coverWidth = min(21, frame.height - 4)
        let x = frame.midX + (coverWidth + entrySpacing - size) / 2
        return CGRect(x: x, y: frame.midY - size / 2, width: size, height: size)
    }

    static func entryBands(from levels: [Double], response: Double) -> [Int] {
        let count = 5
        return (0..<count).map { index in
            let sample = levels.isEmpty ? 0 : levels[index * levels.count / count]
            return Int((pow(min(1, max(0, sample)), 1 / response) * 11).rounded())
        }
    }

    enum Surface {
        case entry(height: CGFloat, bands: [Int], playback: EntryPlayback?, lightTime: TimeInterval?)
        case hover, referenceLyrics, attachedLyrics, attachedBackdrop
    }
    let model: MusicModel
    let appearance: AppearanceModel
    let surface: Surface
    let toggle: () -> Void
    @Environment(\.menuBarCapsuleLayer) private var capsuleLayer
    @Environment(\.accessibilityReduceMotion) private var reduceMotion
    @Environment(\.colorScheme) private var colorScheme
    @ViewState private var isSendingPlayback = false
    @ViewState private var playbackMessage: String?

    var body: some View {
        switch surface {
        case .entry(let height, let bands, let playback, let lightTime):
            HStack(spacing: Self.entrySpacing) {
                cover(size: min(21, height - 4), compact: true)
                entryIndicator(bands: bands, playback: playback)
                    .frame(width: Self.entryControlSize, height: Self.entryControlSize)
            }
            .frame(width: Self.entryWidth, height: height)
            .opacity(capsuleLayer == .surface ? 0 : 1)
            .background {
                if capsuleLayer != .content { capsuleCoverLight(at: lightTime) }
            }
            .modifier(MenuBarCapsuleChrome(height: height))
        case .hover:
            if let track = model.track { hoverPanel(track) }
        case .referenceLyrics:
            if let track = model.track { lyricsPanel(track, attached: false) }
        case .attachedLyrics:
            if let track = model.track {
                lyricsPanel(track, attached: true)
            } else {
                Text("等待播放")
                    .font(.title3.weight(.medium))
                    .foregroundStyle(.secondary)
                    .frame(maxWidth: .infinity, maxHeight: .infinity)
            }
        case .attachedBackdrop:
            attachedBackdrop
        }
    }

    @ViewBuilder
    private func entryIndicator(bands: [Int], playback: EntryPlayback?) -> some View {
        if let playback {
            let restingOpacity = playback.playing ? 0.075 : 0
            let backgroundOpacity = playback.highlighted && playback.enabled ? 0.18 : restingOpacity
            Image(systemName: playback.playing ? "pause.fill" : "play.fill")
                .font(.system(size: 9, weight: .semibold))
                .foregroundStyle(.primary.opacity(playback.enabled ? 0.92 : 0.45))
                .frame(width: Self.entryControlSize, height: Self.entryControlSize)
                .background(.primary.opacity(backgroundOpacity), in: Circle())
        } else {
            Canvas { context, size in
                let barWidth: CGFloat = 2
                let step: CGFloat = 3.5
                let span = barWidth + CGFloat(max(0, bands.count - 1)) * step
                let inset = (size.width - span) / 2
                for (index, band) in bands.enumerated() {
                    let height = 2 + CGFloat(band)
                    let rect = CGRect(x: inset + CGFloat(index) * step, y: (size.height - height) / 2,
                                      width: barWidth, height: height)
                    context.fill(Path(roundedRect: rect, cornerRadius: barWidth / 2),
                                 with: .color(.primary.opacity(band == 0 ? 0.32 : 0.78)))
                }
            }.frame(width: 17, height: 15)
        }
    }

    private func hoverPanel(_ track: Track) -> some View {
        let shape = RoundedRectangle(cornerRadius: 15)
        let scrolls = appearance.animation && !reduceMotion
        return HStack(spacing: Self.hoverSpacing) {
            cover(size: Self.hoverCoverSize)
            VStack(alignment: .leading, spacing: 0) {
                ScrollingTrackText(text: track.title, appearance: appearance, scroll: scrolls, size: Self.hoverTitleSize,
                                   weight: Self.hoverTitleWeight, linePadding: 2)
                if !track.artist.isEmpty {
                    ScrollingTrackText(text: track.artist, appearance: appearance, scroll: scrolls, size: Self.hoverArtistSize,
                                       weight: .regular, linePadding: 2)
                        .foregroundStyle(.secondary)
                }
                if !track.album.isEmpty {
                    ScrollingTrackText(text: track.album, appearance: appearance, scroll: scrolls, size: Self.hoverAlbumSize,
                                       weight: .regular, linePadding: 2)
                        .foregroundStyle(.secondary)
                }
            }
            .frame(maxWidth: .infinity, alignment: .leading)
        }
        .padding(.horizontal, Self.hoverPadding)
        .frame(height: Self.hoverHeight)
        .background {
            shape.fill(appearance.tint(colorScheme))
            albumLight(shape, opacity: 0.68, mist: true, animates: model.capsuleGlow)
        }
        .clipShape(shape)
        .glassEffect(.regular, in: shape)
    }

    @ViewBuilder
    private func lyricsPanel(_ track: Track, attached: Bool) -> some View {
        let shape = RoundedRectangle(cornerRadius: 12)
        let spectrumInk = detailSpectrumInk(attached: attached)
        let showsTrackSummary = attached && model.lyrics.isEmpty
        let content = VStack(alignment: .leading, spacing: 0) {
            ZStack(alignment: .topLeading) {
                GeometryReader { geometry in
                    let spectrumHeight = attached ? min(142, max(42, geometry.size.height * 0.30)) : 142
                    let opacity = spectrumInk.opacity * (spectrumHeight / 142)
                    let lyricTop: CGFloat = attached ? 4 : 30
                    let lyricHeight = max(44, min(lyricViewport, geometry.size.height - lyricTop))
                    let fadeExtension = attached && !showsTrackSummary ? min(12, appearance.lyricsSize * 0.65) : 0
                    ZStack(alignment: .topLeading) {
                        spectrumBars(height: spectrumHeight, opacity: opacity, color: spectrumInk.color)
                            .frame(maxWidth: .infinity, maxHeight: .infinity, alignment: .bottom)
                            .zIndex(attached ? 0 : 1)
                        lyricView(track, attached: attached, fadeExtension: fadeExtension)
                            .frame(height: lyricHeight + fadeExtension, alignment: .top)
                            .padding(.leading, 12).padding(.top, lyricTop - fadeExtension)
                    }
                }
                if !attached {
                    HStack(spacing: 9) {
                        let source = model.previewSpectrum == nil ? model.lyricsCaption : "歌词"
                        appearance.styledText("\(track.player.name) · \(source)", size: 11)
                            .foregroundStyle(Color.primary.opacity(0.72)).lineLimit(1)
                        Spacer(minLength: 0)
                        timingButton("minus", label: "歌词延后 0.5 秒", delta: -0.5)
                        timingButton("plus", label: "歌词提前 0.5 秒", delta: 0.5)
                        Menu {
                            Button("导入本地歌词…") { model.importLyrics() }
                            Button("恢复原始时间") { model.adjustOffset(-model.offset) }.disabled(model.offset == 0)
                        } label: {
                            Image(systemName: "ellipsis").frame(width: 25, height: 25)
                        }
                        .menuStyle(.borderlessButton).menuIndicator(.hidden).fixedSize().accessibilityLabel("歌词选项")
                        Button(action: toggle) {
                            Image(systemName: "xmark").font(.system(size: 15, weight: .medium))
                                .frame(width: 25, height: 25).contentShape(Rectangle())
                        }
                        .buttonStyle(.plain).help("关闭歌词").accessibilityLabel("关闭歌词")
                    }
                    .padding(.leading, 12).frame(height: 28).padding(.top, 4)
                }
            }
            .frame(maxHeight: .infinity)

            VStack(alignment: .leading, spacing: 10) {
                if attached, let playbackMessage {
                    Text(playbackMessage).font(.caption).foregroundStyle(.secondary)
                        .accessibilityAddTraits(.updatesFrequently)
                }
                HStack(spacing: 12) {
                    cover(size: 58)
                    if showsTrackSummary {
                        Spacer(minLength: 0).frame(minHeight: track.album.isEmpty ? 58 : 66)
                    } else {
                        trackNames(track, attached: attached)
                    }
                    if attached {
                        playbackControls(track)
                            .fixedSize(horizontal: true, vertical: false)
                            .zIndex(1)
                    }
                }
                if attached {
                    AttachedPlaybackProgress(track: track, music: model, appearance: appearance)
                        .id(track.key)
                } else {
                    PlaybackProgress(track: track, appearance: appearance, colors: model.colors,
                                     calibration: model.playbackCalibration,
                                     frozen: model.previewSpectrum != nil)
                }
            }.padding(.bottom, 8)
        }
        .padding(.horizontal, 22).padding(.top, attached ? 8 : 14).padding(.bottom, 16)
        .frame(maxWidth: .infinity, maxHeight: .infinity, alignment: .top)
        .overlay(alignment: .bottom) {
            if showsTrackSummary && model.lyricsLoading {
                appearance.styledText("正在查找同步歌词…", size: 11)
                    .foregroundStyle(.secondary)
                    .padding(.bottom, 5)
                    .allowsHitTesting(false)
                    .accessibilityAddTraits(.updatesFrequently)
            }
        }
        .onChange(of: track.key) { _, _ in playbackMessage = nil }
        if attached {
            content
        } else {
            content.background {
                albumLight(shape, opacity: detailGlowOpacity(attached: false), mist: true)
            }
            .modifier(FlyoutChrome(appearance: appearance))
        }
    }

    private var attachedBackdrop: some View {
        ZStack {
            appearance.tint(colorScheme).opacity(0.72)
            if model.notchLyrics.glow {
                albumLight(Rectangle(), opacity: detailGlowOpacity(attached: true), mist: true, attachedLyrics: true)
            }
            RadialGradient(colors: [.black.opacity(colorScheme == .dark ? 0.48 : 0.10), .clear],
                           center: .top, startRadius: 8, endRadius: 135)
        }
        .allowsHitTesting(false)
        .accessibilityHidden(true)
    }

    private func playbackControls(_ track: Track) -> some View {
        HStack(spacing: 3) {
            playbackButton("backward.end.fill", label: "前一首", command: .previous, track: track)
            playbackButton(track.playing ? "pause.fill" : "play.fill", label: track.playing ? "暂停" : "播放",
                           command: .playPause, track: track)
            playbackButton("forward.end.fill", label: "下一首", command: .next, track: track)
        }
        .disabled(!track.systemSession || SystemMedia.executable == nil || isSendingPlayback)
    }

    private func playbackButton(_ symbol: String, label: String, command: SystemMedia.Command,
                                track: Track) -> some View {
        let primary = command == .playPause
        return Button {
            guard !isSendingPlayback else { return }
            playbackMessage = nil
            isSendingPlayback = true
            Task { @MainActor in
                let accepted = await model.control(command, trackKey: track.key)
                isSendingPlayback = false
                if !accepted, model.track?.key == track.key { playbackMessage = "播放器未接受操作" }
            }
        } label: {
            Image(systemName: symbol)
                .font(.system(size: primary ? 17 : 15, weight: .medium))
        }
        .buttonStyle(AttachedTransportStyle(primary: primary, tint: model.colors.first ?? Color.accentColor,
                                            animates: appearance.animation))
        .accessibilityLabel(label)
        .accessibilityIdentifier(primary ? "LumiBar.attached.playPause"
                                 : command == .previous ? "LumiBar.attached.previous" : "LumiBar.attached.next")
        .help(label)
    }

    private var lyricViewport: CGFloat {
        let size = appearance.lyricsSize
        let step = size * 1.3 + min(18, max(9, size * 0.5))
        let rows: Double = size <= 21 ? 6 : size <= 29 ? 5 : 4
        return min(392, max(312, step * rows + 120)) + step * 2
    }

    @ViewBuilder
    private func capsuleCoverLight(at time: TimeInterval?) -> some View {
        if appearance.coverTint {
            let strength = appearance.coverBlend / 0.55
            let light = colorScheme == .light
            let colors = model.colors.map { softenCoverLight($0, saturation: appearance.coverSaturation, light: light) }
            let phase = (time ?? 0) * 2 * .pi / 6.5
            let sweep = sin(phase)
            let drift = time == nil ? 0 : cos(phase)
            ZStack {
                colors[0].opacity(strength * (light ? 0.26 : 0.32))
                RadialGradient(colors: [colors[0].opacity(strength * 0.76), colors[0].opacity(0)],
                               center: UnitPoint(x: 0.37 + sweep * 0.28, y: 0.67 + drift * 0.16),
                               startRadius: 2, endRadius: 32)
                RadialGradient(colors: [colors[1].opacity(strength * 0.70), colors[1].opacity(0)],
                               center: UnitPoint(x: 0.72 - sweep * 0.30, y: 0.33 - drift * 0.14),
                               startRadius: 0, endRadius: 28)
            }
        }
    }

    private func albumLight<S: Shape>(_ shape: S, opacity: Double, mist: Bool = false,
                                      attachedLyrics: Bool = false, animates: Bool = true) -> some View {
        let moving = animates && appearance.animation && !reduceMotion && model.track?.playing == true
        let strength = opacity * appearance.coverBlend / 0.55
        let colors = model.colors.map { softenCoverLight($0, saturation: appearance.coverSaturation, light: colorScheme == .light) }
        return GeometryReader { geometry in
            if appearance.coverTint {
                TimelineView(.animation(minimumInterval: 1 / 18, paused: !moving)) { context in
                    let time = attachedLyrics && moving ? model.lyricGlowTime(expanded: true)
                        : context.date.timeIntervalSinceReferenceDate
                    let dx = moving ? sin(time * 2 * .pi / 17) * 0.022 : 0
                    let dy = moving ? sin(time * 2 * .pi / 21) * 0.015 : 0
                    let width = geometry.size.width
                    let height = geometry.size.height
                    let capsule = height < 100
                    let primary = RadialGradient(colors: [colors[0].opacity(strength), colors[0].opacity(strength * 0.48), .clear],
                                                 center: UnitPoint(x: 0.28 + dx, y: 0.83 + dy),
                                                 startRadius: 3, endRadius: max(width * 0.68, height * 0.45))
                    let secondary = RadialGradient(colors: [colors[1].opacity(strength * 0.73), .clear],
                                                   center: UnitPoint(x: 0.91 - dx, y: (capsule ? 0.63 : 0.87) - dy),
                                                   startRadius: 0, endRadius: max(width * 0.65, height * 0.38))
                    shape.fill(primary)
                        .overlay { shape.fill(secondary) }
                        .overlay {
                            if mist {
                                lyricMist(size: geometry.size, colors: colors, strength: strength, time: moving ? time : 0)
                                    .clipShape(shape)
                            }
                        }
                }
            }
        }.allowsHitTesting(false)
    }

    private func lyricMist(size: CGSize, colors: [Color], strength: Double, time: TimeInterval) -> some View {
        let broad = time * 2 * .pi / 12
        let slow = time * 2 * .pi / 17
        return ZStack {
            mistLayer(colors[0], opacity: strength * 0.37, size: size,
                      center: UnitPoint(x: 0.22 + sin(broad) * 0.29, y: 0.73 + cos(slow) * 0.06), angle: -12)
            mistLayer(colors[1], opacity: strength * 0.31, size: size,
                      center: UnitPoint(x: 0.74 + sin(slow) * 0.23, y: 0.88 + sin(broad) * 0.05), angle: 9)
        }
    }

    private func mistLayer(_ color: Color, opacity: Double, size: CGSize, center: UnitPoint, angle: Double) -> some View {
        let radius = size.width / 2
        return RadialGradient(colors: [color.opacity(opacity), color.opacity(opacity * 0.43), color.opacity(0)],
                              center: .center, startRadius: 0, endRadius: radius)
            .frame(width: radius * 2, height: radius * 2)
            .scaleEffect(x: 1.46, y: 0.39)
            .rotationEffect(.degrees(angle))
            .position(x: size.width * center.x, y: size.height * center.y)
    }

    private func trackNames(_ track: Track, attached: Bool) -> some View {
        let scroll = appearance.animation && !reduceMotion
        let artist = track.artist.isEmpty ? track.player.name : track.artist
        let footerHeight: CGFloat? = attached && !track.album.isEmpty ? 66 : nil
        return VStack(alignment: .leading, spacing: attached ? 0 : 4) {
            ScrollingTrackText(text: track.title, appearance: appearance, scroll: scroll, size: 19, weight: .semibold)
            ScrollingTrackText(text: artist, appearance: appearance, scroll: scroll, size: 13, weight: .regular)
                .opacity(0.8)
            if !track.album.isEmpty {
                ScrollingTrackText(text: track.album, appearance: appearance, scroll: scroll, size: 11, weight: .regular)
                    .opacity(0.65)
            }
        }.frame(maxWidth: .infinity, minHeight: footerHeight, alignment: .leading)
    }

    private func timingButton(_ symbol: String, label: String, delta: Double) -> some View {
        Button { model.adjustOffset(delta) } label: {
            Image(systemName: symbol).font(.system(size: 15, weight: .medium))
                .frame(width: 24, height: 25).contentShape(Rectangle())
        }
        .buttonStyle(.plain)
        .accessibilityLabel(label)
        .help(label + String(format: " · 本曲偏移 %+.1f 秒", model.offset))
        .disabled(model.lyrics.isEmpty)
    }

    private func cover(size: CGFloat, compact: Bool = false) -> some View {
        Group {
            if let artwork = model.artwork {
                Image(nsImage: artwork).resizable().scaledToFill()
            } else {
                RoundedRectangle(cornerRadius: 6)
                    .fill(.linearGradient(colors: model.colors.map { $0.opacity(0.62) },
                                         startPoint: .topLeading, endPoint: .bottomTrailing))
                    .overlay {
                        Image(systemName: "music.note").font(.system(size: size * 0.46, weight: .semibold))
                    }
            }
        }.frame(width: size, height: size).clipShape(RoundedRectangle(cornerRadius: compact ? 8 : size * 0.19))
            .accessibilityHidden(true)
    }


    private func spectrumBars(height: CGFloat, opacity: Double, color: Color = .white) -> some View {
        let enabled = model.spectrumEnabled && model.track?.playing == true && !reduceMotion
        return TimelineView(.animation(minimumInterval: 1 / 25, paused: !enabled)) { _ in
            let values = model.previewSpectrum ?? (enabled ? model.spectrum.levels : [])
            SpectrumColumns(levels: values, color: color, opacity: opacity)
                .frame(height: height)
        }.accessibilityHidden(true).allowsHitTesting(false)
    }

    private func detailSpectrumInk(attached: Bool) -> SpectrumInk {
        let light = colorScheme == .light
        let glowEnabled = appearance.coverTint && (!attached || model.notchLyrics.glow)
        let glowOpacity = detailGlowOpacity(attached: attached)
        let strength = glowEnabled ? glowOpacity * appearance.coverBlend / 0.55 : 0
        let coverColor = model.colors.first ?? Color.accentColor
        let secondary = model.colors.dropFirst().first ?? coverColor
        let softened = [coverColor, secondary].map {
            softenCoverLight($0, saturation: appearance.coverSaturation, light: light)
        }
        return SpectrumInk(cover: coverColor, primaryGlow: softened[0], secondaryGlow: softened[1],
                           strength: strength, light: light)
    }

    private func detailGlowOpacity(attached: Bool) -> Double {
        if colorScheme == .dark { return attached ? 0.78 : 0.75 }
        return attached ? 0.62 : 0.58
    }

    private func lyricView(_ track: Track, attached: Bool, fadeExtension: CGFloat) -> some View {
        Group {
            if model.lyrics.isEmpty {
                if attached {
                    VStack(alignment: .leading, spacing: 6) {
                        appearance.styledText(track.title, size: max(25, appearance.lyricsSize), weight: .semibold)
                            .lineLimit(3)
                        if !track.artist.isEmpty {
                            appearance.styledText(track.artist, size: 16).foregroundStyle(.primary.opacity(0.78))
                                .lineLimit(2)
                        }
                        if !track.album.isEmpty {
                            appearance.styledText(track.album, size: 13).foregroundStyle(.secondary)
                                .lineLimit(2)
                        }
                    }
                    .padding(.trailing, 12)
                    .frame(maxWidth: .infinity, maxHeight: .infinity, alignment: .leading)
                } else {
                    appearance.styledText(model.lyricsLoading ? "正在查找同步歌词…" : "暂无同步歌词", size: 18, weight: .medium)
                        .foregroundStyle(.secondary)
                        .frame(maxWidth: .infinity, maxHeight: .infinity, alignment: .leading)
                }
            } else {
                LyricsPane(track: track, lines: model.lyrics, offset: model.offset, calibration: model.playbackCalibration,
                           appearance: appearance, fadeExtension: fadeExtension,
                           frozen: !attached && model.previewSpectrum != nil,
                           showsCountdown: attached && model.notchLyrics.showCountdown,
                           acceptsManualText: model.lyricsAreLocal,
                           attachedStyle: attached ? AttachedLyricsStyle(music: model, appearance: appearance,
                                                                         dark: colorScheme == .dark) : nil)
                    .id(track.key)
            }
        }
    }
}

private struct AttachedTransportStyle: ButtonStyle {
    let primary: Bool
    let tint: Color
    let animates: Bool

    func makeBody(configuration: Configuration) -> some View {
        Face(label: configuration.label, pressed: configuration.isPressed,
             primary: primary, tint: tint, animates: animates)
    }

    private struct Face<Label: View>: View {
        let label: Label
        let pressed: Bool
        let primary: Bool
        let tint: Color
        let animates: Bool
        @Environment(\.colorScheme) private var scheme
        @Environment(\.accessibilityReduceMotion) private var reduceMotion
        @Environment(\.isEnabled) private var enabled
        @ViewState private var hovered = false

        var body: some View {
            let motion = animates && !reduceMotion
            let pressing = enabled && pressed
            let highlighting = enabled && (hovered || pressed)
            let whiteMix = scheme == .dark ? (pressing ? 0.26 : 0.43) : (pressing ? 0.08 : 0.27)
            let feedbackTint = tint.mix(with: .white, by: whiteMix)
            let width: CGFloat = primary ? 43 : 34

            ZStack {
                if primary { Circle().fill(tint.opacity(0.26)).frame(width: width, height: width) }
                Circle().fill(feedbackTint.opacity(pressing ? 0.74 : 0.50))
                    .overlay { Circle().strokeBorder(.primary.opacity(pressing ? 0.39 : 0.23), lineWidth: 1) }
                    .frame(width: width, height: width)
                    .opacity(highlighting ? 1 : 0)
                label.foregroundStyle(.primary.opacity(enabled ? 1 : 0.55))
            }
            .scaleEffect(motion && pressing ? 0.87 : 1)
            .offset(y: motion && pressing ? 1 : 0)
            .frame(width: width, height: 43)
            .contentShape(Rectangle())
            .onHover { hovered = $0 }
            .animation(motion ? .spring(response: 0.21, dampingFraction: 0.67) : nil, value: pressing)
            .animation(motion ? .easeOut(duration: 0.14) : nil, value: hovered)
        }
    }
}

private struct LyricsPane: View {
    let track: Track
    let lines: [Lyric]
    let offset: Double
    let calibration: Double
    let appearance: AppearanceModel
    let fadeExtension: CGFloat
    let frozen: Bool
    let showsCountdown: Bool
    let acceptsManualText: Bool
    let attachedStyle: AttachedLyricsStyle?
    @ViewState private var focusedFrame: CGRect?
    @Environment(\.accessibilityReduceMotion) private var reduceMotion

    private struct ScrollFocus: Equatable {
        let index: Int?
        let cueTime: Double?
        let text: String?
        let translation: String?
        let viewport: CGSize
        let step: CGFloat
    }

    private var gap: CGFloat { min(18, max(9, appearance.lyricsSize * 0.5)) }
    private var step: CGFloat { appearance.lyricsSize * 1.3 + gap }
    private var focusTop: CGFloat {
        let original = min(128, max(80, step + 48)) + step * 0.5
        return attachedStyle == nil ? original : max(34, original - 50)
    }
    private var scrollFocusTop: CGFloat { attachedStyle == nil ? focusTop : focusTop + step }
    private var motion: Bool { appearance.animation && !reduceMotion && !frozen }

    var body: some View {
        GeometryReader { geometry in
            let name = "lyrics-viewport"
            let height = max(1, geometry.size.height)
            let originalHeight = max(1, height - fadeExtension)
            ScrollViewReader { reader in
                TimelineView(.periodic(from: .now, by: frozen ? 120 : attachedStyle == nil ? 0.12 : 0.06)) { _ in
                    let shift = calibration + offset
                    let position = track.lyricPosition(offset: shift, frozen: frozen)
                    let usesVocalTiming = showsCountdown && (!acceptsManualText || lines.contains(where: \.isVocalLine))
                    let active = lines.lastIndex {
                        usesVocalTiming ? $0.vocalStartTime <= position && $0.isVocalLine : $0.time <= position
                    }
                    let countdown = showsCountdown ? LyricCountdown.current(in: lines, track: track, position: position,
                                                                            offset: shift, acceptsManualText: acceptsManualText) : nil
                    let anchor = countdown?.nextIndex ?? active
                    let line = (anchor ?? lines.indices.first).map { lines[$0] }
                    let focus = ScrollFocus(index: anchor, cueTime: line?.time, text: line?.text,
                                            translation: line?.translation, viewport: geometry.size, step: step)
                    ScrollView {
                        LazyVStack(alignment: .leading, spacing: gap) {
                            ForEach(Array(lines.enumerated()), id: \.element.id) { index, line in
                                let intensity = active.map {
                                    $0 == index ? 1.0 : abs($0 - index) <= 2 ? AttachedLyricsStyle.adjacentOpacity : 0.40
                                } ?? 0.78
                                let singing = countdown == nil && active == index
                                lyricRow(line, active: singing, width: geometry.size.width)
                                    .opacity(attachedStyle != nil ? intensity : active.map {
                                        $0 == index ? 1 : abs($0 - index) <= 2 ? 0.62 : 0.30
                                    } ?? 0.78)
                                    .fixedSize(horizontal: false, vertical: true)
                                    .frame(maxWidth: .infinity, alignment: .leading)
                                    .accessibilityAddTraits(singing ? .isSelected : [])
                                    .animation(motion ? .easeInOut(duration: 0.52) : nil, value: active)
                                    .onGeometryChange(for: CGRect.self, of: { $0.frame(in: .named(name)) }) { rect in
                                        if attachedStyle == nil, active == index { focusedFrame = rect }
                                    }
                                    .overlay(alignment: .topLeading) {
                                        if let countdown, countdown.nextIndex == index {
                                            HStack(spacing: 4) {
                                                ForEach(0..<3, id: \.self) { dot in
                                                    Circle().fill(.primary.opacity(LyricCountdown.opacity(of: dot, progress: countdown.progress)))
                                                        .frame(width: 4, height: 4)
                                                }
                                            }
                                            .accessibilityElement(children: .ignore).accessibilityLabel(countdown.caption)
                                            .offset(y: -gap / 2 - 2)
                                            .allowsHitTesting(false)
                                        }
                                    }
                                    .id(line.id)
                            }
                        }
                        .padding(.top, min(scrollFocusTop, originalHeight * 0.54) + fadeExtension)
                        .padding(.bottom, max(step * 1.5, originalHeight - focusTop))
                    }
                    .coordinateSpace(name: name)
                    .scrollIndicators(.never, axes: .vertical)
                    .onChange(of: focus, initial: true) { old, new in
                        guard let index = new.index ?? lines.indices.first else { return }
                        if new.index == nil { focusedFrame = nil }
                        let lineHeight = max(appearance.lyricsSize * 1.3, focusedFrame?.height ?? 0)
                        let scrollHeight = max(1, originalHeight - lineHeight)
                        let focusY = min(scrollHeight * 0.67, scrollFocusTop) + fadeExtension
                        let target = UnitPoint(x: 0, y: focusY / (scrollHeight + fadeExtension))
                        let animated = motion && old.index != new.index && old.viewport == new.viewport
                            && old.step == new.step && old.index.map { abs($0 - index) <= 2 } == true
                        Task { @MainActor in
                            if animated {
                                withAnimation(.timingCurve(1.0 / 3, 0, 2.0 / 3, 1, duration: 0.68)) {
                                    reader.scrollTo(lines[index].id, anchor: target)
                                }
                            } else { reader.scrollTo(lines[index].id, anchor: target) }
                        }
                    }
                }
            }
            .mask {
                let top = focusedFrame?.minY ?? focusTop
                let lineHeight = focusedFrame?.height ?? appearance.lyricsSize * 1.3
                let originalUpper = min(attachedStyle == nil ? 0.26 : 0.10, max(0.02, (top - 8) / originalHeight))
                let originalLower = attachedStyle == nil
                    ? min(0.995, max(0.59, (top + lineHeight + 8) / originalHeight)) : 0.76
                let upper = (fadeExtension + originalUpper * originalHeight) / height
                let lower = (fadeExtension + originalLower * originalHeight) / height
                LinearGradient(stops: [.init(color: .clear, location: 0),
                                       .init(color: .black.opacity(0.40), location: upper * 0.40),
                                       .init(color: .black, location: upper),
                                       .init(color: .black, location: lower),
                                       .init(color: .black.opacity(0.42), location: lower + (1 - lower) * 0.53),
                                       .init(color: .clear, location: 1)], startPoint: .top, endPoint: .bottom)
            }
        }
    }

    @ViewBuilder
    private func lyricRow(_ line: Lyric, active: Bool, width: CGFloat) -> some View {
        if let attachedStyle {
            AttachedLyricRow(line: line, track: track, offset: offset, calibration: calibration,
                             width: max(1, width), style: attachedStyle,
                             active: active, frozen: frozen)
        } else {
            appearance.styledText(LyricDisplayText(line.text).string, size: appearance.lyricsSize, weight: .semibold)
        }
    }
}

private struct PlaybackProgress: View {
    let track: Track
    let appearance: AppearanceModel
    let colors: [Color]
    let calibration: Double
    let frozen: Bool
    var body: some View {
        TimelineView(.animation(minimumInterval: 0.06, paused: frozen || !track.playing)) { _ in
            let position = track.displayPosition(calibration: calibration, frozen: frozen)
            let rail = MusicProgressRail(position: position, duration: track.duration, estimated: track.estimatedPosition,
                                         appearance: appearance, colors: colors) { EmptyView() }
            rail.accessibilityElement(children: .ignore)
                .accessibilityLabel("播放进度")
                .accessibilityValue(rail.timeLabel)
        }
    }
}

struct MusicProgressRail<Overlay: View>: View {
    let position: Double
    let duration: Double
    let estimated: Bool
    let appearance: AppearanceModel
    let colors: [Color]
    var showsThumb = false
    var thumbHoverX: CGFloat?
    var thumbPressed = false
    @ViewBuilder let overlay: () -> Overlay
    @Environment(\.accessibilityReduceMotion) private var reduceMotion

    var timeLabel: String { formattedTime(at: position) }

    var body: some View {
        let fraction = duration.isFinite && duration > 0 && position.isFinite ? max(0, min(1, position / duration)) : 0
        return HStack(spacing: 9) {
            GeometryReader { geometry in
                let thumbX = max(5.5, min(geometry.size.width - 5.5, geometry.size.width * fraction))
                let hovered = thumbHoverX.map { abs($0 - thumbX) < 10 } ?? false
                let feedback = thumbPressed ? 2 : hovered ? 1 : 0
                ZStack(alignment: .leading) {
                    ZStack(alignment: .leading) {
                        Capsule().fill(.secondary.opacity(0.48)).frame(height: 4)
                        Capsule().fill(.linearGradient(colors: colors, startPoint: .leading, endPoint: .trailing))
                            .frame(width: geometry.size.width * fraction, height: 4)
                        if showsThumb {
                            let ink = colors.last ?? Color.accentColor
                            Circle().fill(ink.mix(with: .white, by: feedback == 2 ? 0.14 : feedback == 1 ? 0.08 : 0))
                                .frame(width: 11, height: 11)
                                .scaleEffect(feedback == 2 ? 1.19 : feedback == 1 ? 1.10 : 1)
                                .shadow(color: .black.opacity(feedback == 0 ? 0.23 : 0.32),
                                        radius: feedback == 0 ? 1.8 : 2.5, y: 1)
                                .animation(appearance.animation && !reduceMotion ? .easeOut(duration: 0.14) : nil,
                                           value: feedback)
                                .offset(x: thumbX - 5.5)
                        }
                    }
                    .frame(maxHeight: .infinity)
                    .allowsHitTesting(false)
                    .accessibilityHidden(true)
                    overlay()
                }
            }.frame(height: 17)
            timeDisplay.foregroundStyle(.secondary)
        }
    }

    @ViewBuilder
    private var timeDisplay: some View {
        let value = appearance.styledText(timeLabel, size: 11)
        if showsThumb {
            // Reserve the space for the longest minutes field before the user starts dragging.
            appearance.styledText(formattedTime(at: duration), size: 11).monospacedDigit().fixedSize().hidden()
                .overlay(alignment: .trailing) { value.monospacedDigit().fixedSize() }
        } else {
            value.fixedSize()
        }
    }

    private func formattedTime(at position: Double) -> String {
        (estimated ? "≈ " : "") + "\(clock(position)) / \(clock(duration))"
    }

    private func clock(_ seconds: Double) -> String {
        let value = max(0, Int(exactly: seconds.rounded(.towardZero)) ?? 0)
        return String(format: "%d:%02d", value / 60, value % 60)
    }
}

private struct ScrollingTrackText: View {
    let text: String
    let appearance: AppearanceModel
    let scroll: Bool
    let size: CGFloat
    let weight: Font.Weight
    var linePadding: CGFloat = 5
    @ViewState private var began = Date.now

    var body: some View {
        GeometryReader { geometry in
            let width = ceil(appearance.textWidth(text, size: size, weight: weight))
            let label = appearance.styledText(text, size: size, weight: weight).lineLimit(1)
            let viewport = geometry.size
            ZStack(alignment: .leading) {
                Group {
                    if scroll && width > viewport.width + 2 {
                        TimelineView(.animation(minimumInterval: 1 / 30)) { context in
                            let gap: CGFloat = 24
                            let distance = width + gap
                            let elapsed = max(0, context.date.timeIntervalSince(began) - 1.25)
                            let offset = (elapsed * 27).truncatingRemainder(dividingBy: distance)
                            HStack(spacing: gap) {
                                label.fixedSize().frame(width: width, alignment: .leading)
                                label.fixedSize().frame(width: width, alignment: .leading)
                            }
                            .fixedSize()
                            .offset(x: -offset)
                        }
                        .frame(width: viewport.width, height: viewport.height, alignment: .leading)
                        .mask {
                            LinearGradient(stops: [.init(color: .clear, location: 0), .init(color: .black, location: 0.05),
                                                   .init(color: .black, location: 0.95), .init(color: .clear, location: 1)],
                                           startPoint: .leading, endPoint: .trailing)
                        }
                    } else {
                        label
                    }
                }
                .frame(width: viewport.width, height: viewport.height, alignment: .leading)
                .clipped()
                .accessibilityHidden(true)
                .allowsHitTesting(false)

                // The scroll copies can extend to the right of the artwork. Present one stationary reading frame.
                Color.clear
                    .frame(width: viewport.width, height: viewport.height)
                    .accessibilityElement(children: .ignore)
                    .accessibilityLabel(text)
                    .accessibilityAddTraits(.isStaticText)
            }
        }
        .frame(height: size + linePadding)
        .onChange(of: text) { _, _ in began = .now }
    }
}
