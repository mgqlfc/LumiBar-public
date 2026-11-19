import SwiftUI

struct MusicView: View {
    enum Surface { case leading, trailing, capsule, expanded }
    let model: MusicModel
    let appearance: AppearanceModel
    let surface: Surface
    let toggle: () -> Void
    @Environment(\.accessibilityReduceMotion) private var reduceMotion
    @Environment(\.colorScheme) private var colorScheme

    var body: some View {
        if let track = model.track {
            switch surface {
            case .leading:
                compact { cover(size: 23) }
            case .trailing:
                compact { bars(width: 42) }
            case .capsule:
                compact {
                    HStack(spacing: 8) {
                        cover(size: 27)
                        trackNames(track, expanded: false)
                    }.padding(.horizontal, 7)
                }
            case .expanded:
                if model.mode == .lyrics { lyricsPanel(track) }
                else { hoverPanel(track) }
            }
        }
    }

    private func compact<Content: View>(@ViewBuilder content: () -> Content) -> some View {
        Button(action: toggle) {
            content().frame(maxWidth: .infinity, maxHeight: .infinity).contentShape(Rectangle())
        }
        .buttonStyle(.plain)
        .foregroundStyle(.white)
        .background {
            let shape = Capsule()
            if surface == .capsule {
                shape.fill(Color(white: 0.10))
                albumLight(shape, opacity: 0.57)
                spectrumBars(height: 24, opacity: 0.41)
                    .padding(.leading, 43).padding(.trailing, 7)
                    .frame(maxHeight: .infinity, alignment: .bottom).clipShape(shape)
            } else {
                shape.fill(.black)
                albumLight(shape, opacity: 0.36)
            }
        }
        .accessibilityLabel(model.mode == .lyrics ? "收起歌词" : "展开歌词")
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
        let shape = RoundedRectangle(cornerRadius: 26)
        return VStack(alignment: .leading, spacing: 0) {
            HStack(spacing: 9) {
                Text("\(track.player.name) · \(model.lyricsCaption)")
                    .font(.system(size: 11)).foregroundStyle(.secondary).lineLimit(1)
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
            }
            .padding(.leading, 12)
            .frame(height: 28)

            lyricView(track)
                .padding(.leading, 12)
                .frame(maxHeight: .infinity)

            VStack(spacing: 5) {
                HStack(spacing: 13) {
                    cover(size: 58)
                    trackNames(track, expanded: true)
                    transport(track, compact: false)
                }
                PlaybackProgress(track: track, tint: model.colors[0]) { model.control(.seek($0)) }
                    .id(track.key)
            }
            .padding(.top, 6)
        }
        .padding(.horizontal, 22).padding(.top, 17).padding(.bottom, 18)
        .frame(maxWidth: .infinity, maxHeight: .infinity, alignment: .top)
        .background {
            ZStack(alignment: .bottom) {
                shape.fill(appearance.tint(colorScheme))
                albumLight(shape, opacity: colorScheme == .dark ? 0.66 : 0.43)
                spectrumBars(height: 142, opacity: 0.34)
                    .padding(.horizontal, 22).padding(.bottom, 95)
            }
        }
        .glassEffect(.regular, in: shape)
        .padding(6)
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
            ScrollingTitle(text: track.title, scroll: !reduceMotion, size: expanded ? 19 : 14)
            Text(track.artist.isEmpty ? track.player.name : track.artist)
                .font(lumiFont(expanded ? 13 : 11)).foregroundStyle(.secondary).lineLimit(1)
            if expanded && !track.album.isEmpty {
                Text(track.album).font(lumiFont(11)).foregroundStyle(.secondary).lineLimit(1)
            }
        }.frame(maxWidth: .infinity, alignment: .leading)
    }

    private func transport(_ track: Track, compact: Bool) -> some View {
        GlassEffectContainer(spacing: 6) {
            HStack(spacing: 3) {
                icon("backward.end", "上一首", size: 27) { model.control(.previous) }
                icon(track.playing ? "pause" : "play", track.playing ? "暂停" : "播放",
                     size: compact ? 33 : 41, glass: true) { model.control(.toggle) }
                icon("forward.end", "下一首", size: 27) { model.control(.next) }
            }.disabled(!track.canControl)
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

    private func bars(width: CGFloat) -> some View {
        spectrumBars(height: 16, opacity: 0.85, count: 13)
            .frame(width: width, height: 16)
    }

    private func spectrumBars(height: CGFloat, opacity: Double, count: Int = 40) -> some View {
        let enabled = model.spectrumEnabled && model.track?.playing == true
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
                                 with: .linearGradient(Gradient(colors: [.white.opacity(opacity), .clear]),
                                                       startPoint: CGPoint(x: 0, y: rect.minY), endPoint: CGPoint(x: 0, y: size.height)))
                }
            }.frame(height: height)
        }.accessibilityHidden(true).allowsHitTesting(false)
    }

    private func icon(_ symbol: String, _ label: String, size: CGFloat, glass: Bool = false,
                      action: @escaping () -> Void) -> some View {
        let button = Button(action: action) {
            Image(systemName: symbol).font(.system(size: size * 0.39, weight: .semibold))
                .frame(width: size, height: size).contentShape(Circle())
        }
        .buttonStyle(.plain).help(label).accessibilityLabel(label)
        return button.glassEffect(glass ? .regular.tint(model.colors[0].opacity(0.13)).interactive() : .identity,
                                  in: Circle())
    }

    private func lyricView(_ track: Track) -> some View {
        Group {
            if model.lyrics.isEmpty {
                Text(model.lyricsLoading ? "正在查找同步歌词…" : "暂无同步歌词")
                    .font(lumiFont(18, weight: .medium)).foregroundStyle(.secondary)
                    .frame(maxWidth: .infinity, maxHeight: .infinity, alignment: .leading)
            } else {
                GeometryReader { geometry in
                    ScrollViewReader { reader in
                        TimelineView(.periodic(from: .now, by: 0.14)) { context in
                            let position = track.position(at: context.date) + model.offset
                            let current = model.lyrics.lastIndex { $0.time <= position }
                            ScrollView {
                                LazyVStack(alignment: .leading, spacing: 16) {
                                    ForEach(Array(model.lyrics.enumerated()), id: \.element.id) { index, line in
                                        Text(line.text)
                                            .font(lumiFont(25, weight: .semibold))
                                            .foregroundStyle(.primary)
                                            .opacity(current == index ? 1 : 0.53)
                                            .fixedSize(horizontal: false, vertical: true)
                                            .frame(maxWidth: .infinity, alignment: .leading)
                                            .accessibilityAddTraits(current == index ? .isSelected : [])
                                            .id(line.id)
                                    }
                                }
                                .padding(.top, geometry.size.height * 0.27)
                                .padding(.bottom, geometry.size.height * 0.65)
                            }
                            .scrollIndicators(.hidden)
                            .onChange(of: current, initial: true) { old, new in
                                guard let new, model.lyrics.indices.contains(new) else { return }
                                let anchor = UnitPoint(x: 0, y: 0.29)
                                if old == new || reduceMotion {
                                    reader.scrollTo(model.lyrics[new].id, anchor: anchor)
                                } else {
                                    withAnimation(.easeInOut(duration: 0.26)) {
                                        reader.scrollTo(model.lyrics[new].id, anchor: anchor)
                                    }
                                }
                            }
                        }
                    }
                }
                .id(track.key)
                .mask {
                    LinearGradient(stops: [.init(color: .clear, location: 0),
                                           .init(color: .black.opacity(0.32), location: 0.10),
                                           .init(color: .black, location: 0.25),
                                           .init(color: .black, location: 0.61),
                                           .init(color: .black.opacity(0.40), location: 0.83),
                                           .init(color: .clear, location: 1)],
                                   startPoint: .top, endPoint: .bottom)
                }
            }
        }
    }

}

private struct PlaybackProgress: View {
    let track: Track
    let tint: Color
    let seek: (Double) -> Void
    @ViewState private var dragPosition: Double?
    @ViewState private var dragging = false

    var body: some View {
        TimelineView(.periodic(from: .now, by: 0.25)) { context in
            let position = track.position(at: context.date)
            HStack(spacing: 8) {
                Slider(value: Binding(get: { dragPosition ?? position }, set: { value in
                    if dragging { dragPosition = value }
                    else { seek(value) }
                }), in: 0...max(1, track.duration)) { editing in
                    dragging = editing
                    if !editing, let value = dragPosition {
                        dragPosition = nil
                        seek(value)
                    }
                }
                .accessibilityLabel("播放进度").tint(tint).disabled(track.duration <= 0 || !track.canSeek)
                Text((track.estimatedPosition ? "≈ " : "") + "\(timeLabel(dragPosition ?? position)) / \(timeLabel(track.duration))")
            }.font(.caption2.monospacedDigit()).foregroundStyle(.secondary)
        }
    }

    private func timeLabel(_ seconds: Double) -> String {
        let value = max(0, Int(seconds))
        return String(format: "%d:%02d", value / 60, value % 60)
    }
}

private struct ScrollingTitle: View {
    let text: String
    let scroll: Bool
    let size: CGFloat
    @ViewState private var began = Date.now

    var body: some View {
        GeometryReader { geometry in
            let font = NSFont(name: "Geist-Regular", size: size) ?? .systemFont(ofSize: size, weight: .semibold)
            let width = (text as NSString).size(withAttributes: [.font: font]).width.rounded(.up)
            let overflow = width - geometry.size.width
            if scroll && overflow > 2 {
                TimelineView(.animation(minimumInterval: 1 / 30)) { context in
                    let trip = Double(overflow) / 24
                    let phase = max(0, context.date.timeIntervalSince(began)).truncatingRemainder(dividingBy: 3 + trip)
                    let offset = min(Double(overflow), max(0, phase - 1.5) * 24)
                    Text(text).fixedSize().offset(x: -offset)
                }
                .mask {
                    LinearGradient(stops: [.init(color: .black, location: 0.90), .init(color: .clear, location: 1)],
                                   startPoint: .leading, endPoint: .trailing)
                }
            } else {
                Text(text).lineLimit(1)
            }
        }
        .frame(height: size + 5)
        .font(lumiFont(size, weight: .semibold))
        .onChange(of: text) { _, _ in began = .now }
        .accessibilityLabel(text)
    }
}
