import SwiftUI

struct FlyoutChrome: ViewModifier {
    let appearance: AppearanceModel
    @Environment(\.colorScheme) private var scheme

    func body(content: Content) -> some View {
        let shape = RoundedRectangle(cornerRadius: 12)
        content
            .background(shape.fill(appearance.tint(scheme)))
            .clipShape(shape)
            .glassEffect(.regular, in: shape)
            .shadow(color: .black.opacity(scheme == .dark ? 0.32 : 0.18), radius: 7, x: 0, y: 2)
            .padding(16)
    }
}

// Keep the status item as the accessible click target and let its child draw.
final class StatusContent<Content: View>: NSHostingView<Content> {
    override func hitTest(_ point: NSPoint) -> NSView? { nil }
}

struct WeatherCapsule: View {
    let model: WeatherModel
    let appearance: AppearanceModel
    let lite: Bool
    let height: CGFloat
    @Environment(\.colorScheme) private var scheme
    @Environment(\.accessibilityReduceMotion) private var reduceMotion

    private var mainSize: CGFloat { min(lite ? 22 : 24, height - 3) }
    private var gap: CGFloat { lite ? 3 : 4 }
    private var rainText: String? {
        guard !lite, let rain = model.weather?.rain else { return nil }
        return rain.formatted(.number.precision(.fractionLength(0))) + "%"
    }

    var idealWidth: CGFloat {
        guard let weather = model.weather else { return max(28, mainSize + 10) }
        let tempWidth = appearance.textWidth(model.temperature(weather.temperature), size: 15, semibold: true)
        let rainWidth = rainText.map { gap + min(14, height - 6) + 2 + appearance.textWidth($0, size: 11) } ?? 0
        return max(lite ? 64 : 104, ceil(20 + mainSize + gap + tempWidth + rainWidth))
    }

    var body: some View {
        let shape = Capsule()
        HStack(spacing: gap) {
            WeatherIcon(code: model.weather?.code ?? 2, day: model.weather?.isDay, size: mainSize)
            if let weather = model.weather {
                appearance.styledText(model.temperature(weather.temperature), size: 15, weight: .semibold)
                    .fixedSize(horizontal: true, vertical: false)
                if let rainText {
                    HStack(spacing: 2) {
                        WeatherIcon(code: 61, day: true, size: min(14, height - 6)).accessibilityHidden(true)
                        appearance.styledText(rainText, size: 11).opacity(0.85)
                            .fixedSize(horizontal: true, vertical: false)
                    }
                }
            }
        }
        .frame(width: idealWidth, height: height)
        .background {
            shape.fill(scheme == .dark ? Color(red: 0.24, green: 0.29, blue: 0.37).opacity(0.70) : .white.opacity(0.55))
            if let weather = model.weather {
                let moving = appearance.animation && !reduceMotion
                TimelineView(.animation(minimumInterval: 1 / 18, paused: !moving)) { context in
                    let tones = WeatherLight.colors(code: weather.code, day: weather.isDay)
                    let phase = context.date.timeIntervalSinceReferenceDate
                    let drift = moving ? sin(phase * 2 * .pi / 17) * 0.027 : 0
                    RadialGradient(colors: [tones.0.opacity(scheme == .dark ? 0.31 : 0.16), .clear],
                                   center: UnitPoint(x: 0.27 + drift, y: 0.70),
                                   startRadius: 0, endRadius: 58)
                    RadialGradient(colors: [tones.1.opacity(scheme == .dark ? 0.26 : 0.14), .clear],
                                   center: UnitPoint(x: 0.81 - drift, y: 0.41),
                                   startRadius: 0, endRadius: 58)
                }
                .clipShape(shape)
            }
        }
        .overlay {
            shape.strokeBorder(.linearGradient(colors: [.white.opacity(scheme == .dark ? 0.20 : 0.76),
                                                        .white.opacity(scheme == .dark ? 0.04 : 0.16)],
                                                startPoint: .top, endPoint: .bottom), lineWidth: 1)
        }
        .accessibilityHidden(true)
    }
}

enum WeatherLight {
    static func colors(code: Int, day: Bool?) -> (Color, Color) {
        let tones: (UInt32, UInt32) = switch code {
        case -2: (0xE0A957, 0xB77D45)
        case -1: (0xB4A47E, 0x97A2A5)
        case 95...99: (0xA579EA, 0x5790EE)
        case 71...77, 85...86: (0x7DD3EE, 0xABB8EA)
        case 45, 48: (0x8EB8CF, 0x97BFD0)
        case 51...67, 80...82: (0x41B5EF, 0x4886DB)
        case 3: (0x88BADD, 0x7CA7CB)
        case 2 where day != false: (0xFACA71, 0x97CAE1)
        default: day == false ? (0x557DF4, 0x938BEB) : (0xFFCA5B, 0xFFAD4E)
        }
        return (AccentPalette.color(tones.0), AccentPalette.color(tones.1))
    }
}
