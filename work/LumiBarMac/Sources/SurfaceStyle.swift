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

enum MenuBarCapsuleShape {
    static func outline(height: CGFloat) -> RoundedRectangle {
        RoundedRectangle(cornerRadius: height * 0.43)
    }

    static func movingOutline(in bounds: CGRect, progress: CGFloat, gap: CGFloat) -> Path {
        let middle = bounds.midX
        let halfHeight = bounds.height / 2
        let halfGap = gap / 2
        let halfWidth = bounds.width / 2 - halfGap
        let capsule = outline(height: bounds.height)
        var path = capsule.path(in: CGRect(x: bounds.minX, y: bounds.minY, width: halfWidth, height: bounds.height))
        path.addPath(capsule.path(in: CGRect(x: middle + halfGap, y: bounds.minY, width: halfWidth, height: bounds.height)))
        guard progress < 0.76 else { return path }

        // The bridge pinches to a single point before the two rounded ends move apart.
        let shoulder = bounds.height * 0.56
        let neck = halfHeight * (1 - pow(progress / 0.76, 1.5))
        let left = middle - shoulder
        let right = middle + shoulder
        let inner = shoulder * 0.42
        let bottom = bounds.midY - neck
        let top = bounds.midY + neck
        var bridge = Path()
        bridge.move(to: CGPoint(x: left, y: bounds.minY))
        bridge.addQuadCurve(to: CGPoint(x: middle, y: bottom), control: CGPoint(x: middle - inner, y: bottom))
        bridge.addQuadCurve(to: CGPoint(x: right, y: bounds.minY), control: CGPoint(x: middle + inner, y: bottom))
        bridge.addLine(to: CGPoint(x: right, y: bounds.maxY))
        bridge.addQuadCurve(to: CGPoint(x: middle, y: top), control: CGPoint(x: middle + inner, y: top))
        bridge.addQuadCurve(to: CGPoint(x: left, y: bounds.maxY), control: CGPoint(x: middle - inner, y: top))
        bridge.closeSubpath()
        return path.union(bridge)
    }
}

struct MenuBarCapsuleChrome: ViewModifier {
    let height: CGFloat
    @Environment(\.colorScheme) private var scheme

    func body(content: Content) -> some View {
        let shape = MenuBarCapsuleShape.outline(height: height)
        content
            .background(shape.fill(scheme == .dark ? Color(red: 0.24, green: 0.29, blue: 0.37).opacity(0.70) : .white.opacity(0.74)))
            .clipShape(shape)
            .overlay {
                shape.strokeBorder(.linearGradient(colors: [.white.opacity(scheme == .dark ? 0.20 : 0.76),
                                                            .white.opacity(scheme == .dark ? 0.04 : 0.16)],
                                                    startPoint: .top, endPoint: .bottom), lineWidth: 1)
            }
            .accessibilityHidden(true)
    }
}

struct WeatherCapsule: View {
    let model: WeatherModel
    let appearance: AppearanceModel
    let height: CGFloat
    let width: CGFloat
    let showsPrecipitation: Bool
    @Environment(\.colorScheme) private var scheme
    @Environment(\.accessibilityReduceMotion) private var reduceMotion

    private var mainSize: CGFloat { min(22, height - 3) }
    private var rainText: String? {
        guard showsPrecipitation, let rain = model.weather?.rain else { return nil }
        return rain.formatted(.number.precision(.fractionLength(0))) + "%"
    }

    var body: some View {
        HStack(spacing: 3) {
            WeatherIcon(code: model.weather?.code ?? 2, day: model.weather?.isDay, size: mainSize, centersArtwork: true)
            if let weather = model.weather {
                appearance.styledText(model.temperature(weather.temperature), size: 15, weight: .semibold)
                    .lineLimit(1).minimumScaleFactor(0.60)
                    .layoutPriority(1)
            }
            if showsPrecipitation, model.weather != nil { Spacer(minLength: 1) }
            if let rainText {
                HStack(spacing: 2) {
                    WeatherIcon(code: 61, day: true, size: min(14, height - 5), centersArtwork: true)
                    appearance.styledText(rainText, size: 11).opacity(0.85)
                        .lineLimit(1).minimumScaleFactor(0.75)
                }
            }
        }
        .padding(.horizontal, showsPrecipitation ? 8 : 4)
        .frame(width: width, height: height)
        .background {
            if let weather = model.weather {
                let moving = appearance.animation && !reduceMotion
                TimelineView(.animation(minimumInterval: 1 / 18, paused: !moving)) { context in
                    let color = WeatherLight.colors(code: weather.code, day: weather.isDay).0
                    let iconCenter = (showsPrecipitation ? 8 : 4) + mainSize / 2
                    let radius = width * 0.69 - iconCenter
                    let strength = scheme == .dark ? 0.30 : 0.36
                    let phase = context.date.timeIntervalSinceReferenceDate
                    let drift = moving ? sin(phase * 2 * .pi / 17) * 0.05 : 0
                    RadialGradient(colors: [color.opacity(strength), color.opacity(strength * 0.57), color.opacity(0)],
                                   center: UnitPoint(x: iconCenter / width, y: 0.56 + drift),
                                   startRadius: 0, endRadius: radius)
                }
            }
        }
        .modifier(MenuBarCapsuleChrome(height: height))
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
