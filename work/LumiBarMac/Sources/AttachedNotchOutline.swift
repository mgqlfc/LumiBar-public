import SwiftUI

struct NotchGlassInsets: Equatable {
    let outset: CGFloat

    init(system: Bool) {
        outset = system ? 28 : 0
    }

    func windowFrame(around body: CGRect) -> CGRect {
        CGRect(x: body.minX - outset, y: body.minY - outset,
               width: body.width + 2 * outset, height: body.height + outset)
    }

    func bodyFrame(in window: CGRect) -> CGRect {
        CGRect(x: window.minX + outset, y: window.minY + outset,
               width: max(0, window.width - 2 * outset), height: max(0, window.height - outset))
    }

    func shapeRect(in rect: CGRect) -> CGRect {
        // Leave room for the side and bottom shadows while keeping the glass attached to the screen top.
        CGRect(x: rect.minX + outset, y: rect.minY,
               width: max(0, rect.width - 2 * outset), height: max(0, rect.height - outset))
    }
}

struct AttachedNotchOutline: Shape {
    var openness: CGFloat
    let collapsedWidth: CGFloat
    let collapsedHeight: CGFloat
    let squareTop: Bool
    var roundsTop = false
    var glassInsets = NotchGlassInsets(system: false)

    var animatableData: CGFloat {
        get { openness }
        set { openness = newValue }
    }

    func atOpenness(_ value: CGFloat) -> Self {
        var shape = self
        shape.openness = value
        return shape
    }

    func path(in bounds: CGRect) -> Path {
        let rect = glassInsets.shapeRect(in: bounds)
        guard !rect.isNull, !rect.isInfinite, rect.width > 0, rect.height > 0 else { return Path() }

        let spring = openness.isFinite ? max(0, openness) : 0
        let progress = min(1, spring)
        let overshoot = min(0.20, spring - progress)
        let compactWidth = collapsedWidth.isFinite ? min(rect.width, max(1, collapsedWidth)) : rect.width
        let compactHeight = collapsedHeight.isFinite ? min(rect.height, max(1, collapsedHeight)) : rect.height

        let heightProgress = 1 - pow(1 - progress, 1.24)
        let widthProgress = progress * progress * (3 - 2 * progress)
        let height = compactHeight + (rect.height - compactHeight) * heightProgress
        let width = compactWidth + (rect.width - compactWidth) * widthProgress
        let body = CGRect(x: rect.midX - width / 2, y: rect.minY, width: width, height: height)
        let radius = min(16 + progress * 6 + overshoot * 24, width / 2, height / 2)
        guard squareTop else {
            return RoundedRectangle(cornerRadius: radius, style: .continuous).path(in: body)
        }

        let topRadius = roundsTop ? radius : 0
        var outline = Path()
        outline.move(to: CGPoint(x: body.minX + topRadius, y: body.minY))
        outline.addLine(to: CGPoint(x: body.maxX - topRadius, y: body.minY))
        if roundsTop {
            outline.addQuadCurve(to: CGPoint(x: body.maxX, y: body.minY + topRadius),
                                 control: CGPoint(x: body.maxX, y: body.minY))
        }
        outline.addLine(to: CGPoint(x: body.maxX, y: body.maxY - radius))
        outline.addQuadCurve(to: CGPoint(x: body.maxX - radius, y: body.maxY),
                             control: CGPoint(x: body.maxX, y: body.maxY))
        outline.addLine(to: CGPoint(x: body.minX + radius, y: body.maxY))
        outline.addQuadCurve(to: CGPoint(x: body.minX, y: body.maxY - radius),
                             control: CGPoint(x: body.minX, y: body.maxY))
        if roundsTop {
            outline.addLine(to: CGPoint(x: body.minX, y: body.minY + topRadius))
            outline.addQuadCurve(to: CGPoint(x: body.minX + topRadius, y: body.minY),
                                 control: CGPoint(x: body.minX, y: body.minY))
        }
        outline.closeSubpath()
        return outline
    }
}

struct CompactNotchOutline: Equatable {
    let cameraSize: CGSize
    let roundsTop: Bool

    func path(in bounds: CGRect, openness: CGFloat) -> CGPath {
        let amount = min(1, max(0, openness))
        let dropletWidth = min(bounds.width, cameraSize.width * 0.30)
        let dropletHeight = min(bounds.height, cameraSize.height * 0.38)
        let width = dropletWidth + (bounds.width - dropletWidth) * pow(amount, 1.28)
        let height = dropletHeight + (bounds.height - dropletHeight) * pow(amount, 0.94)
        let inset = (cameraSize.height - dropletHeight) / 2 * pow(1 - amount, 3)
        let top = bounds.maxY - inset
        let bottom = top - height
        let left = bounds.midX - width / 2
        let right = bounds.midX + width / 2

        // Pinch the sides toward the camera while keeping the lower glass edge round.
        let pinch = 4 * amount * (1 - amount)
        let shoulder = width * 0.17 * pinch
        let topLeft = left + shoulder
        let topRight = right - shoulder
        let restingCorner = roundsTop ? CGFloat(16) : 0
        let topRadius = min((topRight - topLeft) / 2, height / 2,
                            restingCorner * amount + dropletHeight / 2 * (1 - amount) + 8 * pinch)
        let bottomRadius = min(width / 2, height / 2, 16 + 14 * pinch)
        let upperY = top - topRadius
        let lowerY = bottom + bottomRadius
        let bend = (upperY - lowerY) * 0.42

        let path = CGMutablePath()
        path.move(to: CGPoint(x: topLeft + topRadius, y: top))
        path.addQuadCurve(to: CGPoint(x: topLeft, y: upperY), control: CGPoint(x: topLeft, y: top))
        path.addCurve(to: CGPoint(x: left, y: lowerY), control1: CGPoint(x: topLeft, y: upperY - bend),
                      control2: CGPoint(x: left, y: lowerY + bend))
        path.addQuadCurve(to: CGPoint(x: left + bottomRadius, y: bottom), control: CGPoint(x: left, y: bottom))
        path.addLine(to: CGPoint(x: right - bottomRadius, y: bottom))
        path.addQuadCurve(to: CGPoint(x: right, y: lowerY), control: CGPoint(x: right, y: bottom))
        path.addCurve(to: CGPoint(x: topRight, y: upperY), control1: CGPoint(x: right, y: lowerY + bend),
                      control2: CGPoint(x: topRight, y: upperY - bend))
        path.addQuadCurve(to: CGPoint(x: topRight - topRadius, y: top), control: CGPoint(x: topRight, y: top))
        path.closeSubpath()
        return path
    }
}

struct CompactNotchGlassShape: Shape {
    let outline: CompactNotchOutline
    var openness: CGFloat
    let glassInsets: NotchGlassInsets

    var animatableData: CGFloat {
        get { openness }
        set { openness = newValue }
    }

    func path(in bounds: CGRect) -> Path {
        let rect = glassInsets.shapeRect(in: bounds)
        guard !rect.isEmpty else { return Path() }
        let bounds = CGRect(origin: .zero, size: rect.size)
        let path = outline.path(in: bounds, openness: openness)
        return Path(path).applying(CGAffineTransform(a: 1, b: 0, c: 0, d: -1, tx: rect.minX, ty: rect.maxY))
    }
}
