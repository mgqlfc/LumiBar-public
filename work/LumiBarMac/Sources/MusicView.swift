import SwiftUI

struct MusicView: View {
    enum Surface { case entry(CGFloat), expanded }
    let model: MusicModel
    let appearance: AppearanceModel
    let surface: Surface
    let toggle: () -> Void
    @Environment(\.accessibilityReduceMotion) private var reduceMotion
    @Environment(\.colorScheme) private var colorScheme

    var body: some View {
        if let track = model.track {
            switch surface {
            case .entry(let height):
                HStack(spacing: 6) {
                    cover(size: min(21, height - 4))
                    spectrumBars(height: 13, opacity: 0.78, count: 8, color: .primary)
                        .frame(width: 24)
                }
                .frame(width: 59, height: height)
                .background(model.colors[0].opacity(colorScheme == .dark ? 0.20 : 0.14), in: Capsule())
                .accessibilityHidden(true)
            case .expanded:
                if model.mode == .lyrics { lyricsPanel(track) }
                else { hoverPanel(track) }
            }
        }
    }

    private func hoverPanel(_ track: Track) -> some View {
        HStack(spacing: 10) {
            Button(action: toggle) {
                HStack(spacing: 10) {
                    cover(size: 39)
                    trackNames(track, expanded: false)
                }
                .contentShape(Rectangle())
            }.buttonStyle(.plain).help("打开同步歌词")
            transport(track, compact: true)
        }
        .padding(12)
        .background {
            let shape = RoundedRectangle(cornerRadius: 20)
            shape.fill(Color(white: 0.10))
            albumLight(shape, opacity: 0.54)
            spectrumBars(height: 32, opacity: 0.32)
                .padding(.leading, 48).padding(.trailing, 10)
                .frame(maxHeight: .infinity, alignment: .bottom).clipShape(shape)
        }
        .foregroundStyle(.white)
        .environment(\.colorScheme, .dark)
        .padding(6)
    }

    private func lyricsPanel(_ track: Track) -> some View {
        let shape = RoundedRectangle(cornerRadius: 12)
        return VStack(alignment: .leading, spacing: 0) {
            ZStack(alignment: .topLeading) {
                GeometryReader { geometry in
                    lyricView(track)
                        .frame(height: max(44, min(lyricViewport, geometry.size.height - 30)), alignment: .top)
                        .padding(.leading, 12).padding(.top, 30)
                }
                spectrumBars(height: 142, opacity: 0.45, color: model.colors[0])
                    .frame(maxHeight: .infinity, alignment: .bottom)
                HStack(spacing: 9) {
                    let source = model.previewSpectrum == nil ? model.lyricsCaption : "歌词"
                    appearance.styledText("\(track.player.name) · \(source)", size: 11)
                        .foregroundStyle(Color.primary.opacity(0.72)).lineLimit(1)
                    Spacer(minLength: 0)
                    timingButton("minus", label: "歌词延后 0.5 秒", delta: -0.5)
                    timingButton("plus", label: "歌词提前 0.5 秒", delta: 0.5)
                    Menu {
                        Button("导入 LRC 歌词…") { model.loadLRC() }
                        Button("恢复原始时间") { model.adjustOffset(-model.offset) }.disabled(model.offset == 0)
                        Divider()
                        Button("收起歌词", action: toggle)
                    } label: {
                        Image(systemName: "ellipsis").frame(width: 25, height: 25)
                    }
                    .menuStyle(.borderlessButton).menuIndicator(.hidden).fixedSize().accessibilityLabel("歌词选项")
                }.padding(.leading, 12).frame(height: 28).padding(.top, 4)
            }
            .frame(maxHeight: .infinity)

            VStack(spacing: 14) {
                HStack(spacing: 19) {
                    cover(size: 58)
                    trackNames(track, expanded: true)
                    transport(track, compact: false)
                }
                PlaybackProgress(track: track, appearance: appearance, colors: model.colors,
                                 frozen: model.previewSpectrum != nil) { model.control(.seek($0)) }
                    .id(track.key)
            }.padding(.bottom, 8)
        }
        .padding(.horizontal, 22).padding(.top, 14).padding(.bottom, 16)
        .frame(maxWidth: .infinity, maxHeight: .infinity, alignment: .top)
        .background {
            albumLight(shape, opacity: colorScheme == .dark ? 0.75 : 0.58)
        }
        .modifier(FlyoutChrome(appearance: appearance))
    }

    private var lyricViewport: CGFloat {
        let size = appearance.lyricsSize
        let step = size * 1.3 + min(18, max(9, size * 0.5))
        let rows: Double = size <= 21 ? 6 : size <= 29 ? 5 : 4
        return min(392, max(312, step * rows + 120)) + step * 2
    }

    private func albumLight<S: Shape>(_ shape: S, opacity: Double) -> some View {
        let moving = appearance.animation && !reduceMotion && model.track?.playing == true
        let strength = opacity * appearance.coverBlend / 0.55
        let colors = model.colors.map { softenCoverLight($0, saturation: appearance.coverSaturation, light: colorScheme == .light) }
        return GeometryReader { geometry in
            if appearance.coverTint {
                TimelineView(.animation(minimumInterval: 1 / 18, paused: !moving)) { context in
                    let time = context.date.timeIntervalSinceReferenceDate
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
                    shape.fill(primary).overlay { shape.fill(secondary) }
                }
            }
        }.allowsHitTesting(false)
    }

    private func trackNames(_ track: Track, expanded: Bool) -> some View {
        VStack(alignment: .leading, spacing: 4) {
            ScrollingTitle(text: track.title, appearance: appearance, scroll: appearance.animation && !reduceMotion, size: expanded ? 19 : 14)
            appearance.styledText(track.artist.isEmpty ? track.player.name : track.artist, size: expanded ? 13 : 11)
                .opacity(0.8).lineLimit(1)
            if expanded && !track.album.isEmpty {
                appearance.styledText(track.album, size: 11).opacity(0.65).lineLimit(1)
            }
        }.frame(maxWidth: .infinity, alignment: .leading)
    }

    private func transport(_ track: Track, compact: Bool) -> some View {
        GlassEffectContainer(spacing: 6) {
            HStack(spacing: compact ? 3 : 6) {
                icon("backward.end", "上一首", size: 27) { model.control(.previous) }
                icon(track.playing ? "pause" : "play", track.playing ? "暂停" : "播放",
                     size: compact ? 33 : 42, glass: true) { model.control(.toggle) }
                icon("forward.end", "下一首", size: 27) { model.control(.next) }
            }.padding(.trailing, compact ? 0 : 7).disabled(!track.canControl)
        }
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

    private func cover(size: CGFloat) -> some View {
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
        }.frame(width: size, height: size).clipShape(RoundedRectangle(cornerRadius: size * 0.19))
            .accessibilityHidden(true)
    }


    private func spectrumBars(height: CGFloat, opacity: Double, count: Int = 40, color: Color = .white) -> some View {
        let enabled = model.spectrumEnabled && model.track?.playing == true && !reduceMotion
        return TimelineView(.animation(minimumInterval: 1 / 25, paused: !enabled)) { _ in
            let values = model.previewSpectrum ?? (enabled ? model.spectrum.levels : [])
            Canvas { context, size in
                guard !values.isEmpty else { return }
                let step = size.width / Double(count)
                let barWidth = max(0.8, step * 0.46)
                for index in 0..<count {
                    let position = Double(index) * Double(values.count - 1) / Double(count - 1)
                    let lower = Int(position)
                    let upper = min(values.count - 1, lower + 1)
                    let fraction = position - Double(lower)
                    let level = values[lower] * (1 - fraction) + values[upper] * fraction
                    let barHeight = min(size.height, max(1, level * size.height))
                    let rect = CGRect(x: step * Double(index) + (step - barWidth) / 2,
                                      y: size.height - barHeight, width: barWidth, height: barHeight)
                    context.fill(Path(roundedRect: rect, cornerRadius: barWidth / 2),
                                 with: .linearGradient(Gradient(colors: [color.opacity(opacity), .clear]),
                                                       startPoint: CGPoint(x: 0, y: rect.minY), endPoint: CGPoint(x: 0, y: size.height)))
                }
            }.frame(height: height)
        }.accessibilityHidden(true).allowsHitTesting(false)
    }

    private func icon(_ symbol: String, _ label: String, size: CGFloat, glass: Bool = false,
                      action: @escaping () -> Void) -> some View {
        Button(action: action) {
            Image(systemName: symbol).font(.system(size: size >= 40 ? 18 : 15, weight: .light))
                .frame(width: size, height: size).contentShape(Circle())
        }
        .buttonStyle(AlbumControlStyle(color: model.colors[0], filled: glass))
        .help(label).accessibilityLabel(label)
    }

    private func lyricView(_ track: Track) -> some View {
        Group {
            if model.lyrics.isEmpty {
                appearance.styledText(model.lyricsLoading ? "正在查找同步歌词…" : "暂无同步歌词", size: 18, weight: .medium)
                    .foregroundStyle(.secondary)
                    .frame(maxWidth: .infinity, maxHeight: .infinity, alignment: .leading)
            } else {
                LyricsPane(track: track, lines: model.lyrics, offset: model.offset, appearance: appearance,
                           frozen: model.previewSpectrum != nil).id(track.key)
            }
        }
    }
}

private struct AlbumControlStyle: ButtonStyle {
    let color: Color
    let filled: Bool
    @ViewState private var hovered = false

    func makeBody(configuration: Configuration) -> some View {
        configuration.label
            .background(color.opacity(configuration.isPressed ? 0.32 : hovered ? 0.20 : filled ? 0.09 : 0), in: Circle())
            .onHover { hovered = $0 }
    }
}

private struct LyricsPane: View {
    let track: Track
    let lines: [Lyric]
    let offset: Double
    let appearance: AppearanceModel
    let frozen: Bool
    @ViewState private var focusedFrame: CGRect?
    @Environment(\.accessibilityReduceMotion) private var reduceMotion

    private var gap: CGFloat { min(18, max(9, appearance.lyricsSize * 0.5)) }
    private var step: CGFloat { appearance.lyricsSize * 1.3 + gap }
    private var focusTop: CGFloat { min(128, max(80, step + 48)) + step * 0.5 }
    private var motion: Bool { appearance.animation && !reduceMotion && !frozen }

    var body: some View {
        GeometryReader { geometry in
            let name = "lyrics-viewport"
            let height = max(1, geometry.size.height)
            ScrollViewReader { reader in
                TimelineView(.periodic(from: .now, by: frozen ? 120 : 0.12)) { context in
                    let position = (frozen ? track.position : track.position(at: context.date)) + offset
                    let active = lines.lastIndex { $0.time <= position }
                    ScrollView {
                        LazyVStack(alignment: .leading, spacing: gap) {
                            ForEach(Array(lines.enumerated()), id: \.element.id) { index, line in
                                appearance.styledText(line.text, size: appearance.lyricsSize, weight: .semibold)
                                    .opacity(active.map { $0 == index ? 1 : abs($0 - index) <= 2 ? 0.62 : 0.30 } ?? 0.78)
                                    .fixedSize(horizontal: false, vertical: true)
                                    .frame(maxWidth: .infinity, alignment: .leading)
                                    .accessibilityAddTraits(active == index ? .isSelected : [])
                                    .animation(motion ? .easeInOut(duration: 0.52) : nil, value: active)
                                    .onGeometryChange(for: CGRect.self, of: { $0.frame(in: .named(name)) }) { rect in
                                        if active == index { focusedFrame = rect }
                                    }
                                    .id(line.id)
                            }
                        }
                        .padding(.top, min(focusTop, height * 0.54))
                        .padding(.bottom, max(step * 1.5, height - focusTop))
                    }
                    .coordinateSpace(name: name)
                    .scrollIndicators(.hidden)
                    .onChange(of: active, initial: true) { old, new in
                        guard let new, lines.indices.contains(new) else { return }
                        let lineHeight = max(appearance.lyricsSize * 1.3, focusedFrame?.height ?? 0)
                        let target = UnitPoint(x: 0, y: min(0.67, focusTop / max(1, height - lineHeight)))
                        let animated = motion && old != new && old.map { abs($0 - new) <= 2 } == true
                        Task { @MainActor in
                            if animated {
                                withAnimation(.timingCurve(1.0 / 3, 0, 2.0 / 3, 1, duration: 0.68)) {
                                    reader.scrollTo(lines[new].id, anchor: target)
                                }
                            } else { reader.scrollTo(lines[new].id, anchor: target) }
                        }
                    }
                }
            }
            .mask {
                let top = focusedFrame?.minY ?? focusTop
                let lineHeight = focusedFrame?.height ?? appearance.lyricsSize * 1.3
                let upper = min(0.26, max(0.02, (top - 8) / height))
                let lower = min(0.995, max(0.59, (top + lineHeight + 8) / height))
                LinearGradient(stops: [.init(color: .clear, location: 0),
                                       .init(color: .black.opacity(0.40), location: upper * 0.40),
                                       .init(color: .black, location: upper),
                                       .init(color: .black, location: lower),
                                       .init(color: .black.opacity(0.42), location: lower + (1 - lower) * 0.53),
                                       .init(color: .clear, location: 1)], startPoint: .top, endPoint: .bottom)
            }
        }
    }
}

private struct PlaybackProgress: View {
    let track: Track
    let appearance: AppearanceModel
    let colors: [Color]
    let frozen: Bool
    let seek: (Double) -> Void
    @ViewState private var dragPosition: Double?

    var body: some View {
        TimelineView(.periodic(from: .now, by: frozen ? 120 : 0.25)) { context in
            let position = dragPosition ?? (frozen ? track.position : track.position(at: context.date))
            let fraction = track.duration > 0 ? max(0, min(1, position / track.duration)) : 0
            HStack(spacing: 9) {
                GeometryReader { geometry in
                    let width = geometry.size.width
                    ZStack(alignment: .leading) {
                        Capsule().fill(.secondary.opacity(0.48)).frame(height: 4)
                        Capsule().fill(.linearGradient(colors: colors, startPoint: .leading, endPoint: .trailing))
                            .frame(width: width * fraction, height: 4)
                        Circle().fill(colors[0]).padding(3)
                            .background(.background, in: Circle())
                            .shadow(color: .black.opacity(0.22), radius: 2)
                            .frame(width: 14, height: 14).offset(x: max(0, min(width - 14, width * fraction - 7)))
                    }
                    .frame(maxHeight: .infinity).contentShape(Rectangle())
                    .gesture(DragGesture(minimumDistance: 0).onChanged { value in
                        if track.canSeek, width > 0 { dragPosition = min(1, max(0, value.location.x / width)) * track.duration }
                    }.onEnded { _ in
                        if let dragPosition { seek(dragPosition) }
                        dragPosition = nil
                    })
                    .accessibilityLabel("播放进度")
                    .accessibilityValue(timeLabel(position))
                    .accessibilityAdjustableAction { direction in
                        guard track.canSeek else { return }
                        seek(min(track.duration, max(0, position + (direction == .increment ? 5 : -5))))
                    }
                }.frame(height: 17)
                appearance.styledText((track.estimatedPosition ? "≈ " : "") + "\(timeLabel(position)) / \(timeLabel(track.duration))", size: 11)
                    .fixedSize().foregroundStyle(.secondary)
            }
        }
    }

    private func timeLabel(_ seconds: Double) -> String {
        let value = max(0, Int(seconds))
        return String(format: "%d:%02d", value / 60, value % 60)
    }
}

private struct ScrollingTitle: View {
    let text: String
    let appearance: AppearanceModel
    let scroll: Bool
    let size: CGFloat
    @ViewState private var began = Date.now

    var body: some View {
        GeometryReader { geometry in
            let overflow = ceil(appearance.textWidth(text, size: size, semibold: true)) - geometry.size.width
            if scroll && overflow > 2 {
                TimelineView(.animation(minimumInterval: 1 / 30)) { context in
                    let travel = max(1, overflow / 22)
                    let phase = max(0, context.date.timeIntervalSince(began)).truncatingRemainder(dividingBy: 3.6 + 2 * travel)
                    let outward = min(1, max(0, (phase - 1.8) / travel))
                    let inward = min(1, max(0, (phase - 3.6 - travel) / travel))
                    appearance.styledText(text, size: size, weight: .semibold).fixedSize()
                        .offset(x: -overflow * (outward - inward))
                }
                .mask {
                    LinearGradient(stops: [.init(color: .clear, location: 0), .init(color: .black, location: 0.05),
                                           .init(color: .black, location: 0.95), .init(color: .clear, location: 1)],
                                   startPoint: .leading, endPoint: .trailing)
                }
            } else {
                appearance.styledText(text, size: size, weight: .semibold).lineLimit(1)
            }
        }
        .frame(height: size + 5)
        .onChange(of: text) { _, _ in began = .now }
        .accessibilityLabel(text)
    }
}
