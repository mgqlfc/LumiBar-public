import AppKit
import SwiftUI

struct SpectrumInk {
    let color: Color
    let opacity: Double

    @MainActor
    init(cover: Color, primaryGlow: Color, secondaryGlow: Color, strength: Double, light: Bool) {
        let strength = min(1, max(0, strength))
        let opacity = 0.56 + strength * 0.24
        let surface = light ? RGB(red: 0.91, green: 0.93, blue: 0.96) : RGB(red: 0.14, green: 0.15, blue: 0.18)
        let lightInk = RGB(red: 1, green: 1, blue: 1)
        let darkInk = RGB(red: 0.03, green: 0.03, blue: 0.03)
        let source = RGB(cover) ?? (light ? darkInk : lightInk)
        let primary = RGB(primaryGlow) ?? source
        let secondary = RGB(secondaryGlow) ?? primary
        let nearPrimary = surface.mixed(with: primary, amount: strength * 0.80)
        let nearSecondary = surface.mixed(with: secondary, amount: strength * 0.65)
        // The bars sit across the lower glow. Check both nearby peaks, their overlap and a quiet part of the glass.
        let samples = [surface.mixed(with: primary, amount: strength * 0.28), nearPrimary, nearSecondary,
                       nearPrimary.mixed(with: secondary, amount: strength * 0.20)]
        let backgrounds = samples.map { ($0, $0.luminance) }
        let directions = light ? [darkInk, lightInk] : [lightInk, darkInk]

        var best = source
        var bestContrast = 0.0
        for step in 0...12 {
            for direction in directions {
                let candidate = source.mixed(with: direction, amount: Double(step) / 12)
                let contrast = backgrounds.map { background, luminance in
                    let composite = background.mixed(with: candidate, amount: opacity).luminance
                    return (max(composite, luminance) + 0.05) / (min(composite, luminance) + 0.05)
                }.min() ?? 0
                if contrast >= 2.25 {
                    self.color = candidate.color
                    self.opacity = opacity
                    return
                }
                if contrast > bestContrast {
                    best = candidate
                    bestContrast = contrast
                }
            }
        }
        self.color = best.color
        self.opacity = opacity
    }

    private struct RGB {
        let red: Double
        let green: Double
        let blue: Double

        init(red: Double, green: Double, blue: Double) {
            self.red = red
            self.green = green
            self.blue = blue
        }

        @MainActor
        init?(_ color: Color) {
            guard let resolved = NSColor(color).usingColorSpace(.sRGB) else { return nil }
            red = min(1, max(0, resolved.redComponent))
            green = min(1, max(0, resolved.greenComponent))
            blue = min(1, max(0, resolved.blueComponent))
        }

        var color: Color { Color(red: red, green: green, blue: blue) }

        var luminance: Double {
            func linear(_ channel: Double) -> Double {
                channel <= 0.04045 ? channel / 12.92 : pow((channel + 0.055) / 1.055, 2.4)
            }
            return 0.2126 * linear(red) + 0.7152 * linear(green) + 0.0722 * linear(blue)
        }

        func mixed(with other: Self, amount: Double) -> Self {
            RGB(red: red + (other.red - red) * amount,
                green: green + (other.green - green) * amount,
                blue: blue + (other.blue - blue) * amount)
        }
    }
}

struct SpectrumColumns: View {
    let levels: [Double]
    let color: Color
    let opacity: Double

    var body: some View {
        Canvas { context, size in
            guard !levels.isEmpty else { return }
            let count = 40
            let step = size.width / Double(count)
            let barWidth = max(0.8, step * 0.46)
            let gradient = Gradient(colors: [color.opacity(opacity), .clear])
            for index in 0..<count {
                let position = Double(index) * Double(levels.count - 1) / Double(count - 1)
                let lower = Int(position)
                let upper = min(lower + 1, levels.count - 1)
                let fraction = position - Double(lower)
                let level = levels[lower] * (1 - fraction) + levels[upper] * fraction
                let height = min(size.height, max(1, level * size.height))
                let rect = CGRect(x: step * Double(index) + (step - barWidth) / 2,
                                  y: size.height - height, width: barWidth, height: height)
                context.fill(Path(roundedRect: rect, cornerRadius: barWidth / 2),
                             with: .linearGradient(gradient, startPoint: CGPoint(x: 0, y: rect.minY),
                                                   endPoint: CGPoint(x: 0, y: size.height)))
            }
        }
        .allowsHitTesting(false)
    }
}
