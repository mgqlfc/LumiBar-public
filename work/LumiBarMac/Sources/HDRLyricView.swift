import AppKit
import QuartzCore

@MainActor
final class HDRLyricView: NSView {
    private static let linearRGB = CGColorSpace(name: CGColorSpace.extendedLinearSRGB)!
    private static let headroom: Float = 1.4
    private let inkLayer = CALayer()

    override init(frame: NSRect) {
        super.init(frame: frame)
        wantsLayer = true
        setAccessibilityElement(false)
        inkLayer.preferredDynamicRange = .constrainedHigh
        layer?.addSublayer(inkLayer)
    }

    required init?(coder: NSCoder) { nil }

    override func hitTest(_ point: NSPoint) -> NSView? { nil }

    func render(size: CGSize, color: NSColor, drawing: (CGContext, CGColor) -> Void) {
        let screen = window?.screen ?? NSScreen.main
        guard let screen, screen.maximumPotentialExtendedDynamicRangeColorComponentValue > 1,
              size.width > 0, size.height > 0,
              let linear = color.cgColor.converted(to: Self.linearRGB, intent: .relativeColorimetric, options: nil),
              let components = linear.components, components.count == 4 else {
            clear()
            return
        }
        let maximum = components.prefix(3).max() ?? 0
        guard maximum > 0 else { clear(); return }
        let gain = CGFloat(Self.headroom) / maximum
        let scale = window?.backingScaleFactor ?? screen.backingScaleFactor
        let pixelWidth = ceil(size.width * scale)
        let pixelHeight = ceil(size.height * scale)
        guard pixelWidth.isFinite, pixelHeight.isFinite, pixelWidth * pixelHeight <= 2_000_000 else {
            clear()
            return
        }
        let bitmap = CGBitmapInfo.floatComponents.rawValue | CGBitmapInfo.byteOrder16Little.rawValue
            | CGImageAlphaInfo.premultipliedLast.rawValue
        guard let context = CGContext(data: nil, width: Int(pixelWidth), height: Int(pixelHeight),
                                      bitsPerComponent: 16, bytesPerRow: 0, space: Self.linearRGB, bitmapInfo: bitmap),
              let ink = CGColor(colorSpace: Self.linearRGB, components: [
                components[0] * gain, components[1] * gain, components[2] * gain, 1
              ]) else { clear(); return }
        context.scaleBy(x: scale, y: scale)
        drawing(context, ink)
        guard let frame = context.makeImage(),
              let image = CGImageCreateCopyWithContentHeadroom(Self.headroom, frame) else {
            clear()
            return
        }
        CATransaction.begin()
        CATransaction.setDisableActions(true)
        inkLayer.frame = CGRect(origin: .zero, size: size)
        inkLayer.contentsScale = scale
        inkLayer.contents = image
        CATransaction.commit()
    }

    func clear() {
        guard inkLayer.contents != nil else { return }
        CATransaction.begin()
        CATransaction.setDisableActions(true)
        inkLayer.contents = nil
        CATransaction.commit()
    }
}
