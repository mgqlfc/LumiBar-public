import CoreLocation
import Foundation
import MapKit
import SwiftUI

struct City: Codable, Hashable, Identifiable {
    let name: String
    let latitude: Double
    let longitude: Double
    var region: String?
    var timezone: String? = nil
    var id: String { String(format: "%.4f,%.4f", latitude, longitude) }
    var coordinates: CLLocationCoordinate2D { .init(latitude: latitude, longitude: longitude) }
}

struct Weather: Codable {
    struct Hour: Codable {
        let time: String
        let temperature: Double
        let code: Int
        let rain: Double?
        let isDay: Bool?
    }

    struct Day: Codable {
        let date: String
        let min: Double
        let max: Double
        let code: Int
        let rain: Double?
    }

    struct Alert: Codable, Identifiable {
        let id: String
        let event: String
        let level: String
        let headline: String
        let description: String
        let instruction: String
        let source: String
        let issuedAt: String?
        let startsAt: String?
        let endsAt: String?
        let cancelled: Bool?
    }

    struct Rain: Codable {
        struct Interval: Codable {
            let start: String
            let end: String
            let amountMm: Double?
        }
        let updatedAt: String
        let source: String
        let precisionNote: String
        let intervals: [Interval]

        func nextHour(at now: Date = .now) -> [Interval] {
            guard let updated = WeatherAPI.date(updatedAt),
                  (-300...10800).contains(now.timeIntervalSince(updated)) else { return [] }
            return intervals.filter {
                guard let start = WeatherAPI.date($0.start), let end = WeatherAPI.date($0.end) else { return false }
                return end > now && start < now.addingTimeInterval(3600)
            }.sorted { $0.start < $1.start }
        }
    }

    let temperature: Double
    let feelsLike: Double
    let code: Int
    let humidity: Double
    let wind: Double
    let windDirection: Double
    let rain: Double?
    let hours: [Hour]
    let days: [Day]
    let updated: String
    let aqi: Double?
    let pm25: Double?
    let pm10: Double?
    let source: String
    let shortTermPrecipitation: Rain?
    let isDay: Bool?
    let pressureHpa: Double?
    let uvIndex: Double?
    let alerts: [Alert]?
    let aqiStandard: String?
    let conditionText: String?
    let attributions: [String]?
    var providerNote: [String]? = nil
    var aqiCategory: String? = nil

    var aqiCaption: String {
        guard let aqi else { return "暂无数据" }
        if let aqiCategory, !aqiCategory.isEmpty { return aqiCategory }
        if aqi <= 50 { return "优" }
        if aqi <= 100 { return aqiStandard == "CN AQI" ? "良" : "中等" }
        if aqi <= 150 { return aqiStandard == "CN AQI" ? "轻度污染" : "敏感人群" }
        if aqi <= 200 { return aqiStandard == "CN AQI" ? "中度污染" : "不健康" }
        return aqiStandard == "CN AQI" ? (aqi <= 300 ? "重度污染" : "严重污染") : "很不健康"
    }

    var currentAlerts: [Alert] {
        let now = Date.now
        let ranks = ["red": 4, "orange": 3, "yellow": 2, "blue": 1]
        return Dictionary(grouping: alerts ?? [], by: \.id).values
            .compactMap { $0.max { ($0.issuedAt.flatMap(WeatherAPI.date) ?? .distantPast) <
                ($1.issuedAt.flatMap(WeatherAPI.date) ?? .distantPast) } }
            .filter { alert in
                let issued = alert.issuedAt.flatMap(WeatherAPI.date)
                let begins = alert.startsAt.flatMap(WeatherAPI.date) ?? .distantPast
                let ends = alert.endsAt.flatMap(WeatherAPI.date) ?? issued?.addingTimeInterval(86400)
                return !alert.event.isEmpty && alert.cancelled != true && begins <= now &&
                    (issued ?? .distantPast) <= now && (ends ?? .distantPast) > now
            }.sorted { ranks[$0.level, default: 0] > ranks[$1.level, default: 0] }
    }

    static func appearance(_ code: Int, day: Bool? = nil) -> (name: String, symbol: String, color: Color) {
        switch code {
        case 0 where day == false: ("晴", "moon.stars.fill", .indigo)
        case 0: ("晴", "sun.max.fill", .orange)
        case 1 where day == false, 2 where day == false: ("晴间多云", "cloud.moon.fill", .indigo)
        case 1, 2: ("晴间多云", "cloud.sun.fill", .orange)
        case 3: ("阴", "cloud.fill", .blue)
        case -2: ("沙尘暴", "sun.dust.fill", .orange)
        case -1, 45, 48: ("雾", "cloud.fog.fill", .gray)
        case 71...77, 85...86: ("雪", "cloud.snow.fill", .cyan)
        case 95...99: ("雷雨", "cloud.bolt.rain.fill", .indigo)
        case 51...67, 80...82: ("雨", "cloud.rain.fill", .blue)
        default: ("天气", "cloud.fill", .gray)
        }
    }
}

enum WeatherAPI {
    static let defaultProxy = "https://124.156.224.125/lumibar-weather/"
    private struct ProxyResponse: Decodable { let forecast: Weather }
    private struct CityResponse: Decodable {
        struct Match: Decodable {
            let name: String
            let latitude: Double
            let longitude: Double
            let timezone: String?
            let admin1: String?
            let country: String?
            var city: City {
                City(name: name, latitude: latitude, longitude: longitude,
                     region: [admin1, country].compactMap { $0 }.filter { !$0.isEmpty }.joined(separator: " · "),
                     timezone: timezone)
            }
        }
        let results: [Match]?
    }

    private struct District: Decodable {
        let name: String?
        let lat: String
        let lon: String
        let display_name: String?
        let address: [String: String]?
        var city: City? {
            guard let name, !name.isEmpty, let latitude = Double(lat), let longitude = Double(lon),
                  (-90...90).contains(latitude), (-180...180).contains(longitude) else { return nil }
            return .init(name: name, latitude: latitude, longitude: longitude, region: display_name,
                         timezone: address?["country_code"] == "cn" ? "Asia/Shanghai" : nil)
        }
    }
    private static let session: URLSession = {
        let configuration = URLSessionConfiguration.ephemeral
        configuration.timeoutIntervalForRequest = 12
        configuration.timeoutIntervalForResource = 18
        return URLSession(configuration: configuration)
    }()

    static func date(_ text: String) -> Date? {
        (try? Date.ISO8601FormatStyle(includingFractionalSeconds: true).parse(text))
            ?? (try? Date.ISO8601FormatStyle().parse(text))
    }

    static func proxyURL(_ text: String) -> URL? {
        guard let parts = URLComponents(string: text.trimmingCharacters(in: .whitespacesAndNewlines)),
              parts.scheme == "https", let host = parts.host, !host.isEmpty,
              parts.user == nil, parts.password == nil, parts.query == nil, parts.fragment == nil
        else { return nil }
        return parts.url
    }

    static func search(_ term: String) async throws -> [City] {
        var parts = URLComponents(string: "https://geocoding-api.open-meteo.com/v1/search")!
        parts.queryItems = [URLQueryItem(name: "name", value: term),
                            URLQueryItem(name: "count", value: "12"),
                            URLQueryItem(name: "language", value: "zh"),
                            URLQueryItem(name: "format", value: "json")]
        if let matches = try? JSONDecoder().decode(CityResponse.self, from: await get(parts.url!)).results,
           !matches.isEmpty { return matches.map(\.city).uniqued() }
        try Task.checkCancellation()
        if let districts = try? await searchDistricts(term), !districts.isEmpty { return districts }
        try Task.checkCancellation()
        let request = MKLocalSearch.Request()
        request.naturalLanguageQuery = term
        request.resultTypes = .address
        let result = try await MKLocalSearch(request: request).start()
        return result.mapItems.prefix(12).compactMap { item in
            let coordinate = item.location.coordinate
            guard CLLocationCoordinate2DIsValid(coordinate), let name = item.name else { return nil }
            return City(name: name, latitude: coordinate.latitude, longitude: coordinate.longitude,
                        region: item.address?.shortAddress)
        }.uniqued()
    }

    private static func searchDistricts(_ term: String) async throws -> [City] {
        var endpoint = URLComponents(string: "https://nominatim.openstreetmap.org/search")!
        endpoint.queryItems = [.init(name: "q", value: term), .init(name: "format", value: "jsonv2"),
                               .init(name: "limit", value: "6"), .init(name: "addressdetails", value: "1"),
                               .init(name: "accept-language", value: "zh-CN")]
        let cities = try JSONDecoder().decode([District].self, from: await get(endpoint.url!))
        return cities.compactMap(\.city).uniqued()
    }

    static func district(for location: CLLocation) async -> City? {
        let coordinate = location.coordinate
        var endpoint = URLComponents(string: "https://nominatim.openstreetmap.org/reverse")!
        endpoint.queryItems = [
            .init(name: "lat", value: String(coordinate.latitude)), .init(name: "lon", value: String(coordinate.longitude)),
            .init(name: "format", value: "jsonv2"), .init(name: "zoom", value: "10"),
            .init(name: "addressdetails", value: "1"), .init(name: "accept-language", value: "zh-CN")
        ]
        if let data = try? await get(endpoint.url!), let place = try? JSONDecoder().decode(District.self, from: data),
           let address = place.address {
            let name = ["city_district", "town", "city", "county", "village", "suburb"]
                .compactMap { address[$0] }.first { !$0.isEmpty } ?? "当前位置"
            var seen = Set([name])
            let region = ["state", "city", "country"].compactMap { address[$0] }
                .filter { !$0.isEmpty && seen.insert($0).inserted }.joined(separator: " · ")
            return City(name: name, latitude: coordinate.latitude, longitude: coordinate.longitude,
                        region: region, timezone: address["country_code"] == "cn" ? "Asia/Shanghai" : nil)
        }
        guard !Task.isCancelled else { return nil }
        let request = MKReverseGeocodingRequest(location: location)
        let item = (try? await request?.mapItems)?.first
        let name = item?.addressRepresentations?.cityName ?? item?.name ?? "当前位置"
        return City(name: name, latitude: coordinate.latitude, longitude: coordinate.longitude, region: nil)
    }

    static func forecast(_ city: City, proxy: String) async throws -> Weather {
        if let base = proxyURL(proxy) {
            var parts = URLComponents(url: base.appending(path: "v1/forecast"), resolvingAgainstBaseURL: false)!
            parts.queryItems = coordinates(city, precision: 2)
            if let result = try? JSONDecoder().decode(ProxyResponse.self, from: await get(parts.url!)).forecast,
               (0...100).contains(result.humidity), result.wind >= 0, date(result.updated) != nil,
               result.hours.count <= 240, result.days.count <= 30 {
                return result
            }
        }
        try Task.checkCancellation()
        do { return try await openMeteo(city) }
        catch {
            try Task.checkCancellation()
            return try await metNorway(city)
        }
    }

    private static func openMeteo(_ city: City) async throws -> Weather {
        let currentFields = "is_day,temperature_2m,relative_humidity_2m,apparent_temperature,weather_code,wind_speed_10m,wind_direction_10m,pressure_msl"
        let hourlyFields = "is_day,temperature_2m,weather_code,precipitation_probability"
        let dailyFields = "weather_code,temperature_2m_max,temperature_2m_min,precipitation_probability_max"
        var weather = URLComponents(string: "https://api.open-meteo.com/v1/forecast")!
        weather.queryItems = coordinates(city) + [
            .init(name: "current", value: currentFields), .init(name: "hourly", value: hourlyFields),
            .init(name: "daily", value: dailyFields), .init(name: "timezone", value: "auto"),
            .init(name: "forecast_days", value: "10"), .init(name: "minutely_15", value: "precipitation"),
            .init(name: "forecast_minutely_15", value: "12")]
        var air = URLComponents(string: "https://air-quality-api.open-meteo.com/v1/air-quality")!
        air.queryItems = coordinates(city) + [.init(name: "current", value: "us_aqi,pm2_5,pm10,uv_index")]
        async let data = get(weather.url!)
        async let airData = try? get(air.url!)
        let root = try dictionary(await data)
        guard let current = root["current"] as? [String: Any],
              let temperature = current["temperature_2m"] as? Double,
              let feels = current["apparent_temperature"] as? Double,
              let code = current["weather_code"] as? Int,
              let humidity = current["relative_humidity_2m"] as? Double,
              let wind = current["wind_speed_10m"] as? Double,
              let direction = current["wind_direction_10m"] as? Double else { throw URLError(.cannotParseResponse) }

        let hourly = root["hourly"] as? [String: Any] ?? [:]
        let currentHour = String((current["time"] as? String ?? "").prefix(13))
        let hours = (hourly["time"] as? [String] ?? []).enumerated().compactMap { index, time -> Weather.Hour? in
            guard String(time.prefix(13)) >= currentHour, let temp: Double = value(hourly, "temperature_2m", index),
                  let code: Int = value(hourly, "weather_code", index) else { return nil }
            let day: Int? = value(hourly, "is_day", index)
            return .init(time: time, temperature: temp, code: code,
                         rain: value(hourly, "precipitation_probability", index), isDay: day.map { $0 == 1 })
        }
        let daily = root["daily"] as? [String: Any] ?? [:]
        let days = (daily["time"] as? [String] ?? []).enumerated().compactMap { index, date -> Weather.Day? in
            guard let min: Double = value(daily, "temperature_2m_min", index),
                  let max: Double = value(daily, "temperature_2m_max", index),
                  let code: Int = value(daily, "weather_code", index) else { return nil }
            return .init(date: date, min: min, max: max, code: code,
                         rain: value(daily, "precipitation_probability_max", index))
        }

        let now = Date.now
        let airRoot = (await airData).flatMap { try? dictionary($0) }
        let quality = airRoot?["current"] as? [String: Any] ?? [:]
        let minutes = root["minutely_15"] as? [String: Any] ?? [:]
        let parser = DateFormatter()
        parser.locale = Locale(identifier: "en_US_POSIX")
        parser.dateFormat = "yyyy-MM-dd'T'HH:mm"
        parser.timeZone = (root["timezone"] as? String).flatMap(TimeZone.init(identifier:))
            ?? TimeZone(secondsFromGMT: root["utc_offset_seconds"] as? Int ?? 0)
        let intervals = (minutes["time"] as? [String] ?? []).enumerated().compactMap { index, time -> Weather.Rain.Interval? in
            guard let end = parser.date(from: time) else { return nil }
            return .init(start: end.addingTimeInterval(-900).ISO8601Format(), end: end.ISO8601Format(),
                         amountMm: value(minutes, "precipitation", index))
        }
        let trend = intervals.isEmpty ? nil : Weather.Rain(updatedAt: now.ISO8601Format(), source: "Open-Meteo",
                                                          precisionNote: "15 分钟模式预报", intervals: intervals)
        return Weather(temperature: temperature, feelsLike: feels, code: code, humidity: humidity,
                       wind: wind, windDirection: direction, rain: hours.first?.rain,
                       hours: Array(hours.prefix(24)), days: days, updated: now.ISO8601Format(),
                       aqi: quality["us_aqi"] as? Double, pm25: quality["pm2_5"] as? Double,
                       pm10: quality["pm10"] as? Double, source: "Open-Meteo", shortTermPrecipitation: trend,
                       isDay: (current["is_day"] as? Int).map { $0 == 1 },
                       pressureHpa: current["pressure_msl"] as? Double, uvIndex: quality["uv_index"] as? Double,
                       alerts: nil, aqiStandard: "US AQI", conditionText: nil, attributions: nil)
    }

    private static func metNorway(_ city: City) async throws -> Weather {
        var parts = URLComponents(string: "https://api.met.no/weatherapi/locationforecast/2.0/compact")!
        let coordinateValues = coordinates(city).map(\.value)
        parts.queryItems = [.init(name: "lat", value: coordinateValues[0]), .init(name: "lon", value: coordinateValues[1])]
        let root = try dictionary(await get(parts.url!))
        let properties = root["properties"] as? [String: Any] ?? [:]
        let series = properties["timeseries"] as? [[String: Any]] ?? []
        let formatter = DateFormatter()
        formatter.locale = Locale(identifier: "en_US_POSIX")
        formatter.dateFormat = "yyyy-MM-dd'T'HH:mm"
        formatter.timeZone = city.timezone.flatMap(TimeZone.init(identifier:)) ?? TimeZone(secondsFromGMT: 0)
        func details(_ entry: [String: Any]) -> [String: Any]? {
            let data = entry["data"] as? [String: Any]
            let instant = data?["instant"] as? [String: Any]
            return instant?["details"] as? [String: Any]
        }
        let hours = series.compactMap { entry -> Weather.Hour? in
            guard let date = (entry["time"] as? String).flatMap(Self.date),
                  let temperature = details(entry)?["air_temperature"] as? Double,
                  let data = entry["data"] as? [String: Any] else { return nil }
            let summaries = ["next_1_hours", "next_6_hours", "next_12_hours"].compactMap {
                (data[$0] as? [String: Any])?["summary"] as? [String: Any]
            }
            let symbol = summaries.first?["symbol_code"] as? String ?? ""
            return .init(time: formatter.string(from: date), temperature: temperature, code: metCode(symbol),
                         rain: nil, isDay: symbol.hasSuffix("_night") ? false : nil)
        }
        guard let first = hours.first, let instant = series.first.flatMap(details),
              let humidity = instant["relative_humidity"] as? Double,
              let wind = instant["wind_speed"] as? Double,
              let direction = instant["wind_from_direction"] as? Double else { throw URLError(.cannotParseResponse) }
        let group = Dictionary(grouping: hours) { String($0.time.prefix(10)) }
        let days = group.keys.sorted().compactMap { date -> Weather.Day? in
            guard let samples = group[date], let sample = samples.first,
                  let min = samples.map(\.temperature).min(), let max = samples.map(\.temperature).max() else { return nil }
            let frequencies = Dictionary(grouping: samples, by: \.code)
            let mode = frequencies.keys.sorted().max { frequencies[$0, default: []].count < frequencies[$1, default: []].count }
            return .init(date: date, min: min, max: max, code: mode ?? sample.code, rain: nil)
        }
        let temperature = first.temperature
        let feels = temperature + 0.33 * (humidity / 100 * 6.105 * exp(17.27 * temperature / (237.7 + temperature)))
            - 0.7 * wind - 4
        return .init(temperature: temperature, feelsLike: feels, code: first.code, humidity: humidity, wind: wind * 3.6,
                     windDirection: direction, rain: nil, hours: Array(hours.prefix(24)), days: Array(days.prefix(10)),
                     updated: Date.now.ISO8601Format(), aqi: nil, pm25: nil, pm10: nil, source: "MET Norway",
                     shortTermPrecipitation: nil, isDay: first.isDay, pressureHpa: instant["air_pressure_at_sea_level"] as? Double,
                     uvIndex: nil, alerts: nil, aqiStandard: nil, conditionText: nil,
                     attributions: city.timezone == nil ? ["预报使用 UTC 时间"] : nil)
    }

    private static func metCode(_ symbol: String) -> Int {
        if symbol.contains("thunder") { return 95 }
        if symbol.contains("snow") { return 73 }
        if symbol.contains("rain") { return 63 }
        if symbol.contains("fog") { return 45 }
        if symbol.hasPrefix("clearsky") { return 0 }
        if symbol.hasPrefix("fair") { return 1 }
        if symbol.hasPrefix("partlycloudy") { return 2 }
        return 3
    }

    private static func coordinates(_ city: City, precision: Int = 4) -> [URLQueryItem] {
        let names = precision == 2 ? ("lat", "lon") : ("latitude", "longitude")
        let locale = Locale(identifier: "en_US_POSIX")
        return [.init(name: names.0, value: String(format: "%.*f", locale: locale, precision, city.latitude)),
                .init(name: names.1, value: String(format: "%.*f", locale: locale, precision, city.longitude))]
    }

    private static func get(_ url: URL) async throws -> Data {
        var request = URLRequest(url: url)
        request.setValue("LumiBar", forHTTPHeaderField: "X-LumiBar-Client")
        request.setValue("LumiBar-macOS/0.1 (+https://github.com/mgqlfc/LumiBar)", forHTTPHeaderField: "User-Agent")
        let (data, response) = try await session.data(for: request)
        guard let http = response as? HTTPURLResponse, (200..<300).contains(http.statusCode),
              data.count <= 2_000_000 else { throw URLError(.badServerResponse) }
        return data
    }

    private static func dictionary(_ data: Data) throws -> [String: Any] {
        guard let result = try JSONSerialization.jsonObject(with: data) as? [String: Any]
        else { throw URLError(.cannotParseResponse) }
        return result
    }

    private static func value<T>(_ values: [String: Any], _ key: String, _ index: Int) -> T? {
        guard let array = values[key] as? [Any], array.indices.contains(index) else { return nil }
        return array[index] as? T
    }
}

extension Array where Element == City {
    func uniqued() -> [City] {
        var seen = Set<String>()
        return filter { seen.insert($0.id).inserted }
    }
}
