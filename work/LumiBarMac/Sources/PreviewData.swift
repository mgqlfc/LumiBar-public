import Foundation

enum PreviewData {
    @MainActor
    static func install(weather: WeatherModel, music: MusicModel) {
        let city = City(name: "奎文区", latitude: 36.71, longitude: 119.13, region: "潍坊 · 山东")
        weather.city = city
        weather.favorites = [city]
        let now = Date.now
        let hourValues: [Double] = [17, 17, 18, 19, 20, 21, 22, 22, 23, 23, 24, 23,
                                    23, 22, 21, 20, 19, 18, 17, 17, 16, 16, 16, 16]
        let hours = hourValues.enumerated().map { index, value in
            Weather.Hour(time: now.addingTimeInterval(Double(index) * 3600).ISO8601Format(),
                          temperature: value, code: index < 4 ? 2 : 3, rain: 18, isDay: index < 13)
        }
        let codes = [2, 0, 3, 61]
        let days: [Weather.Day] = (0..<7).map { index in
            let date = now.addingTimeInterval(Double(index) * 86400).ISO8601Format()
            let step = index % 4
            return Weather.Day(date: date, min: Double(12 + step), max: Double(21 + step),
                               code: codes[step], rain: 18)
        }
        weather.weather = Weather(temperature: 17, feelsLike: 16, code: 2, humidity: 67, wind: 13, windDirection: 170,
            rain: 18, hours: hours, days: days, updated: now.ISO8601Format(), aqi: 42, pm25: 9, pm10: 19,
            source: "LumiBar 外观预览", shortTermPrecipitation: nil, isDay: true, pressureHpa: 1017, uvIndex: 3,
            alerts: nil, aqiStandard: "US AQI", conditionText: "晴间多云", attributions: nil)
        music.track = Track(player: .music, title: "穿过城市的光", artist: "LumiBar", album: "外观预览",
                            duration: 213, position: 32, playing: false, artworkURL: nil, pid: 0, observedAt: now)
        music.lyrics = Lyric.parse("""
            [00:28.00]街灯落在雨后的路
            [00:31.00]穿过城市的光
            [00:35.10]把夜色照成海
            [00:38.00]等风从窗边经过
            """)
        music.onTrackChange?()
    }
}
