#if LUMIBAR_LEGACY_ACTIVITY_CLEANUP && canImport(ActivityKit)
import ActivityKit
import Foundation
import OSLog
#endif

func retireLumiBarLiveActivities() {
    #if LUMIBAR_LEGACY_ACTIVITY_CLEANUP && canImport(ActivityKit)
    if #available(macOS 27.3, *) {
        Task { await endLegacyLumiBarLiveActivities() }
    }
    #endif
}

#if LUMIBAR_LEGACY_ACTIVITY_CLEANUP && canImport(ActivityKit)
@available(macOS 27.3, *)
@concurrent
private func endLegacyLumiBarLiveActivities() async {
    let activities = Activity<LumiBarActivityAttributes>.activities
    guard !activities.isEmpty else { return }
    for activity in activities {
        await activity.end(nil, dismissalPolicy: .immediate)
    }
    Logger(subsystem: "com.lumibar.macos", category: "live-lyrics")
        .notice("Retired \(activities.count) legacy LumiBar live activities")
}

// This schema identifies and decodes activities left by earlier releases.
@available(macOS 27.3, *)
struct LumiBarActivityAttributes: ActivityAttributes {
    struct ContentState: Codable, Hashable {
        let title: String
        let artist: String
        let line: String
        let nextLine: String
        let playing: Bool
        let canControl: Bool
        let position: Double
        let duration: Double
        let sampledAt: Date
        let accentHex: UInt32
        let playbackRate: Double
        let album: String?
        let translation: String?
        var artwork: Data?
        var spectrum: [UInt8]?
        let canSeek: Bool?
        let previousLine: String?
        let laterLine: String?
        let nextTranslation: String?
        let showsTranslations: Bool?
        let countdownDots: Int?
        var lyricTrackToken: String? = nil
        var lyricOffset: Double? = nil
        var lyricsLoading: Bool? = nil
        var coverGlowHex: UInt32? = nil
        var coverGlowStrength: Double? = nil
        var expandedSpectrum: [UInt8]? = nil
        var karaokeRanges: [UInt8]? = nil
        var lyricCueIndex: Int? = nil
        var inlineMetadata: Bool? = nil
        var inlineKaraoke: Bool? = nil
    }

    let sessionID: String
}

#endif
