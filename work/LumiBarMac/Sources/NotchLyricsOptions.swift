import Foundation

struct NotchLyricsOptions: Codable, Equatable {
    static let hideWhenPausedLabel = "暂停播放时自动隐藏歌词"
    static let hideInFullScreenLabel = "全屏时自动隐藏歌词"
    static let translationOpacityRange = 0.72...0.96
    static let compactWidthRange = 192.0...420.0
    static let compactHeightRange = 64.0...144.0
    static let compactCameraOffsetRange = -12.0...16.0
    static let expandedWidthRange = 360.0...640.0
    static let expandedHeightRange = 380.0...820.0
    static let firstLineExtraSizeRange = 0.0...4.0
    static let glowSpeedRange = 0.5...3.0
    private static let defaultTranslationOpacity = 0.86
    private static let defaultLineSpacing = 0.0

    var enabled = true
    var width = 196.0
    private var savedCompactHeight: Double?
    private var savedCompactCameraOffset: Double?
    private var savedExpandedWidth: Double?
    private var savedExpandedHeight: Double?
    private var savedFirstLineExtraSize: Double?
    var fontSize = 11.5
    private var fontFamily = ""
    private var savedChineseFontFamily: String?
    private var savedWesternFontFamily: String?
    var fontWeight = 700
    var verticalPadding = 3.0
    var lineSpacing = Self.defaultLineSpacing
    var useCoverColors = true
    var lyricColor = "#D7EEE6"
    // Older releases require this key when they read the saved settings.
    private var translationColor = "#A6C4BA"
    private var contextualOpacity: Double?
    var shadow = 0.6
    var showIntroMetadata = true
    var showCountdown = true
    // Keep the saved key readable by older versions during rollback.
    private var countdownSeconds = 3.0
    var showTranslation = true
    var splitLongLines = true
    var karaoke = true
    var glow = true
    private var savedCompactGlowSpeed: Double?
    private var savedExpandedGlowSpeed: Double?
    var theme = "System"
    private var hoverFade: Bool?
    private var hideWhenPaused: Bool?
    private var hideInFullScreen: Bool?
    private var reducedLightTextShadow: Bool?
    private var roundedTopCorners: Bool?
    private var hdrLyricHighlights: Bool?
    private var systemGlassBorder: Bool?
    private var coverTintedBorder: Bool?
    private var savedBorderIntensity: Double?

    var chineseFontFamily: String {
        get { savedChineseFontFamily ?? fontFamily }
        set { savedChineseFontFamily = newValue }
    }

    var westernFontFamily: String {
        get { savedWesternFontFamily ?? fontFamily }
        set { savedWesternFontFamily = newValue }
    }

    var fadesOnHover: Bool {
        get { hoverFade ?? true }
        set { hoverFade = newValue }
    }

    var hidesWhenPaused: Bool {
        get { hideWhenPaused ?? false }
        set { hideWhenPaused = newValue }
    }

    var hidesInFullScreen: Bool {
        get { hideInFullScreen ?? false }
        set { hideInFullScreen = newValue }
    }

    var reducesLightTextShadow: Bool {
        get { reducedLightTextShadow ?? true }
        set { reducedLightTextShadow = newValue }
    }

    var roundsTopCorners: Bool {
        get { roundedTopCorners ?? false }
        set { roundedTopCorners = newValue }
    }

    var usesHDRLyricHighlights: Bool {
        get { hdrLyricHighlights ?? false }
        set { hdrLyricHighlights = newValue }
    }

    var usesSystemGlassBorder: Bool {
        get { systemGlassBorder ?? false }
        set { systemGlassBorder = newValue }
    }

    var tintsBorderFromCover: Bool {
        get { coverTintedBorder ?? false }
        set { coverTintedBorder = newValue }
    }

    var borderIntensity: Double {
        get { Self.bounded(savedBorderIntensity ?? 0.12, in: 0...1, fallback: 0.12) }
        set { savedBorderIntensity = Self.bounded(newValue, in: 0...1, fallback: 0.12) }
    }

    var compactGlowSpeed: Double {
        get { Self.bounded(savedCompactGlowSpeed ?? 1, in: Self.glowSpeedRange, fallback: 1) }
        set { savedCompactGlowSpeed = Self.bounded(newValue, in: Self.glowSpeedRange, fallback: 1) }
    }

    var expandedGlowSpeed: Double {
        get { Self.bounded(savedExpandedGlowSpeed ?? 1, in: Self.glowSpeedRange, fallback: 1) }
        set { savedExpandedGlowSpeed = Self.bounded(newValue, in: Self.glowSpeedRange, fallback: 1) }
    }

    func textShadow(dark: Bool) -> Double {
        shadow * (dark || !reducesLightTextShadow ? 1 : 0.45)
    }

    var compactHeight: Double {
        get { Self.bounded(savedCompactHeight ?? 84, in: Self.compactHeightRange, fallback: 84) }
        set { savedCompactHeight = Self.bounded(newValue, in: Self.compactHeightRange, fallback: 84) }
    }

    var compactCameraOffset: Double {
        get { Self.bounded(savedCompactCameraOffset ?? 0, in: Self.compactCameraOffsetRange, fallback: 0) }
        set { savedCompactCameraOffset = Self.bounded(newValue, in: Self.compactCameraOffsetRange, fallback: 0) }
    }

    func compactLayout(cameraHeight: CGFloat, rowHeight: CGFloat, textRowHeight: CGFloat, fontPointSize: CGFloat)
        -> (height: CGFloat, bottomPadding: CGFloat, minimumOffset: Double, textCeiling: CGFloat?) {
        let naturalHeight = cameraHeight + verticalPadding * 2 + rowHeight * 2 + lineSpacing
        let extraFirstRow = max(0, firstLineExtraSize - 1) * rowHeight / fontPointSize
        let baseHeight = max(naturalHeight + extraFirstRow, compactHeight)
        let firstLineScale = 1 + firstLineExtraSize / fontPointSize
        let textHeight = max(rowHeight - 5, textRowHeight) + textRowHeight * firstLineScale + lineSpacing
        let roomAboveText = (baseHeight - cameraHeight - textHeight) / 2
        let minimumOffset = max(Self.compactCameraOffsetRange.lowerBound, ceil(-max(0, roomAboveText - 1)))
        let offset = max(compactCameraOffset, minimumOffset)
        let height = CGFloat(baseHeight + offset)
        // An unchanged bottom inset moves the lyrics and the bottom edge by the same distance.
        let bottomPadding = CGFloat(verticalPadding + (baseHeight - naturalHeight) / 2)
        let textCeiling = offset < 0 ? height - cameraHeight - 1 : nil
        return (height, bottomPadding, minimumOffset, textCeiling)
    }

    var expandedWidth: Double {
        get { Self.bounded(savedExpandedWidth ?? 420, in: Self.expandedWidthRange, fallback: 420) }
        set { savedExpandedWidth = Self.bounded(newValue, in: Self.expandedWidthRange, fallback: 420) }
    }

    var expandedHeight: Double {
        get { Self.bounded(savedExpandedHeight ?? 682, in: Self.expandedHeightRange, fallback: 682) }
        set { savedExpandedHeight = Self.bounded(newValue, in: Self.expandedHeightRange, fallback: 682) }
    }

    var firstLineExtraSize: Double {
        get { Self.bounded(savedFirstLineExtraSize ?? 1, in: Self.firstLineExtraSizeRange, fallback: 1) }
        set { savedFirstLineExtraSize = Self.bounded(newValue, in: Self.firstLineExtraSizeRange, fallback: 1) }
    }

    var translationOpacity: Double {
        get {
            Self.bounded(contextualOpacity ?? Self.defaultTranslationOpacity,
                         in: Self.translationOpacityRange, fallback: Self.defaultTranslationOpacity)
        }
        set {
            contextualOpacity = Self.bounded(newValue, in: Self.translationOpacityRange,
                                              fallback: Self.defaultTranslationOpacity)
        }
    }

    static func load(from defaults: UserDefaults) -> Self {
        var options = defaults.data(forKey: "music.notchLyrics.options").flatMap {
            try? JSONDecoder().decode(Self.self, from: $0)
        } ?? Self()
        options.width = bounded(options.width, in: compactWidthRange, fallback: 196)
        options.fontSize = bounded(options.fontSize, in: 10...20, fallback: 11.5)
        options.verticalPadding = bounded(options.verticalPadding, in: 1...12, fallback: 3)
        options.lineSpacing = bounded(options.lineSpacing, in: 0...10, fallback: defaultLineSpacing)
        options.shadow = bounded(options.shadow, in: 0...1, fallback: 0.6)
        if ![400, 500, 600, 700, 800].contains(options.fontWeight) { options.fontWeight = 700 }
        if !["System", "Light", "Dark"].contains(options.theme) { options.theme = "System" }
        let migrationKey = "music.notchLyrics.migratedToLocalPanel"
        if !defaults.bool(forKey: migrationKey) {
            if defaults.object(forKey: "music.notchLyrics") as? Bool == false, options.enabled {
                options.enabled = false
                options.save(to: defaults)
            }
            defaults.set(true, forKey: migrationKey)
        }
        let lineSpacingMigrationKey = "music.notchLyrics.adjustedCompactLineSpacing"
        if !defaults.bool(forKey: lineSpacingMigrationKey) {
            if options.lineSpacing == 2 {
                options.lineSpacing = defaultLineSpacing
                options.save(to: defaults)
            }
            defaults.set(true, forKey: lineSpacingMigrationKey)
        }
        return options
    }

    func save(to defaults: UserDefaults) {
        if let data = try? JSONEncoder().encode(self) {
            defaults.set(data, forKey: "music.notchLyrics.options")
        }
    }

    private static func bounded(_ value: Double, in range: ClosedRange<Double>, fallback: Double) -> Double {
        value.isFinite ? min(range.upperBound, max(range.lowerBound, value)) : fallback
    }
}
