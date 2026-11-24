import AppKit
import SwiftUI

extension View {
    /// Apply to a scroll view's content so the attachment can reach its AppKit scroll view.
    func nativeOverlayScrollIndicator() -> some View {
        background(alignment: .topTrailing) {
            NativeOverlayScrollIndicator()
                .frame(width: 0, height: 0)
                .allowsHitTesting(false)
                .accessibilityHidden(true)
        }
    }
}

private struct NativeOverlayScrollIndicator: NSViewRepresentable {
    @Environment(\.colorScheme) private var scheme

    func makeNSView(context: Context) -> Attachment { Attachment() }

    func updateNSView(_ view: Attachment, context: Context) {
        view.knobStyle = scheme == .dark ? .light : .dark
        view.applyStyle()
    }

    final class Attachment: NSView {
        var knobStyle: NSScroller.KnobStyle = .default

        override func viewDidMoveToWindow() {
            super.viewDidMoveToWindow()
            applyStyle()
        }

        func applyStyle() {
            guard let scrollView = enclosingScrollView else { return }
            if scrollView.scrollerStyle != .overlay { scrollView.scrollerStyle = .overlay }
            if scrollView.scrollerKnobStyle != knobStyle { scrollView.scrollerKnobStyle = knobStyle }
            if let scroller = scrollView.verticalScroller, scroller.controlSize != .small {
                scroller.controlSize = .small
            }
        }
    }
}

struct FlyoutChrome: ViewModifier {
    static let inset: CGFloat = 16
    let appearance: AppearanceModel
    var hasShadow = true
    @Environment(\.colorScheme) private var scheme

    func body(content: Content) -> some View {
        let shape = RoundedRectangle(cornerRadius: 12)
        let surface = content
            .background(shape.fill(appearance.tint(scheme)))
            .clipShape(shape)
            .glassEffect(.regular, in: shape)
        return Group {
            if hasShadow {
                surface.shadow(color: .black.opacity(scheme == .dark ? 0.32 : 0.18), radius: 7, x: 0, y: 2)
            } else {
                surface
            }
        }.padding(Self.inset)
    }
}

enum MenuBarCapsuleShape {
    static let entryHeight: CGFloat = 24
    static let entryGap: CGFloat = 4

    static func outline(height: CGFloat) -> RoundedRectangle {
        RoundedRectangle(cornerRadius: height * 0.49)
    }

    static func movingOutline(leading: CGRect, trailing: CGRect, progress: CGFloat) -> Path {
        var path = outline(height: trailing.height).path(in: trailing)
        guard leading.width > 0 else { return path }
        path.addPath(outline(height: leading.height).path(in: leading))
        guard progress < 0.76 else { return path }

        // The bridge pinches to a single point before the two rounded ends move apart.
        let middle = (leading.maxX + trailing.minX) / 2
        let leftShoulder = min(leading.height * 0.56, leading.width / 2)
        let rightShoulder = min(trailing.height * 0.56, trailing.width / 2)
        let neck = min(leading.height, trailing.height) / 2 * (1 - pow(progress / 0.76, 1.5))
        let left = middle - leftShoulder
        let right = middle + rightShoulder
        let leftControl = leftShoulder * 0.42
        let rightControl = rightShoulder * 0.42
        let bottom = leading.midY - neck
        let top = leading.midY + neck
        var bridge = Path()
        bridge.move(to: CGPoint(x: left, y: leading.minY))
        bridge.addCurve(to: CGPoint(x: middle, y: bottom), control1: CGPoint(x: left + leftControl, y: leading.minY),
                        control2: CGPoint(x: middle - leftControl, y: bottom))
        bridge.addCurve(to: CGPoint(x: right, y: trailing.minY), control1: CGPoint(x: middle + rightControl, y: bottom),
                        control2: CGPoint(x: right - rightControl, y: trailing.minY))
        bridge.addLine(to: CGPoint(x: right, y: trailing.maxY))
        bridge.addCurve(to: CGPoint(x: middle, y: top), control1: CGPoint(x: right - rightControl, y: trailing.maxY),
                        control2: CGPoint(x: middle + rightControl, y: top))
        bridge.addCurve(to: CGPoint(x: left, y: leading.maxY), control1: CGPoint(x: middle - leftControl, y: top),
                        control2: CGPoint(x: left + leftControl, y: leading.maxY))
        bridge.closeSubpath()
        return path.union(bridge)
    }
}

enum MenuBarCapsuleLayer {
    case complete, surface, content
}

private struct MenuBarCapsuleLayerKey: EnvironmentKey {
    static let defaultValue = MenuBarCapsuleLayer.complete
}

extension EnvironmentValues {
    var menuBarCapsuleLayer: MenuBarCapsuleLayer {
        get { self[MenuBarCapsuleLayerKey.self] }
        set { self[MenuBarCapsuleLayerKey.self] = newValue }
    }
}

struct MenuBarCapsuleChrome: ViewModifier {
    let height: CGFloat
    @Environment(\.menuBarCapsuleLayer) private var layer
    @Environment(\.colorScheme) private var scheme

    func body(content: Content) -> some View {
        let surface = content.background(scheme == .dark ? Color(red: 0.24, green: 0.29, blue: 0.37).opacity(0.70)
                                                        : .white.opacity(0.74))
        Group {
            switch layer {
            case .complete:
                let shape = MenuBarCapsuleShape.outline(height: height)
                let opacity = Self.borderOpacity(for: scheme)
                surface.clipShape(shape).overlay {
                    shape.strokeBorder(.linearGradient(colors: [.white.opacity(opacity.top), .white.opacity(opacity.bottom)],
                                                        startPoint: .top, endPoint: .bottom), lineWidth: 1)
                }
            case .surface:
                surface
            case .content:
                content
            }
        }.accessibilityHidden(true)
    }

    nonisolated private static func borderOpacity(for scheme: ColorScheme) -> (top: Double, bottom: Double) {
        scheme == .dark ? (0.20, 0.04) : (0.76, 0.16)
    }

    nonisolated static func draw(outline: Path, scheme: ColorScheme, in context: CGContext, contents: () -> Void) {
        let path = outline.cgPath
        context.saveGState()
        defer { context.restoreGState() }
        context.addPath(path)
        context.clip()
        contents()

        let opacity = borderOpacity(for: scheme)
        let colors = [CGColor(gray: 1, alpha: opacity.top), CGColor(gray: 1, alpha: opacity.bottom)]
        guard let border = CGGradient(colorsSpace: CGColorSpaceCreateDeviceGray(), colors: colors as CFArray,
                                      locations: [0, 1]) else { return }
        context.addPath(path)
        context.setLineWidth(2)
        context.replacePathWithStrokedPath()
        context.clip()
        let bounds = outline.boundingRect
        context.drawLinearGradient(border, start: CGPoint(x: bounds.midX, y: bounds.maxY),
                                   end: CGPoint(x: bounds.midX, y: bounds.minY), options: [])
    }
}

struct MenuBarCapsuleImage {
    let surface: NSImage
    let content: NSImage
    var size: CGSize { surface.size }
}

struct MenuBarCapsuleTransition {
    let frame: CGRect
    let musicFrame: CGRect
    let weatherFrame: CGRect
    let progress: CGFloat

    func draw(wide: MenuBarCapsuleImage?, music: MenuBarCapsuleImage, weather: MenuBarCapsuleImage,
              scheme: ColorScheme, in context: CGContext) {
        let outline = MenuBarCapsuleShape.movingOutline(leading: musicFrame, trailing: weatherFrame, progress: progress)
        MenuBarCapsuleChrome.draw(outline: outline, scheme: scheme, in: context) {
            if wide == nil { weather.surface.draw(in: weatherFrame) }
            if !musicFrame.isEmpty { drawSurfaces(wide: wide, music: music, weather: weather, in: context) }

            let pairContentOpacity = eased((progress - 0.45) / 0.43)
            if let wide {
                let opacity = 1 - eased((progress - 0.07) / 0.38)
                wide.content.draw(in: frame, from: .zero, operation: .sourceOver, fraction: opacity)
            }
            if pairContentOpacity > 0 {
                let contentFrame = wide == nil
                    ? CGRect(x: musicFrame.midX - music.size.width / 2, y: frame.minY,
                             width: music.size.width, height: music.size.height)
                    : musicFrame
                context.saveGState()
                context.clip(to: musicFrame)
                music.content.draw(in: contentFrame, from: .zero, operation: .sourceOver, fraction: pairContentOpacity)
                context.restoreGState()
            }
            let weatherOpacity = wide == nil ? 1 : pairContentOpacity
            weather.content.draw(in: weatherFrame, from: .zero, operation: .sourceOver, fraction: weatherOpacity)
        }
    }

    private func drawSurfaces(wide: MenuBarCapsuleImage?, music: MenuBarCapsuleImage, weather: MenuBarCapsuleImage,
                              in context: CGContext) {
        let mix = eased((progress - 0.14) / 0.74)
        let masks = blendMasks(mix: mix)
        // Blend premultiplied colors in an isolated layer so their combined opacity stays steady.
        context.beginTransparencyLayer(auxiliaryInfo: nil)
        context.saveGState()
        if let masks { context.clip(to: frame, mask: masks.wide) }
        let initialOpacity = masks == nil ? 1 - mix : 1
        if let wide {
            wide.surface.draw(in: frame, from: .zero, operation: .sourceOver, fraction: initialOpacity)
        } else {
            // Extend the stationary weather capsule's edge into the new drop before the cover color takes over.
            let edge = CGRect(x: 0, y: musicFrame.minY, width: 1, height: musicFrame.height)
            weather.surface.draw(in: musicFrame, from: edge, operation: .sourceOver, fraction: initialOpacity)
        }
        context.restoreGState()

        context.saveGState()
        if let masks { context.clip(to: frame, mask: masks.pair) }
        let pairedOpacity = masks == nil ? mix : 1
        music.surface.draw(in: musicFrame, from: .zero, operation: .plusLighter, fraction: pairedOpacity)
        if wide != nil {
            weather.surface.draw(in: weatherFrame, from: .zero, operation: .plusLighter, fraction: pairedOpacity)
        }
        context.restoreGState()
        context.endTransparencyLayer()
    }

    private func blendMasks(mix: CGFloat) -> (wide: CGImage, pair: CGImage)? {
        let count = Int(ceil(frame.width * 2))
        let middle = (musicFrame.maxX + weatherFrame.minX) / 2
        let bridge = 1 - eased((progress - 0.70) / 0.16)
        // While the drops touch, keep the original weather tint through the neck and feather both sides.
        let samples = (0..<count).map { index in
            let x = frame.minX + (CGFloat(index) + 0.5) * frame.width / CGFloat(count)
            let edge = eased(abs(x - middle) / (frame.height / 2))
            return UInt8((255 * mix * (1 - bridge * (1 - edge))).rounded())
        }
        func mask(_ values: [UInt8]) -> CGImage? {
            guard let provider = CGDataProvider(data: Data(values) as CFData) else { return nil }
            return CGImage(maskWidth: count, height: 1, bitsPerComponent: 8, bitsPerPixel: 8, bytesPerRow: count,
                           provider: provider, decode: nil, shouldInterpolate: true)
        }
        // Image masks use inverse alpha, so the two layers cover each pixel with complementary weights.
        guard let wide = mask(samples), let pair = mask(samples.map { 255 - $0 }) else { return nil }
        return (wide, pair)
    }

    private func eased(_ value: CGFloat) -> CGFloat {
        let t = min(1, max(0, value))
        return t * t * (3 - 2 * t)
    }
}

struct WeatherCapsule: View {
    let model: WeatherModel
    let appearance: AppearanceModel
    let height: CGFloat
    let minimumWidth: CGFloat
    let showsPrecipitation: Bool
    var lightTime: TimeInterval? = nil
    var showsSample: Bool = false
    @Environment(\.menuBarCapsuleLayer) private var layer
    @Environment(\.colorScheme) private var scheme

    private var mainSize: CGFloat { min(22, height - 3) }
    private var rainText: String? {
        guard showsPrecipitation,
              let rain = model.weather?.rain ?? (showsSample && model.weather == nil ? 0 : nil) else { return nil }
        return rain.formatted(.number.precision(.fractionLength(0))) + "%"
    }

    var body: some View {
        let forecast = model.weather
        let temperature = forecast?.temperature ?? (showsSample ? 8 : nil)
        let code = forecast?.code ?? (showsSample ? 0 : 2)
        let isDay = forecast?.isDay ?? (showsSample && forecast == nil ? true : nil)
        let tintScale = model.tintScale
        HStack(spacing: 3) {
            WeatherIcon(code: code, day: isDay, size: mainSize, centersArtwork: true)
            if let temperature {
                appearance.styledText(model.temperature(temperature), size: 15, weight: .semibold)
                    .fixedSize()
            }
            if showsPrecipitation, temperature != nil { Spacer(minLength: 1) }
            if let rainText {
                HStack(spacing: 2) {
                    WeatherIcon(code: 61, day: true, size: min(14, height - 5), centersArtwork: true)
                    appearance.styledText(rainText, size: 11).opacity(0.85)
                        .fixedSize()
                }
            }
        }
        .padding(.horizontal, showsPrecipitation ? 8 : 4)
        .frame(minWidth: minimumWidth)
        .frame(height: height)
        .fixedSize(horizontal: true, vertical: false)
        .opacity(layer == .surface ? 0 : 1)
        .background {
            if layer != .content, temperature != nil, tintScale > 0 {
                GeometryReader { geometry in
                    let width = geometry.size.width
                    let (primary, secondary) = WeatherLight.colors(code: code, day: isDay, saturation: model.capsuleTintSaturation)
                    let density = (scheme == .dark ? 0.46 : 0.38) * tintScale
                    let phase = (lightTime ?? 0) * 2 * .pi / 11
                    let sweep = lightTime.map { _ in 0.50 + sin(phase) * 0.32 } ?? (0.18 + mainSize / (2 * width))
                    let drift = lightTime.map { _ in cos(phase) * 0.13 } ?? 0
                    ZStack {
                        LinearGradient(colors: [primary.opacity(density), secondary.opacity(density * 0.83),
                                                primary.opacity(density * 0.72)],
                                       startPoint: .topLeading, endPoint: .bottomTrailing)
                        RadialGradient(colors: [primary.opacity(density), primary.opacity(density * 0.44), .clear],
                                       center: UnitPoint(x: sweep, y: 0.54 + drift),
                                       startRadius: 0, endRadius: max(height, width * 0.58))
                        RadialGradient(colors: [secondary.opacity(density * 0.49), .clear],
                                       center: UnitPoint(x: 0.96 - sweep * 0.46, y: 0.12 - drift),
                                       startRadius: 0, endRadius: max(height, width * 0.48))
                    }
                }
            }
        }
        .modifier(MenuBarCapsuleChrome(height: height))
    }
}

enum WeatherLight {
    static func colors(code: Int, day: Bool?, saturation: Double) -> (Color, Color) {
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
        return (Color.rgb(tones.0, saturation: saturation), Color.rgb(tones.1, saturation: saturation))
    }
}
