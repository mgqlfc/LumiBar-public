import Foundation
import SwiftUI

enum PreviewData {
    @MainActor
    static func install(weather: WeatherModel, music: MusicModel) {
        let city = City(name: "武清区", latitude: 39.36, longitude: 117.07, region: "天津市 · 中国")
        weather.city = city
        weather.locatedCity = city
        weather.usingLocation = true
        weather.favorites = [.init(name: "奎文区", latitude: 36.71, longitude: 119.13), city]
        let now = Date.now
        let timeFormat = DateFormatter()
        timeFormat.locale = Locale(identifier: "en_US_POSIX")
        timeFormat.timeZone = TimeZone(identifier: "Asia/Shanghai")
        timeFormat.dateFormat = "yyyy-MM-dd'T'HH:mm"
        let hourValues: [Double] = [0, 0, -1, -1, -1, -1, -2, -2, -2, -1, -1, 2,
                                    5, 6, 7, 7, 8, 7, 6, 5, 5, 3, 2, 1]
        let hours = hourValues.enumerated().map { index, value in
            Weather.Hour(time: timeFormat.string(from: now.addingTimeInterval(Double(index + 1) * 3600)),
                         temperature: value, code: index == 10 ? 2 : 0, rain: 0, isDay: nil)
        }
        let days: [Weather.Day] = (0..<10).map { index in
            let date = timeFormat.string(from: now.addingTimeInterval(Double(index) * 86400))
            let step = index % 2
            return Weather.Day(date: date, min: index == 0 ? -1 : -2, max: Double(7 + step), code: 0, rain: 0)
        }
        let intervals = (0..<12).map { index in
            let start = now.addingTimeInterval(Double(index) * 300)
            return Weather.Rain.Interval(start: start.ISO8601Format(), end: start.addingTimeInterval(300).ISO8601Format(),
                                         amountMm: 0)
        }
        let trend = Weather.Rain(updatedAt: now.ISO8601Format(), source: "和风天气", precisionNote: "5 分钟临近预报",
                                 intervals: intervals)
        let alerts = [makeAlert("海上大风", level: "yellow", now: now), makeAlert("大风", level: "blue", now: now)]
        weather.weather = Weather(temperature: 3, feelsLike: -5, code: 0, humidity: 27, wind: 29, windDirection: 0,
            rain: 0, hours: hours, days: days, updated: now.ISO8601Format(), aqi: 12, pm25: 4, pm10: 10,
            source: "外观预览", shortTermPrecipitation: trend, isDay: false, pressureHpa: 1029, uvIndex: 0,
            alerts: alerts, aqiStandard: "CN AQI", conditionText: "晴", attributions: nil, aqiCategory: "优")

        music.track = Track(player: .netease, title: "穿过城市的光", artist: "LumiBar", album: "外观预览",
                            duration: 213, position: 32, playing: false, artworkURL: nil, pid: 0, observedAt: now)
        music.colors = [Color(red: 0.81, green: 0.24, blue: 0.63), Color(red: 0.84, green: 0.37, blue: 0.49)]
        music.previewSpectrum = [0.77, 0.92, 0.66, 0.41, 0.21, 0.79, 0.64, 0.44, 0.38, 0.29, 0.55, 0.62, 0.41, 0.26]
        music.lyrics = Lyric.parse("""
            [00:14.00]The night is quiet by the station
            [00:22.00]Paper lights drift across the avenue
            [00:29.00]Somewhere a window is waiting
            [00:31.00]穿过城市的光
            [00:35.10]把夜色照成海
            [00:38.00]等风从窗边经过
            [00:41.00]Till the whole street turns to gold
            [00:45.00]We keep walking toward the morning light
            [00:50.00]The last train has left
            """)
        music.lyricsProvider = "同步"
        music.onTrackChange?()
    }

    private static func makeAlert(_ name: String, level: String, now: Date) -> Weather.Alert {
        .init(id: "preview-" + level, event: name, level: level, headline: name, description: "外观预览",
              instruction: "", source: "LumiBar 外观预览", issuedAt: now.addingTimeInterval(-300).ISO8601Format(),
              startsAt: nil, endsAt: now.addingTimeInterval(3600).ISO8601Format(), cancelled: false)
    }
}
