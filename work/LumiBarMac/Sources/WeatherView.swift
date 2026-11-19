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
    let appearance: AppearanceModel
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
    @ViewState private var showSources = false
    @ViewState private var selectedAlert: Weather.Alert?
    @Environment(\.colorScheme) private var colorScheme

    var body: some View {
        Group {
            if compact {
                VStack(spacing: 12) {
                    if let weather = model.weather {
                        hoverSummary(weather)
                    } else {
                        Text(model.city?.name ?? "LumiBar 天气").font(.headline)
                        Text(model.error ?? (model.loading ? "正在更新天气…" : "点击选择城市"))
                            .foregroundStyle(.secondary)
                    }
                }
                .padding(.horizontal, 22).padding(.vertical, 17)
                .frame(width: width - 16)
            } else {
                ScrollView {
                    VStack(alignment: .leading, spacing: 8) {
                        header
                        citySwitcher
                        if searching || model.city == nil { search }
                        if let message = model.locationError ?? model.error {
                            Text(message).font(.caption).foregroundStyle(.secondary)
                        }
                        if let weather = model.weather {
                            if let date = WeatherAPI.date(weather.updated), Date.now.timeIntervalSince(date) > 7200 {
                                Label("显示上次保存的天气，请刷新。", systemImage: "clock.badge.exclamationmark")
                                    .font(.caption).foregroundStyle(.secondary)
                            }
                            current(weather)
                            rainTrend(weather)
                            if !weather.hours.isEmpty { section { hourly(weather) } }
                            if !weather.days.isEmpty { section { daily(weather) } }
                            section { conditions(weather) }
                        } else if model.loading {
                            ProgressView("正在更新天气…").frame(maxWidth: .infinity).padding(.vertical, 36)
                        }
                    }
                    .padding(.horizontal, 22).padding(.vertical, 16)
                    .frame(maxWidth: .infinity, alignment: .leading)
                }
                .scrollIndicators(.hidden)
                .frame(width: width - 16, height: height - 16)
            }
        }
        .background {
            RoundedRectangle(cornerRadius: 22).fill(appearance.tint(colorScheme))
        }
        .glassEffect(.regular, in: RoundedRectangle(cornerRadius: 22))
        .padding(8)
        .frame(width: width, height: height, alignment: .top)
        .onDisappear { searchTask?.cancel() }
    }

    private var accent: Color { appearance.accent.color }

    private func hoverSummary(_ weather: Weather) -> some View {
        let appearance = Weather.appearance(weather.code, day: weather.isDay)
        return VStack(spacing: 17) {
            HStack(alignment: .center, spacing: 8) {
                WeatherIcon(code: weather.code, day: weather.isDay, size: 29)
                    .background { weatherGlow(weather).frame(width: 142, height: 112).allowsHitTesting(false) }
                Text(model.city?.name ?? "天气").font(.system(size: 17, weight: .semibold)).lineLimit(1)
                Text(weather.conditionText ?? appearance.name).font(.system(size: 12)).foregroundStyle(.secondary).lineLimit(1)
                Text(degrees(weather.temperature) + (model.fahrenheit ? "F" : "C"))
                    .font(.system(size: 27, weight: .semibold)).monospacedDigit().fixedSize()
            }
            HStack(alignment: .top, spacing: 5) {
                hoverReading("体感", degrees(weather.feelsLike), "当前", "thermometer.medium")
                hoverReading("降水", hoverRain(weather), "未来 1 小时", "cloud.rain.fill")
                hoverReading("空气", weather.aqi.map { decimal($0) } ?? "—", weather.aqiCaption, "leaf")
            }
        }
    }

    private func hoverRain(_ weather: Weather) -> String {
        if let probability = weather.rain { return decimal(probability) + "%" }
        if let rain = weather.shortTermPrecipitation {
            let amounts = rain.nextHour().compactMap(\.amountMm)
            if !amounts.isEmpty { return amounts.allSatisfy { $0 == 0 } ? "0%" : "有雨" }
        }
        return "—"
    }

    private func hoverReading(_ label: String, _ reading: String, _ caption: String, _ symbol: String) -> some View {
        VStack(spacing: 6) {
            HStack(spacing: 4) {
                Image(systemName: symbol).foregroundStyle(accent)
                Text(label).foregroundStyle(.secondary)
            }.font(.system(size: 12))
            Text(reading).font(.system(size: 24, weight: .semibold)).monospacedDigit()
            Text(caption).font(.system(size: 11)).foregroundStyle(.secondary).lineLimit(1)
        }.frame(maxWidth: .infinity)
    }

    private var header: some View {
        HStack(alignment: .firstTextBaseline, spacing: 9) {
            Text(model.city?.name ?? "你的天气")
                .font(.system(size: 24, weight: .semibold)).lineLimit(1)
                .layoutPriority(2)
            if let weather = model.weather {
                Button(weather.source) { showSources.toggle() }
                    .buttonStyle(.plain).font(.caption2).foregroundStyle(.secondary)
                    .lineLimit(1).fixedSize().help("数据来源")
                    .popover(isPresented: $showSources) {
                        VStack(alignment: .leading, spacing: 7) {
                            Text(weather.source).font(.headline)
                            ForEach(Array(((weather.attributions ?? []) + (weather.providerNote ?? [])).enumerated()),
                                    id: \.offset) { _, line in
                                Text(line).font(.caption).foregroundStyle(.secondary)
                            }
                        }.padding(16).frame(maxWidth: 300, alignment: .leading)
                    }
            }
            Text(weatherStatus).font(.caption2).foregroundStyle(.secondary).lineLimit(1)
            Spacer(minLength: 0)
            iconButton("arrow.clockwise", "更新天气") { model.refresh() }.disabled(model.city == nil || model.loading)
            Menu {
                Button("选择城市") { searching.toggle() }
                if let city = model.city {
                    Button(model.favorites.contains(where: { $0.id == city.id }) ? "从收藏移除" : "收藏当前城市") {
                        model.toggleFavorite(city)
                    }
                }
                Button("设置…", action: showSettings)
                Divider()
                Button("退出 LumiBar", action: quit)
            } label: {
                Image(systemName: "ellipsis").font(.system(size: 14, weight: .semibold)).frame(width: 25, height: 25)
            }
            .menuStyle(.borderlessButton).menuIndicator(.hidden).fixedSize().accessibilityLabel("更多选项")
        }
    }

    private var weatherStatus: String {
        if model.loading { return "正在更新…" }
        if model.error != nil { return "更新失败" }
        if let updated = (model.weather?.updated).flatMap(WeatherAPI.date) {
            return "上次更新  " + updated.formatted(.dateTime.hour().minute())
        }
        return ""
    }

    private func section<Content: View>(@ViewBuilder content: () -> Content) -> some View {
        VStack(alignment: .leading, spacing: 5) {
            Rectangle().fill(Color.secondary.opacity(0.14)).frame(height: 1)
            content()
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
                TextField("搜索城市，例如：天津 / Tianjin", text: $term).onSubmit(runSearch)
                Button("搜索", action: runSearch).disabled(term.trimmingCharacters(in: .whitespaces).isEmpty)
            }
            ForEach(results) { city in
                Button {
                    model.saveAndSelect(city)
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

    private var citySwitcher: some View {
        let cities = (model.favorites + Array([model.city].compactMap { $0 })).uniqued()
        return ScrollView(.horizontal) {
            HStack(spacing: 6) {
                Button { model.locate() } label: {
                    HStack(spacing: 5) {
                        Image(systemName: model.usingLocation ? "location.circle" : "location")
                        if let city = model.locatedCity { Text(city.name) }
                    }.font(.system(size: 12))
                    .padding(.horizontal, model.locatedCity == nil ? 0 : 9)
                    .frame(minWidth: 28, minHeight: 28)
                    .background(model.usingLocation ? accent.opacity(0.23) : Color.secondary.opacity(0.09), in: Capsule())
                }.buttonStyle(.plain).help("定位当前位置").accessibilityLabel("定位当前位置")
                ForEach(cities) { city in
                    Button { model.select(city) } label: {
                        Text(city.name).font(.system(size: 12)).padding(.horizontal, 10)
                            .frame(height: 28)
                            .background(!model.usingLocation && model.city?.id == city.id ? accent.opacity(0.23) : Color.secondary.opacity(0.09),
                                        in: Capsule())
                    }
                    .buttonStyle(.plain)
                    .contextMenu {
                        if model.favorites.contains(where: { $0.id == city.id }) {
                            Button("移除已保存城市") { model.toggleFavorite(city) }
                        }
                    }
                }
                iconButton("plus", "添加城市") { searching.toggle() }
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
            Text(degrees(weather.temperature) + (compact ? "" : model.fahrenheit ? "F" : "C"))
                .font(.system(size: compact ? 39 : 52, weight: .regular))
                .monospacedDigit()
                .fixedSize()
                .accessibilityLabel("实况温度 " + degrees(weather.temperature))
            VStack(alignment: .leading, spacing: 6) {
                HStack(spacing: 4) {
                    Text(compact ? (model.city?.name ?? condition.name) : (weather.conditionText ?? condition.name))
                        .font(.system(size: compact ? 15 : 18)).lineLimit(1)
                    if !compact { alertBadges(weather) }
                }
                if let today = weather.days.first, !compact {
                    Text("最低 \(degrees(today.min))  最高 \(degrees(today.max)) · 体感 \(degrees(weather.feelsLike))")
                        .font(.system(size: 12)).foregroundStyle(.secondary).lineLimit(1).minimumScaleFactor(0.76)
                } else {
                    Text("体感 \(degrees(weather.feelsLike))").font(.subheadline).foregroundStyle(.secondary)
                }
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

    private func alertBadges(_ weather: Weather) -> some View {
        let visible = Array(weather.currentAlerts.prefix(2))
        return ForEach(visible) { alert in
            Button {
                selectedAlert = alert
            } label: {
                let tone: Color = switch alert.level {
                case "red": .red
                case "orange": .orange
                case "blue": .blue
                default: .yellow
                }
                HStack(spacing: 4) {
                    Image(systemName: "exclamationmark.triangle.fill").foregroundStyle(tone)
                    Text(alert.event).lineLimit(1)
                }.font(.system(size: 10, weight: .semibold))
                    .padding(.horizontal, 6).padding(.vertical, 4)
                    .background(tone.opacity(0.10), in: RoundedRectangle(cornerRadius: 8))
            }
            .buttonStyle(.plain)
            .popover(isPresented: Binding(get: { selectedAlert?.id == alert.id },
                                           set: { if !$0 { selectedAlert = nil } })) {
                ScrollView {
                    VStack(alignment: .leading, spacing: 8) {
                        Text(alert.headline).font(.headline)
                        Text(alert.description)
                        if !alert.instruction.isEmpty { Text(alert.instruction) }
                        Text(alert.source).foregroundStyle(.secondary)
                    }.font(.subheadline).padding(14)
                }.frame(width: 300, height: 220)
            }
        }
    }

    private func rainTrend(_ weather: Weather) -> some View {
        let series = weather.shortTermPrecipitation
        let now = Date.now
        let items = series?.nextHour(at: now) ?? []
        let peak = max(2, items.compactMap(\.amountMm).max() ?? 0)
        let chart = Chart {
            ForEach(Array(items.enumerated()), id: \.offset) { _, item in
                if let end = WeatherAPI.date(item.end), let amount = item.amountMm {
                    let minutes = min(60, max(0, end.timeIntervalSince(now) / 60))
                    AreaMark(x: .value("分钟", minutes), y: .value("降水", amount))
                        .interpolationMethod(.monotone)
                        .foregroundStyle(.linearGradient(colors: [accent.opacity(0.28), .clear], startPoint: .top, endPoint: .bottom))
                    LineMark(x: .value("分钟", minutes), y: .value("降水", amount))
                        .interpolationMethod(.monotone).foregroundStyle(accent)
                }
            }
        }
        .chartXScale(domain: 0.0...60.0)
        .chartYScale(domain: 0.0...peak)
        .chartXAxis {
            AxisMarks(values: [0.0, 30.0, 60.0]) { tick in
                AxisValueLabel {
                    Text(tick.as(Double.self) == 0 ? "现在" : "\(Int(tick.as(Double.self) ?? 0)) 分钟").font(.system(size: 9))
                }
            }
        }
        .chartYAxis {
            AxisMarks(position: .leading, values: [0, peak / 2, peak]) { tick in
                AxisGridLine()
                AxisValueLabel {
                    Text(tick.as(Double.self)?.formatted(.number.precision(.fractionLength(0...1))) ?? "")
                        .font(.system(size: 9))
                }
            }
        }
        .overlay {
            if items.isEmpty { Text("暂无数据").font(.caption2).foregroundStyle(.secondary) }
            else if items.contains(where: { $0.amountMm == nil }) {
                Text("数据不完整").font(.caption2).foregroundStyle(.secondary)
            }
        }
        return HStack(alignment: .top, spacing: 12) {
            VStack(alignment: .leading, spacing: 5) {
                Text("未来 1 小时降水").font(.system(size: 15, weight: .semibold))
                Text(series?.precisionNote ?? "短时预报").font(.system(size: 10)).foregroundStyle(.secondary)
                    .lineLimit(2)
            }
            .frame(width: 144, alignment: .leading)
            chart.frame(height: 74).overlay(alignment: .topLeading) {
                Text("mm").font(.system(size: 9)).foregroundStyle(.secondary).offset(y: -2)
            }
        }
        .padding(.horizontal, 14).padding(.vertical, 10)
        .background(Color.primary.opacity(0.062), in: RoundedRectangle(cornerRadius: 16))
        .overlay {
            RoundedRectangle(cornerRadius: 16).strokeBorder(Color.primary.opacity(0.06), lineWidth: 1)
        }
    }

    private func hourly(_ weather: Weather) -> some View {
        let count = min(8, weather.hours.count)
        let entries = (0..<count).map { index in
            weather.hours[count == 1 ? 0 : Int((Double(index) * Double(weather.hours.count - 1) / Double(count - 1)).rounded())]
        }
        let low = entries.map(\.temperature).min() ?? 0
        let span = max(3, (entries.map(\.temperature).max() ?? 0) - low)
        return VStack(alignment: .leading, spacing: 5) {
            Text("未来 24 小时").font(.system(size: 16, weight: .semibold))
            Canvas { context, size in
                guard !entries.isEmpty else { return }
                let column = size.width / CGFloat(entries.count)
                let points = entries.enumerated().map { index, hour in
                    CGPoint(x: column * (Double(index) + 0.5), y: 48 - (hour.temperature - low) / span * 25)
                }
                var curve = Path()
                curve.move(to: points[0])
                for (start, end) in zip(points, points.dropFirst()) {
                    let dx = (end.x - start.x) / 3
                    curve.addCurve(to: end, control1: CGPoint(x: start.x + dx, y: start.y),
                                   control2: CGPoint(x: end.x - dx, y: end.y))
                }
                if let end = points.last, points.count > 1 {
                    var area = curve
                    area.addLine(to: CGPoint(x: end.x, y: size.height))
                    area.addLine(to: CGPoint(x: points[0].x, y: size.height))
                    area.closeSubpath()
                    context.fill(area, with: .linearGradient(Gradient(colors: [temperatureColor(1).opacity(0.18), .clear]),
                                                             startPoint: .zero, endPoint: CGPoint(x: 0, y: size.height)))
                    context.stroke(curve, with: .linearGradient(Gradient(colors: [temperatureColor(1), temperatureColor(0.5),
                                                                                 temperatureColor(0)]),
                                                                 startPoint: CGPoint(x: 0, y: 23), endPoint: CGPoint(x: 0, y: 48)),
                                   style: StrokeStyle(lineWidth: 1.8, lineCap: .round))
                }
                for (point, hour) in zip(points, entries) {
                    let dot = Path(ellipseIn: CGRect(x: point.x - 2.5, y: point.y - 2.5, width: 5, height: 5))
                    context.fill(dot, with: .color(colorScheme == .dark ? Color(white: 0.22) : Color(white: 0.96)))
                    context.stroke(dot, with: .color(temperatureColor((hour.temperature - low) / span)), lineWidth: 1.25)
                    context.draw(Text(degrees(hour.temperature)).font(.system(size: 10)),
                                 at: CGPoint(x: point.x, y: point.y - 13))
                }
            }.frame(height: 61).accessibilityHidden(true)
            HStack(spacing: 0) {
                ForEach(Array(entries.enumerated()), id: \.offset) { _, hour in
                    VStack(spacing: 3) {
                        WeatherIcon(code: hour.code, day: hour.isDay, size: 26)
                        Text(hour.time.count >= 16 ? String(hour.time.dropFirst(11).prefix(5)) : hour.time)
                            .font(.system(size: 11)).foregroundStyle(.secondary)
                        Text(degrees(hour.temperature)).font(.system(size: 13)).monospacedDigit()
                    }.frame(maxWidth: .infinity)
                }
            }
        }
    }

    private func daily(_ weather: Weather) -> some View {
        let minTemp = weather.days.map(\.min).min() ?? 0
        let span = max(1, (weather.days.map(\.max).max() ?? 0) - minTemp)
        let items = Array(weather.days.prefix(moreDays ? weather.days.count : 4))
        return VStack(alignment: .leading, spacing: 4) {
            HStack {
                Text("\(weather.days.count) 天天气预报").font(.system(size: 16, weight: .semibold))
                Spacer()
                if weather.days.count > 4 {
                    Button(moreDays ? "收起" : "展开") { moreDays.toggle() }
                        .buttonStyle(.plain).font(.system(size: 12)).foregroundStyle(.secondary)
                }
            }
            ForEach(Array(items.enumerated()), id: \.offset) { index, day in
                let label = dayLabel(day.date, index: index)
                HStack(spacing: 10) {
                    HStack(spacing: 4) {
                        Text(label.weekday).font(.system(size: 14))
                        Text(label.date).font(.system(size: 11)).foregroundStyle(.secondary)
                    }.frame(width: 112, alignment: .leading)
                    WeatherIcon(code: day.code, size: 24).frame(width: 32)
                    Text(degrees(day.min)).foregroundStyle(.secondary).frame(width: 58, alignment: .trailing)
                    GeometryReader { proxy in
                        let low = (day.min - minTemp) / span
                        let high = (day.max - minTemp) / span
                        Capsule().fill(Color.secondary.opacity(0.12)).overlay(alignment: .leading) {
                            Capsule().fill(.linearGradient(colors: [temperatureColor(low), temperatureColor((low + high) / 2),
                                                                    temperatureColor(high)],
                                                          startPoint: .leading, endPoint: .trailing))
                                .frame(width: max(2, proxy.size.width * max(0, high - low)))
                                .offset(x: proxy.size.width * low)
                        }
                    }.frame(height: 5)
                    Text(degrees(day.max)).frame(width: 36, alignment: .trailing)
                }.frame(minHeight: 28).font(.system(size: 14)).monospacedDigit()
            }
        }
    }

    private func dayLabel(_ text: String, index: Int) -> (weekday: String, date: String) {
        let parts = text.prefix(10).split(separator: "-").compactMap { Int($0) }
        guard parts.count == 3 else { return ("", text) }
        let calendar = Calendar(identifier: .gregorian)
        let day = calendar.date(from: DateComponents(year: parts[0], month: parts[1], day: parts[2]))
        let weekdays = ["周日", "周一", "周二", "周三", "周四", "周五", "周六"]
        let weekday = index == 0 ? "今天" : index == 1 ? "明天" : day.map { weekdays[calendar.component(.weekday, from: $0) - 1] } ?? ""
        return (weekday, "\(parts[1])/\(parts[2])")
    }

    private func temperatureColor(_ t: Double) -> Color {
        let x = min(1, max(0, t))
        let cool = (71.0, 153.0, 255.0), center = (164.0, 151.0, 218.0), warm = (255.0, 165.0, 79.0)
        let a = x < 0.5 ? cool : center
        let b = x < 0.5 ? center : warm
        let f = x < 0.5 ? x * 2 : (x - 0.5) * 2
        return Color(red: (a.0 + (b.0 - a.0) * f) / 255, green: (a.1 + (b.1 - a.1) * f) / 255,
                     blue: (a.2 + (b.2 - a.2) * f) / 255)
    }

    private func conditions(_ weather: Weather) -> some View {
        let wind = model.windMetersPerSecond ? weather.wind / 3.6 : weather.wind
        return HStack(spacing: 8) {
            reading("湿度 · 相对", "\(decimal(weather.humidity))%", "drop")
            reading("风速 · \(model.windMetersPerSecond ? "m/s" : "km/h")",
                    decimal(wind, digits: 1) + " " + direction(weather.windDirection), "wind")
            reading("空气 · \(weather.aqiStandard ?? "AQI")",
                    weather.aqi.map { decimal($0) } ?? "—", "leaf")
                .help([weather.pm25.map { "PM₂.₅ " + decimal($0) },
                       weather.pm10.map { "PM₁₀ " + decimal($0) }].compactMap { $0 }.joined(separator: "  "))
            reading("气压 · hPa", weather.pressureHpa.map { decimal($0) } ?? "—", "gauge.with.dots.needle.50percent")
            reading("紫外线 · UVI", weather.uvIndex.map { decimal($0, digits: 1) } ?? "—", "sun.max")
        }.padding(.top, 4)
    }

    private func reading(_ title: String, _ value: String, _ icon: String) -> some View {
        VStack(spacing: 4) {
            HStack(spacing: 4) {
                Image(systemName: icon).foregroundStyle(accent)
                Text(title).foregroundStyle(.secondary)
            }.font(.system(size: 10)).lineLimit(1).minimumScaleFactor(0.75).frame(height: 20)
            Text(value).font(.system(size: 16, weight: .semibold)).monospacedDigit()
                .lineLimit(1).minimumScaleFactor(0.66).frame(height: 28)
        }.frame(maxWidth: .infinity)
    }

    private func degrees(_ temperature: Double) -> String { model.temperature(temperature) }

    private func decimal(_ value: Double, digits: Int = 0) -> String {
        value.formatted(.number.precision(.fractionLength(digits)))
    }

    private func direction(_ value: Double) -> String {
        let labels = ["北风", "东北风", "东风", "东南风", "南风", "西南风", "西风", "西北风"]
        return labels[Int((max(0, min(360, value)) / 45).rounded()) % 8]
    }
}
