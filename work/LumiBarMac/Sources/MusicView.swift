import SwiftUI

struct MusicView: View {
    enum Surface { case leading, trailing, capsule, expanded }
    let model: MusicModel
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
                    HStack(spacing: 9) {
                        cover(size: 22)
                        Text(track.title).font(.caption.weight(.semibold)).lineLimit(1)
                        bars(width: 36)
                    }.padding(.horizontal, 8)
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
            Capsule().fill(.black).overlay(alignment: .bottom) {
                LinearGradient(colors: [model.colors[0].opacity(0.24), model.colors[1].opacity(0.10), .clear],
                               startPoint: .bottomLeading, endPoint: .topTrailing).clipShape(Capsule())
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
            RoundedRectangle(cornerRadius: 20).fill(Color(white: 0.048))
                .overlay {
                    RoundedRectangle(cornerRadius: 20).fill(
                        RadialGradient(colors: [model.colors[0].opacity(0.18), .clear],
                                       center: .leading, startRadius: 0, endRadius: 135))
                }
        }
        .foregroundStyle(.white)
        .environment(\.colorScheme, .dark)
        .padding(6)
    }

    private func lyricsPanel(_ track: Track) -> some View {
        let shape = RoundedRectangle(cornerRadius: 26)
        return VStack(alignment: .leading, spacing: 7) {
            HStack(spacing: 6) {
                Text("\(track.player.name) · \(model.lyricsLoading ? "查找歌词中" : model.lyrics.isEmpty ? "暂无歌词" : "同步歌词")")
                    .font(.caption).foregroundStyle(.secondary).lineLimit(1)
                Spacer(minLength: 0)
                timingButton("慢 0.5s", delta: -0.5)
                timingButton("快 0.5s", delta: 0.5)
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
            .padding(.leading, 8)
            .frame(height: 28)

            lyricView(track)
                .padding(.leading, 8)
                .frame(maxHeight: .infinity)
                .background(alignment: .bottom) { spectrumBackdrop }

            HStack(spacing: 12) {
                cover(size: 58)
                trackNames(track, expanded: true)
                transport(track, compact: false)
            }
            .padding(.top, 4)

            PlaybackProgress(track: track, tint: model.colors[0]) { model.control(.seek($0)) }
                .id(track.key)
        }
        .padding(.horizontal, 21).padding(.vertical, 13)
        .frame(maxWidth: .infinity, maxHeight: .infinity, alignment: .top)
        .background {
            shape.fill(RadialGradient(colors: [
                model.colors[0].opacity(colorScheme == .dark ? 0.23 : 0.16),
                model.colors[1].opacity(colorScheme == .dark ? 0.09 : 0.07), .clear
            ], center: UnitPoint(x: 0.06, y: 0.93), startRadius: 4, endRadius: 235))
        }
        .glassEffect(.regular, in: shape)
        .padding(7)
    }

    private func trackNames(_ track: Track, expanded: Bool) -> some View {
        VStack(alignment: .leading, spacing: 4) {
            ScrollingTitle(text: track.title, scroll: !reduceMotion, size: expanded ? 19 : 14)
            Text(track.artist.isEmpty ? track.player.name : track.artist)
                .font(.system(size: expanded ? 13 : 11)).foregroundStyle(.secondary).lineLimit(1)
            if expanded && !track.album.isEmpty {
                Text(track.album).font(.system(size: 11)).foregroundStyle(.secondary).lineLimit(1)
            }
        }.frame(maxWidth: .infinity, alignment: .leading)
    }

    private func transport(_ track: Track, compact: Bool) -> some View {
        GlassEffectContainer(spacing: 6) {
            HStack(spacing: 3) {
                icon("backward.end.fill", "上一首", size: 27) { model.control(.previous) }
                icon(track.playing ? "pause.fill" : "play.fill", track.playing ? "暂停" : "播放",
                     size: compact ? 33 : 41, glass: true) { model.control(.toggle) }
                icon("forward.end.fill", "下一首", size: 27) { model.control(.next) }
            }
        }
    }

    private func timingButton(_ label: String, delta: Double) -> some View {
        Button { model.adjustOffset(delta) } label: {
            Text(label).font(.caption.monospacedDigit())
                .padding(.horizontal, 5).frame(height: 25).contentShape(Rectangle())
        }
        .buttonStyle(.plain)
        .foregroundStyle(.secondary)
        .help(String(format: "本曲歌词偏移 %+.1f 秒", model.offset))
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
        TimelineView(.animation(minimumInterval: 1 / 25, paused: !model.spectrumEnabled || model.track?.playing != true)) { _ in
            let levels = model.track?.playing == true ? model.spectrum.levels : Array(repeating: 0.0, count: 8)
            HStack(alignment: .center, spacing: 2) {
                ForEach(Array(levels.enumerated()), id: \.offset) { index, level in
                    Capsule().fill(model.colors[index < 4 ? 0 : 1].opacity(level > 0 ? 0.92 : 0.46))
                        .frame(height: 2 + 13 * level)
                }
            }.frame(width: width, height: 16)
        }.accessibilityHidden(true)
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

    private var spectrumBackdrop: some View {
        TimelineView(.animation(minimumInterval: 1 / 25, paused: !model.spectrumEnabled || model.track?.playing != true)) { _ in
            let levels = model.track?.playing == true ? model.spectrum.levels : []
            HStack(alignment: .bottom, spacing: 5) {
                ForEach(Array(levels.enumerated()), id: \.offset) { index, level in
                    RoundedRectangle(cornerRadius: 5)
                        .fill(.linearGradient(colors: [model.colors[index < 4 ? 0 : 1].opacity(0.19), .clear],
                                              startPoint: .top, endPoint: .bottom))
                        .frame(height: 75 * level)
                }
            }.frame(height: 78, alignment: .bottom)
        }.accessibilityHidden(true).allowsHitTesting(false)
    }

    private func lyricView(_ track: Track) -> some View {
        Group {
            if model.lyrics.isEmpty {
                Text(model.lyricsLoading ? "正在查找同步歌词…" : "暂无同步歌词")
                    .font(.system(size: 18, weight: .medium)).foregroundStyle(.secondary)
                    .frame(maxWidth: .infinity, maxHeight: .infinity, alignment: .leading)
            } else {
                TimelineView(.periodic(from: .now, by: 0.15)) { context in
                    let position = track.position(at: context.date) + model.offset
                    let current = model.lyrics.lastIndex { $0.time <= position }
                    let center = current ?? 0
                    let bounds = max(0, center - 1)..<min(model.lyrics.count, center + 3)
                    VStack(alignment: .leading, spacing: 10) {
                        ForEach(bounds, id: \.self) { index in
                            Text(model.lyrics[index].text)
                                .font(.system(size: current == index ? 25 : 16,
                                              weight: current == index ? .semibold : .medium))
                                .foregroundStyle(current == index ? Color.primary : Color.secondary)
                                .opacity(current == index ? 1 : 0.69)
                                .lineLimit(current == index ? 2 : 1)
                        }
                    }
                    .frame(maxWidth: .infinity, maxHeight: .infinity, alignment: .leading)
                    .mask {
                        LinearGradient(stops: [.init(color: .clear, location: 0), .init(color: .black, location: 0.12),
                                               .init(color: .black, location: 0.84), .init(color: .clear, location: 1)],
                                       startPoint: .top, endPoint: .bottom)
                    }
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
                .accessibilityLabel("播放进度").tint(tint).disabled(track.duration <= 0)
                Text("\(timeLabel(dragPosition ?? position)) / \(timeLabel(track.duration))")
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
            let font = NSFont.systemFont(ofSize: size, weight: .semibold)
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
        .font(.system(size: size, weight: .semibold))
        .onChange(of: text) { _, _ in began = .now }
        .accessibilityLabel(text)
    }
}
