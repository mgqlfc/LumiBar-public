import Charts
import SwiftUI

@MainActor
enum WeatherGlyph {
    private static let atlas: CGImage? = {
        guard let url = Bundle.main.url(forResource: "weather-icons-clean", withExtension: "png"),
              let image = NSImage(contentsOf: url) else { return nil }
        return image.cgImage(forProposedRect: nil, context: nil, hints: nil)
    }()
    private static var atlasCells: [Int: NSImage] = [:]
    private static var fallbackImages: [String: NSImage] = [:]

    private static func atlasImage(_ code: Int, day: Bool?) -> NSImage? {
        guard let atlas else { return nil }
        let index: Int = switch code {
        case 0, 1: day == false ? 1 : 0
        case 2: day == false ? 3 : 2
        case 3: 5
        case -1, 45, 48: 12
        case -2: 13
        case 56, 57, 66, 67: 10
        case 71, 73, 75, 77, 85, 86: 9
        case 95, 96, 99: 11
        case 65, 82: 7
        case 51...82: 6
        default: 4
        }
        if let cached = atlasCells[index] { return cached }
        let width = atlas.width / 4
        let height = atlas.height / 4
        let rect = CGRect(x: (index % 4) * width, y: (index / 4) * height, width: width, height: height)
        guard let bitmap = atlas.cropping(to: rect) else { return nil }
        let image = NSImage(cgImage: bitmap, size: NSSize(width: width, height: height))
        atlasCells[index] = image
        return image
    }

    static func image(_ code: Int, day: Bool? = nil) -> NSImage? {
        if let artwork = atlasImage(code, day: day) { return artwork }
        let filename: String = switch Weather.appearance(code, day: day).symbol {
        case "sun.max.fill": "sun"
        case "moon.stars.fill": "crescent_moon"
        case "cloud.sun.fill": "sun_behind_small_cloud"
        case "cloud.bolt.rain.fill": "cloud_with_lightning_and_rain"
        case "cloud.rain.fill": "cloud_with_rain"
        case "cloud.snow.fill": "cloud_with_snow"
        case "cloud.fog.fill": "fog"
        case "cloud.fill": "cloud"
        default: ""
        }
        if let cached = fallbackImages[filename] { return cached }
        guard !filename.isEmpty, let url = Bundle.main.url(forResource: filename, withExtension: "png", subdirectory: "Weather")
        else { return nil }
        let image = NSImage(contentsOf: url)
        fallbackImages[filename] = image
        return image
    }
}

private struct WeatherIcon: View {
    let code: Int
    var day: Bool? = nil
    let size: CGFloat

    var body: some View {
        Group {
            if let original = WeatherGlyph.image(code, day: day) {
                Image(nsImage: original).resizable().scaledToFit()
            } else {
                Image(systemName: Weather.appearance(code, day: day).symbol)
                    .symbolRenderingMode(.multicolor)
                    .font(.system(size: size * 0.75))
            }
        }
        .frame(width: size, height: size)
        .accessibilityLabel(Weather.appearance(code, day: day).name)
    }
}

struct WeatherView: View {
    @Bindable var model: WeatherModel
    let compact: Bool
    let width: CGFloat
    let height: CGFloat
    var showSettings: () -> Void
    var quit: () -> Void
    @ViewState private var searching = false
    @ViewState private var term = ""
    @ViewState private var results: [City] = []
    @ViewState private var searchError: String?
    @ViewState private var searchTask: Task<Void, Never>?
    @ViewState private var moreDays = false
    @Environment(\.colorScheme) private var colorScheme

    var body: some View {
        Group {
            if compact {
                VStack(alignment: .leading, spacing: 12) {
                    if let weather = model.weather {
                        current(weather)
                        rainTrend(weather)
                        if let aqi = weather.aqi {
                            Text("\(weather.aqiStandard ?? "AQI")  \(Int(aqi.rounded()))").font(.subheadline)
                        }
                    } else {
                        Text(model.city?.name ?? "LumiBar 天气").font(.headline)
                        Text(model.error ?? (model.loading ? "正在更新天气…" : "点击选择城市"))
                            .foregroundStyle(.secondary)
                    }
                }
                .padding(20)
                .frame(width: width - 16, alignment: .leading)
            } else {
                VStack(spacing: 0) {
                    header.padding(.horizontal, 22).padding(.top, 17).padding(.bottom, 10)
                    ScrollView {
                        VStack(alignment: .leading, spacing: 15) {
                            if searching || model.city == nil { search }
                            if !model.favorites.isEmpty { favorites }
                            if let message = model.locationError ?? model.error {
                                Label(message, systemImage: "exclamationmark.circle").font(.subheadline)
                                    .foregroundStyle(.secondary)
                            }
                            if let weather = model.weather {
                                current(weather)
                                if !weather.currentAlerts.isEmpty { alerts(weather) }
                                if !weather.hours.isEmpty { hourly(weather) }
                                if !weather.days.isEmpty { daily(weather) }
                                conditions(weather)
                                footer(weather)
                            } else if model.loading {
                                ProgressView("正在更新天气…").padding(.vertical, 30)
                            }
                        }
                        .padding(.horizontal, 22).padding(.top, 3).padding(.bottom, 22)
                        .frame(maxWidth: .infinity, alignment: .leading)
                    }
                    .scrollIndicators(.hidden)
                }
                .frame(width: width - 16, height: height - 16)
            }
        }
        .glassEffect(.regular, in: RoundedRectangle(cornerRadius: 29))
        .padding(8)
        .frame(width: width, height: height, alignment: .top)
        .onDisappear { searchTask?.cancel() }
    }

    private var header: some View {
        HStack(spacing: 8) {
            VStack(alignment: .leading, spacing: 2) {
                Text(model.city?.name ?? "天气").font(.system(size: 24, weight: .semibold)).lineLimit(1)
                if let region = model.city?.region, region != model.city?.name {
                    Text(region).font(.caption).foregroundStyle(.secondary).lineLimit(1)
                }
            }
            Spacer()
            if let city = model.city {
                let saved = model.favorites.contains { $0.id == city.id }
                iconButton(saved ? "star.fill" : "star", saved ? "取消收藏" : "收藏城市") { model.toggleFavorite(city) }
            }
            iconButton("location", "使用当前位置") { model.locate() }
            iconButton("magnifyingglass", "搜索城市") { searching.toggle() }
            iconButton("arrow.clockwise", "刷新天气") { model.refresh() }.disabled(model.city == nil || model.loading)
            Menu {
                Button("LumiBar 设置…", action: showSettings)
                Divider()
                Button("退出 LumiBar", action: quit)
            } label: {
                Image(systemName: "ellipsis").font(.system(size: 14, weight: .semibold)).frame(width: 25, height: 25)
            }
            .menuStyle(.borderlessButton).menuIndicator(.hidden).fixedSize().accessibilityLabel("更多选项")
        }
    }

    private func iconButton(_ symbol: String, _ label: String, action: @escaping () -> Void) -> some View {
        Button(action: action) {
            Image(systemName: symbol).font(.system(size: 13, weight: .medium)).frame(width: 25, height: 25)
                .contentShape(Rectangle())
        }.buttonStyle(.plain).help(label).accessibilityLabel(label)
    }

    private var search: some View {
        VStack(alignment: .leading, spacing: 8) {
            HStack {
                TextField("搜索城市", text: $term).onSubmit(runSearch)
                Button("搜索", action: runSearch).disabled(term.trimmingCharacters(in: .whitespaces).isEmpty)
            }
            ForEach(results) { city in
                Button {
                    model.select(city)
                    searching = false
                    results = []
                    term = ""
                } label: {
                    HStack {
                        Text(city.name).fontWeight(.medium)
                        Text(city.region ?? "").foregroundStyle(.secondary).lineLimit(1)
                        Spacer()
                        Image(systemName: "arrow.up.left").foregroundStyle(.secondary)
                    }.padding(.vertical, 5).contentShape(Rectangle())
                }.buttonStyle(.plain)
            }
            if let searchError { Text(searchError).font(.caption).foregroundStyle(.secondary) }
        }
    }

    private func runSearch() {
        let query = term.trimmingCharacters(in: .whitespacesAndNewlines)
        guard !query.isEmpty else { return }
        searchTask?.cancel()
        searchError = "正在搜索…"
        results = []
        searchTask = Task {
            do {
                let results = try await WeatherAPI.search(query)
                try Task.checkCancellation()
                self.results = results
                searchError = results.isEmpty ? "没有找到这个城市。" : nil
            } catch {
                if !Task.isCancelled { searchError = "城市搜索暂不可用，请重试。" }
            }
        }
    }

    private var favorites: some View {
        ScrollView(.horizontal) {
            HStack(spacing: 8) {
                ForEach(model.favorites) { city in
                    Button { model.select(city) } label: {
                        Text(city.name).font(.subheadline).padding(.horizontal, 10).padding(.vertical, 5)
                            .background(model.city?.id == city.id ? Color.teal.opacity(0.17) : Color.secondary.opacity(0.08),
                                        in: Capsule())
                    }.buttonStyle(.plain)
                }
            }
        }.scrollIndicators(.hidden)
    }

    private func current(_ weather: Weather) -> some View {
        let condition = Weather.appearance(weather.code, day: weather.isDay)
        return HStack(spacing: compact ? 12 : 16) {
            WeatherIcon(code: weather.code, day: weather.isDay, size: compact ? 46 : 68)
                .background {
                    weatherGlow(weather)
                        .frame(width: 190, height: 156)
                        .allowsHitTesting(false)
                }
            Text(degrees(weather.temperature))
                .font(.system(size: compact ? 39 : 51, weight: .light, design: .rounded))
                .monospacedDigit()
                .accessibilityLabel("\(Int(weather.temperature.rounded())) 摄氏度")
            VStack(alignment: .leading, spacing: 4) {
                Text(compact ? (model.city?.name ?? condition.name) : (weather.conditionText ?? condition.name))
                    .font(.system(size: compact ? 15 : 17, weight: .semibold))
                Text("体感 \(degrees(weather.feelsLike))").font(.subheadline).foregroundStyle(.secondary)
            }
            Spacer(minLength: 0)
        }
    }

    private func weatherGlow(_ weather: Weather) -> some View {
        let tones: (UInt32, UInt32) = switch weather.code {
        case -2: (0xE0A957, 0xB77D45)
        case -1: (0xB4A47E, 0x97A2A5)
        case 95...99: (0xA579EA, 0x5790EE)
        case 71...77, 85...86: (0x7DD3EE, 0xABB8EA)
        case 45, 48: (0x8EB8CF, 0x97BFD0)
        case 51...67, 80...82: (0x41B5EF, 0x4886DB)
        case 3: (0x88BADD, 0x7CA7CB)
        case 2 where weather.isDay != false: (0xFACA71, 0x97CAE1)
        default: weather.isDay == false ? (0x557DF4, 0x938BEB) : (0xFFCA5B, 0xFFAD4E)
        }
        let strength = colorScheme == .dark ? 0.29 : 0.21
        func color(_ value: UInt32) -> Color {
            Color(red: Double((value >> 16) & 255) / 255,
                  green: Double((value >> 8) & 255) / 255, blue: Double(value & 255) / 255)
        }
        return ZStack {
            RadialGradient(colors: [color(tones.1).opacity(strength * 0.58), .clear],
                           center: UnitPoint(x: 0.58, y: 0.48), startRadius: 2, endRadius: 95)
            RadialGradient(colors: [color(tones.0).opacity(strength), .clear],
                           center: .center, startRadius: 3, endRadius: 73)
        }
    }

    private func alerts(_ weather: Weather) -> some View {
        VStack(alignment: .leading, spacing: 7) {
            ForEach(weather.currentAlerts) { alert in
                DisclosureGroup {
                    VStack(alignment: .leading, spacing: 5) {
                        Text(alert.headline).font(.headline)
                        Text(alert.description)
                        if !alert.instruction.isEmpty { Text(alert.instruction) }
                        Text(alert.source).foregroundStyle(.secondary)
                    }.font(.subheadline).padding(.top, 4)
                } label: {
                    Label(alert.event, systemImage: "exclamationmark.triangle.fill")
                        .font(.subheadline.weight(.semibold))
                        .foregroundStyle(alert.level == "red" ? .red : .orange)
                }
            }
        }
    }

    private func rainTrend(_ weather: Weather) -> some View {
        let series = weather.shortTermPrecipitation
        let items = series?.nextHour() ?? []
        return VStack(alignment: .leading, spacing: 5) {
            Label("未来一小时降水", systemImage: "drop.fill").foregroundStyle(.secondary)
            if items.isEmpty {
                if let probability = weather.rain { Text("逐时降水概率 \(Int(probability.rounded()))%") }
                else { Text("短时降水数据暂不可用") }
            } else if items.allSatisfy({ $0.amountMm != nil }) {
                let peak = items.compactMap(\.amountMm).max() ?? 0
                if peak == 0 {
                    Text("当前预报时段暂无降水")
                } else {
                    HStack(alignment: .bottom, spacing: 3) {
                        ForEach(Array(items.enumerated()), id: \.offset) { _, item in
                            RoundedRectangle(cornerRadius: 2).fill(Color.cyan.opacity(0.6))
                                .frame(height: max(2, 22 * (item.amountMm ?? 0) / peak))
                                .frame(maxWidth: .infinity)
                        }
                    }.accessibilityLabel("降水趋势")
                }
                Text(series?.precisionNote ?? "").font(.caption2).foregroundStyle(.secondary)
            } else {
                Text("降水数据更新中")
            }
        }.font(.subheadline)
    }

    private func hourly(_ weather: Weather) -> some View {
        let entries = Array(weather.hours.enumerated()).filter { $0.offset % 3 == 0 }
        let temps = entries.map { $0.element.temperature }
        let range = ((temps.min() ?? 0) - 3)...((temps.max() ?? 0) + 3)
        return VStack(alignment: .leading, spacing: 8) {
            Divider()
            Label("逐时预报", systemImage: "clock").font(.headline)
            Chart(entries, id: \.offset) { index, hour in
                let x = Double(index)
                AreaMark(x: .value("时段", x), yStart: .value("起点", range.lowerBound), yEnd: .value("温度", hour.temperature))
                    .interpolationMethod(.monotone)
                    .foregroundStyle(.linearGradient(colors: [.teal.opacity(0.20), .clear], startPoint: .top, endPoint: .bottom))
                LineMark(x: .value("时段", x), y: .value("温度", hour.temperature))
                    .interpolationMethod(.monotone).foregroundStyle(.teal)
                PointMark(x: .value("时段", x), y: .value("温度", hour.temperature)).foregroundStyle(.teal)
                    .annotation(position: .top) { Text(degrees(hour.temperature)).font(.caption2).monospacedDigit() }
            }
            .chartXScale(domain: -1.5...(Double(entries.last?.offset ?? 0) + 1.5))
            .chartYScale(domain: range).chartXAxis(.hidden).chartYAxis(.hidden)
            .frame(height: 72)
            HStack(spacing: 0) {
                ForEach(entries, id: \.offset) { _, hour in
                    VStack(spacing: 3) {
                        WeatherIcon(code: hour.code, day: hour.isDay, size: 22)
                        Text(hour.time.count >= 16 ? String(hour.time.dropFirst(11).prefix(5)) : hour.time)
                            .font(.caption2).foregroundStyle(.secondary)
                    }.frame(maxWidth: .infinity)
                }
            }
        }
    }

    private func daily(_ weather: Weather) -> some View {
        let minTemp = weather.days.map(\.min).min() ?? 0
        let span = max(1, (weather.days.map(\.max).max() ?? 0) - minTemp)
        let items = Array(weather.days.prefix(moreDays ? weather.days.count : 4))
        return VStack(alignment: .leading, spacing: 7) {
            Divider()
            HStack {
                Label("逐日天气", systemImage: "calendar").font(.headline)
                Spacer()
                if weather.days.count > 4 {
                    Button(moreDays ? "收起" : "更多") { moreDays.toggle() }.buttonStyle(.plain).foregroundStyle(.secondary)
                }
            }
            ForEach(Array(items.enumerated()), id: \.offset) { index, day in
                HStack(spacing: 10) {
                    Text(index == 0 ? "今天" : String(day.date.prefix(10).suffix(5)))
                        .frame(width: 45, alignment: .leading)
                    WeatherIcon(code: day.code, size: 22)
                    Text(degrees(day.min)).foregroundStyle(.secondary).frame(width: 32, alignment: .trailing)
                    GeometryReader { proxy in
                        Capsule().fill(Color.secondary.opacity(0.12)).overlay(alignment: .leading) {
                            Capsule().fill(.linearGradient(colors: [.teal, .yellow.opacity(0.85)],
                                                          startPoint: .leading, endPoint: .trailing))
                                .frame(width: max(2, proxy.size.width * max(0, day.max - day.min) / span))
                                .offset(x: proxy.size.width * (day.min - minTemp) / span)
                        }
                    }.frame(height: 5)
                    Text(degrees(day.max)).frame(width: 34, alignment: .trailing)
                }.font(.subheadline).monospacedDigit()
            }
        }
    }

    private func conditions(_ weather: Weather) -> some View {
        VStack(alignment: .leading, spacing: 9) {
            Divider()
            Grid(alignment: .leading, horizontalSpacing: 22, verticalSpacing: 13) {
                GridRow {
                    reading("风速", "\(direction(weather.windDirection))  \(decimal(weather.wind, digits: 1)) km/h", "wind")
                    reading(weather.aqiStandard ?? "空气质量", weather.aqi.map { decimal($0) } ?? "暂无", "aqi.medium")
                }
                GridRow {
                    reading("相对湿度", "\(decimal(weather.humidity))%", "humidity")
                    reading("气压", weather.pressureHpa.map { "\(decimal($0)) hPa" } ?? "暂无", "gauge.with.dots.needle.50percent")
                }
                GridRow {
                    reading("紫外线", weather.uvIndex.map { decimal($0, digits: 1) } ?? "暂无", "sun.max")
                    reading("PM₂.₅ / PM₁₀", [weather.pm25, weather.pm10].map { $0.map { decimal($0) } ?? "—" }.joined(separator: " / ") + " µg/m³", "aqi.low")
                }
            }
        }
    }

    private func reading(_ title: String, _ value: String, _ icon: String) -> some View {
        VStack(alignment: .leading, spacing: 4) {
            Label(title, systemImage: icon).font(.caption).foregroundStyle(.secondary)
            Text(value).font(.subheadline.weight(.semibold)).monospacedDigit()
        }.frame(maxWidth: .infinity, alignment: .leading)
    }

    private func footer(_ weather: Weather) -> some View {
        VStack(alignment: .leading, spacing: 4) {
            let time = WeatherAPI.date(weather.updated)?.formatted(date: .omitted, time: .shortened)
            Text("\(weather.source)\(time.map { "  ·  更新于 \($0)" } ?? "")")
            ForEach(weather.attributions ?? [], id: \.self) { Text($0) }
        }.font(.caption2).foregroundStyle(.secondary)
    }

    private func degrees(_ temperature: Double) -> String { "\(decimal(temperature))°" }

    private func decimal(_ value: Double, digits: Int = 0) -> String {
        value.formatted(.number.precision(.fractionLength(digits)))
    }

    private func direction(_ value: Double) -> String {
        let labels = ["北风", "东北风", "东风", "东南风", "南风", "西南风", "西风", "西北风"]
        return labels[Int((max(0, min(360, value)) / 45).rounded()) % 8]
    }
}
