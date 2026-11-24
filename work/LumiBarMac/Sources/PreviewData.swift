import Foundation
import SwiftUI

struct ReferenceRequest {
    enum Scene: String {
        case weather, music, settings
        case hover = "weather-hover"
        case capsule = "weather-capsule"
        case lite = "weather-lite"
        case wide = "weather-wide"
        case alerts = "weather-alerts"

        var size: CGSize {
            switch self {
            case .weather, .alerts: .init(width: 512, height: 742)
            case .hover: WeatherView.hoverSize
            case .capsule: .init(width: 124, height: 54)
            case .lite: .init(width: 85, height: 54)
            case .wide: .init(width: 146, height: 54)
            case .music: MusicView.detailsSize
            case .settings: SettingsView.windowSize
            }
        }
    }

    let scene: Scene
    let dark: Bool

    static var current: Self? {
        let arguments = ProcessInfo.processInfo.arguments
        guard let index = arguments.firstIndex(of: "--reference-case"), arguments.indices.contains(index + 1),
              let scene = Scene(rawValue: arguments[index + 1]) else { return nil }
        return Self(scene: scene, dark: arguments.contains("--dark"))
    }
}

enum PreviewData {
    private struct Fixture: Decodable {
        struct Clock: Decodable { let date: String; let hourlyStart: String }
        struct FixtureCity: Decodable { let name: String; let region: String; let favorites: [String]; let located: Bool }
        struct Forecast: Decodable {
            struct Hour: Decodable { let hoursFromStart: Int; let temperature: Double; let code: Int; let rainProbabilityPercent: Double }
            struct Day: Decodable { let daysFromStart: Int; let min: Double; let max: Double; let code: Int; let rainProbabilityPercent: Double }
            let temperature: Double; let feelsLike: Double; let code: Int; let isDay: Bool; let condition: String
            let rainProbabilityPercent: Double; let relativeHumidityPercent: Double; let windKmh: Double; let windDegrees: Double
            let aqi: Double; let aqiStandard: String; let aqiCategory: String; let pressureHpa: Double; let uvi: Double; let sourceLabel: String
            let hourly: [Hour]; let daily: [Day]; let precipitation15MinAmountMm: [Double]
        }
        struct Wide: Decodable { let temperature: Double; let feelsLike: Double; let rainProbabilityPercent: Double }
        struct Alert: Decodable { let event: String; let level: String }
        struct Media: Decodable {
            struct Line: Decodable {
                struct Word: Decodable { let seconds, duration: Double; let text: String }
                let seconds: Double
                let text: String
                let words: [Word]?
            }
            let title: String; let artist: String; let album: String; let playing: Bool
            let positionSeconds: Double; let durationSeconds: Double
            let accent: String; let secondaryAccent: String; let lyrics: [Line]; let spectrum: [Double]
        }
        struct Capture: Decodable { let glassDensity: Double; let glow: Double; let saturation: Double; let lyricsFontSize: Double }
        let clock: Clock; let city: FixtureCity; let weather: Forecast
        let wideWeather: Wide; let alerts: [Alert]; let media: Media; let capture: Capture
    }

    @MainActor
    static func install(weather: WeatherModel, music: MusicModel, appearance: AppearanceModel, scene: ReferenceRequest.Scene) throws -> Date {
        guard let url = Bundle.main.url(forResource: "fixtures", withExtension: "json", subdirectory: "Reference") else {
            throw CocoaError(.fileNoSuchFile)
        }
        let fixture = try JSONDecoder().decode(Fixture.self, from: Data(contentsOf: url))
        guard let clock = WeatherAPI.date(fixture.clock.date + "T" + fixture.clock.hourlyStart + ":00+08:00") else {
            throw CocoaError(.fileReadCorruptFile)
        }
        let format = DateFormatter()
        format.locale = Locale(identifier: "en_US_POSIX")
        format.timeZone = TimeZone(identifier: "Asia/Shanghai")
        format.dateFormat = "yyyy-MM-dd'T'HH:mm"
        let city = City(name: fixture.city.name, latitude: 39.36, longitude: 117.07, region: fixture.city.region)
        weather.isPreview = true
        weather.city = city
        weather.locatedCity = fixture.city.located ? city : nil
        weather.usingLocation = fixture.city.located
        weather.favorites = fixture.city.favorites.enumerated().map { .init(name: $0.element, latitude: Double($0.offset), longitude: 0) }
        let w = fixture.weather
        let hours = w.hourly.map { entry in
            Weather.Hour(time: format.string(from: clock.addingTimeInterval(Double(entry.hoursFromStart) * 3600)),
                         temperature: entry.temperature, code: entry.code, rain: entry.rainProbabilityPercent, isDay: nil)
        }
        let days = w.daily.map { entry in
            Weather.Day(date: format.string(from: clock.addingTimeInterval(Double(entry.daysFromStart) * 86400)),
                        min: entry.min, max: entry.max, code: entry.code, rain: entry.rainProbabilityPercent)
        }
        let intervals = w.precipitation15MinAmountMm.enumerated().map { index, amount in
            let start = clock.addingTimeInterval(Double(index) * 900)
            return Weather.Rain.Interval(start: start.ISO8601Format(), end: start.addingTimeInterval(900).ISO8601Format(),
                                         amountMm: amount)
        }
        let trend = Weather.Rain(updatedAt: clock.ISO8601Format(), source: "Open-Meteo", precisionNote: "15 分钟模式趋势",
                                 intervals: intervals)
        let alerts = scene == .alerts ? fixture.alerts.enumerated().map { index, item in
            Weather.Alert(id: "preview-\(index)", event: item.event, level: item.level, headline: item.event,
                          description: "外观预览", instruction: "", source: w.sourceLabel, issuedAt: clock.ISO8601Format(),
                          startsAt: nil, endsAt: clock.addingTimeInterval(3600).ISO8601Format(), cancelled: false)
        } : []
        weather.weather = Weather(temperature: scene == .wide ? fixture.wideWeather.temperature : w.temperature,
            feelsLike: scene == .wide ? fixture.wideWeather.feelsLike : w.feelsLike, code: w.code, humidity: w.relativeHumidityPercent,
            wind: w.windKmh, windDirection: w.windDegrees, rain: scene == .wide ? fixture.wideWeather.rainProbabilityPercent : w.rainProbabilityPercent,
            hours: hours, days: days, updated: clock.ISO8601Format(), aqi: w.aqi, pm25: nil, pm10: nil,
            source: w.sourceLabel, shortTermPrecipitation: trend, isDay: w.isDay, pressureHpa: w.pressureHpa, uvIndex: w.uvi,
            alerts: alerts, aqiStandard: w.aqiStandard, conditionText: w.condition, attributions: nil, aqiCategory: w.aqiCategory)

        appearance.glassDensity = fixture.capture.glassDensity
        appearance.coverBlend = fixture.capture.glow
        appearance.coverSaturation = fixture.capture.saturation
        appearance.lyricsSize = fixture.capture.lyricsFontSize
        appearance.animation = false
        let m = fixture.media
        music.track = Track(player: .spotify, title: m.title, artist: m.artist, album: m.album, duration: m.durationSeconds,
                            position: m.positionSeconds, playing: m.playing, artworkURL: nil, pid: 0, observedAt: .now)
        music.artwork = Bundle.main.url(forResource: "reference-cover", withExtension: "png", subdirectory: "Reference")
            .flatMap { NSImage(contentsOf: $0) }
        music.colors = [m.accent, m.secondaryAccent].map { Color.rgb(UInt32($0.dropFirst(), radix: 16) ?? 0x71B6D7) }
        music.previewSpectrum = m.spectrum
        music.lyrics = m.lyrics.map { line in
            Lyric(time: line.seconds, text: line.text, words: (line.words ?? []).map {
                Lyric.Word(time: $0.seconds, duration: $0.duration, text: $0.text)
            })
        }
        music.lyricsProvider = "同步"
        if let current = music.track {
            music.lyricLibrary.populatePreview(libraryPreview(current: current, lines: music.lyrics))
        }
        music.onTrackChange?()
        return clock
    }

    private static func libraryPreview(current: Track, lines: [Lyric]) -> [LyricLibraryEntry] {
        var active = LyricLibraryEntry(track: current)
        active.source = .local
        active.lines = lines
        let recording = LyricRecording(id: "preview", title: current.title, artist: current.artist,
                                      album: current.album, duration: current.duration)
        let sample = LyricCandidate(source: .local, recording: recording, lines: lines)
        active.selectionID = sample.id
        active.decisions = [LyricLibraryEntry.Decision(RankedLyric(candidate: sample, score: sample.score(for: current)))]

        let another = Track(player: .music, title: "Somewhere After Midnight", artist: "LumiBar 预览", album: "Night Notes",
                            duration: 183, position: 0, playing: false, artworkURL: nil, pid: 0, observedAt: .now)
        var timed = LyricLibraryEntry(track: another)
        timed.updated = .now.addingTimeInterval(-3600)
        timed.source = .qqMusic
        timed.translationSource = .qqMusic
        timed.locked = true
        timed.lines = [
            Lyric(time: 12.2, text: "The city breathes", translation: "城市轻轻呼吸", words: [
                .init(time: 12.2, duration: 0.35, text: "The "),
                .init(time: 12.55, duration: 0.48, text: "city "),
                .init(time: 13.03, duration: 0.70, text: "breathes"),
            ]),
            Lyric(time: 15.8, text: "Windows turn to stars", translation: "窗灯渐成星光", words: [
                .init(time: 15.8, duration: 0.7, text: "Windows "),
                .init(time: 16.5, duration: 0.4, text: "turn "),
                .init(time: 16.9, duration: 0.3, text: "to "),
                .init(time: 17.2, duration: 0.65, text: "stars"),
            ]),
        ]
        let timedRecording = LyricRecording(id: "preview-timed", title: another.title, artist: another.artist,
                                            album: another.album, duration: another.duration)
        let timedCandidate = LyricCandidate(source: .qqMusic, recording: timedRecording, lines: timed.lines)
        timed.selectionID = timedCandidate.id
        timed.decisions = [LyricLibraryEntry.Decision(RankedLyric(candidate: timedCandidate, score: timedCandidate.score(for: another)))]
        let missingTrack = Track(player: .music, title: "Quiet Horizon", artist: "LumiBar 预览", album: "Night Notes",
                                 duration: 199, position: 0, playing: false, artworkURL: nil, pid: 0, observedAt: .now)
        var missing = LyricLibraryEntry(track: missingTrack)
        missing.updated = .now.addingTimeInterval(-7200)
        return [active, timed, missing]
    }
}
