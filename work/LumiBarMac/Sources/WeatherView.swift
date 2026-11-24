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
    private static var centeredImages: [ObjectIdentifier: NSImage] = [:]

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

    static func centered(_ original: NSImage) -> NSImage {
        let key = ObjectIdentifier(original)
        if let image = centeredImages[key] { return image }
        guard let bitmap = original.cgImage(forProposedRect: nil, context: nil, hints: nil) else { return original }
        let width = bitmap.width
        let height = bitmap.height
        let stride = width * 4
        guard let context = CGContext(data: nil, width: width, height: height, bitsPerComponent: 8,
                                      bytesPerRow: stride, space: CGColorSpaceCreateDeviceRGB(),
                                      bitmapInfo: CGImageAlphaInfo.premultipliedLast.rawValue),
              let pixels = context.data?.assumingMemoryBound(to: UInt8.self) else { return original }
        let canvas = CGRect(x: 0, y: 0, width: width, height: height)
        context.draw(bitmap, in: canvas)
        var left = width, top = height, right = -1, bottom = -1
        for y in 0..<height {
            for x in 0..<width where pixels[y * stride + x * 4 + 3] >= 16 {
                left = min(left, x)
                top = min(top, y)
                right = max(right, x)
                bottom = max(bottom, y)
            }
        }
        guard right >= left, bottom >= top else { return original }
        let visible = CGRect(x: left, y: top, width: right - left + 1, height: bottom - top + 1)
        guard let cropped = context.makeImage()?.cropping(to: visible) else { return original }
        context.clear(canvas)
        context.draw(cropped, in: CGRect(x: (canvas.width - visible.width) / 2, y: (canvas.height - visible.height) / 2,
                                        width: visible.width, height: visible.height))
        guard let centered = context.makeImage() else { return original }
        let image = NSImage(cgImage: centered, size: original.size)
        centeredImages[key] = image
        return image
    }
}

struct WeatherIcon: View {
    let code: Int
    var day: Bool? = nil
    let size: CGFloat
    var centersArtwork = false

    var body: some View {
        Group {
            if let original = WeatherGlyph.image(code, day: day) {
                Image(nsImage: centersArtwork ? WeatherGlyph.centered(original) : original).resizable().scaledToFit()
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
    // The flyout has a transparent 16-point margin on each edge.
    nonisolated static let hoverSize = CGSize(width: 272 + 32, height: 104 + 32)

    @Bindable var model: WeatherModel
    let appearance: AppearanceModel
    let compact: Bool
    let width: CGFloat
    let height: CGFloat?
    var referenceDate: Date? = nil
    var measured: (CGFloat) -> Void = { _ in }
    var alertWindowChanged: (NSWindow?) -> Void = { _ in }
    var showSettings: () -> Void
    var quit: () -> Void
    @ViewState private var searching = false
    @ViewState private var moreDays = false
    @ViewState private var showSources = false
    @Environment(\.colorScheme) private var colorScheme
    @Environment(\.accessibilityReduceMotion) private var reduceMotion

    var body: some View {
        GeometryReader { geometry in
            if compact {
                hoverBody
                    .frame(width: max(1, width - 32), height: max(1, geometry.size.height - 32))
                    .modifier(FlyoutChrome(appearance: appearance, hasShadow: false))
            } else {
                ScrollView {
                    VStack(alignment: .leading, spacing: 8) {
                        header
                        if searching || model.city == nil {
                            WeatherCitySearch(model: model) { city in
                                model.saveAndSelect(city)
                                searching = false
                            }
                        }
                        if let message = model.locationError ?? model.error {
                            Text(message).font(.caption).foregroundStyle(.secondary)
                        }
                        if let weather = model.weather {
                            if let date = WeatherAPI.date(weather.updated), now.timeIntervalSince(date) > 7200 {
                                Label("显示上次保存的天气，请刷新。", systemImage: "clock.badge.exclamationmark")
                                    .font(.caption).foregroundStyle(.secondary)
                            }
                            forecastDetails(weather)
                        } else if model.loading {
                            ProgressView("正在更新天气…").frame(maxWidth: .infinity).padding(.vertical, 36)
                        }
                    }
                    .padding(.horizontal, 22).padding(.vertical, 16)
                    .frame(maxWidth: .infinity, alignment: .leading)
                    .onGeometryChange(for: CGFloat.self, of: { $0.size.height }) { value in measured(value + 32) }
                }
                .scrollIndicators(.never, axes: .vertical)
                .frame(width: max(1, width - 32), height: max(1, geometry.size.height - 32))
                .modifier(FlyoutChrome(appearance: appearance, hasShadow: false))
            }
        }
        .frame(width: width, height: height, alignment: .top)
    }

    @ViewBuilder
    private func forecastDetails(_ weather: Weather) -> some View {
        let details = temperatureDetails(weather)
        if model.showsDetail(.current) {
            current(weather, temperatureDetails: details)
        } else {
            weatherAlerts(weather)
            if let details { temperatureDetailLabel(details) }
        }
        if model.showsDetail(.precipitation) { rainTrend(weather) }
        if model.showsDetail(.hourly), !weather.hours.isEmpty { section { hourly(weather) } }
        if model.showsDetail(.daily), !weather.days.isEmpty { section { daily(weather) } }
        if WeatherDetailItem.readings.contains(where: model.showsDetail) { section { conditions(weather) } }
    }

    @ViewBuilder
    private func weatherAlerts(_ weather: Weather) -> some View {
        if model.showsDetail(.alerts), weather.alerts?.isEmpty == false {
            if let referenceDate {
                weatherAlerts(weather, at: referenceDate)
            } else {
                TimelineView(.everyMinute) { context in weatherAlerts(weather, at: context.date) }
            }
        }
    }

    @ViewBuilder
    private func weatherAlerts(_ weather: Weather, at date: Date) -> some View {
        let groups = weather.currentAlertGroups(at: date)
        if !groups.isEmpty { WeatherAlertBadges(groups: groups, windowChanged: alertWindowChanged) }
    }

    private var now: Date { referenceDate ?? .now }

    private var hoverBody: some View {
        Group {
            if let weather = model.weather {
                hoverSummary(weather)
            } else {
                VStack(spacing: 8) {
                    Text(model.city?.name ?? "LumiBar 天气").font(.headline)
                    Text(model.error ?? (model.loading ? "正在更新天气…" : "点击选择城市"))
                        .foregroundStyle(.secondary)
                }
            }
        }
        .padding(.horizontal, 14).padding(.vertical, 10)
    }

    private var accent: Color { Color.accentColor }

    private func hoverSummary(_ weather: Weather) -> some View {
        let condition = Weather.appearance(weather.code, day: weather.isDay)
        return HStack(spacing: 13) {
            VStack(spacing: 4) {
                appearance.styledText(model.city?.name ?? "天气", size: 14, weight: .medium)
                    .lineLimit(1).minimumScaleFactor(0.85)
                HStack(spacing: 4) {
                    WeatherIcon(code: weather.code, day: weather.isDay, size: 27, centersArtwork: true)
                    appearance.styledText(degrees(weather.temperature) + (model.fahrenheit ? "F" : "C"),
                                          size: 23, weight: .regular)
                        .monospacedDigit().lineLimit(1).minimumScaleFactor(0.72)
                }.frame(height: 28)
                appearance.styledText(weather.conditionText ?? condition.name, size: 12, weight: .medium)
                    .foregroundStyle(.secondary).lineLimit(1).minimumScaleFactor(0.92)
            }
            .frame(width: 90)
            .background { weatherGlow(weather) }
            VStack(spacing: 0) {
                let feelsLike = model.fahrenheit ? weather.feelsLike * 1.8 + 32 : weather.feelsLike
                hoverReading("体感温度", feelsLike, unit: "°", symbol: "thermometer.medium")
                hoverReading("临近降水", weather.rain, unit: "%", symbol: "cloud.rain")
                hoverReading("空气质量", weather.aqi, unit: "", symbol: "leaf")
            }
            .padding(.leading, 13)
            .overlay(alignment: .leading) { Rectangle().fill(.primary.opacity(0.12)).frame(width: 1) }
        }
    }

    private func hoverReading(_ label: String, _ value: Double?, unit: String, symbol: String) -> some View {
        let reading = value.map { decimal($0).replacingOccurrences(of: "-", with: "−") } ?? "—"
        let suffix = value == nil ? "" : unit
        return HStack(spacing: 2) {
            HStack(spacing: 5) {
                Image(systemName: symbol)
                    .symbolRenderingMode(.monochrome)
                    .font(.system(size: 11, weight: .light))
                    .foregroundStyle(accent)
                    .frame(width: 12, height: 12)
                appearance.styledText(label, size: 12).foregroundStyle(.secondary).fixedSize()
            }.frame(width: 66, alignment: .leading)
            HStack(alignment: .firstTextBaseline, spacing: 2) {
                appearance.styledText(reading, size: 17, weight: .regular)
                    .monospacedDigit().lineLimit(1).minimumScaleFactor(0.8)
                    .frame(maxWidth: .infinity, alignment: .trailing)
                appearance.styledText(suffix, size: suffix == "%" ? 11.5 : 13, weight: .medium)
                    .foregroundStyle(.secondary.opacity(colorScheme == .dark ? 0.76 : 0.84))
                    .frame(width: 13, alignment: .leading)
            }
        }.frame(height: 23)
            .accessibilityElement(children: .ignore)
            .accessibilityLabel(label)
            .accessibilityValue(value == nil ? "暂无数据" : reading + suffix)
    }

    private var header: some View {
        VStack(alignment: .leading, spacing: 5) {
            HStack(spacing: 7) {
                citySwitcher.frame(height: 28)
                iconButton("arrow.clockwise", "更新天气") { model.refresh() }.disabled(model.city == nil || model.loading)
                Menu {
                    Button("选择城市") { searching.toggle() }
                    if let city = model.city {
                        Button(model.isFavorite(city) ? "从收藏移除" : "收藏当前城市") {
                            model.toggleFavorite(city)
                        }.disabled(!model.isFavorite(city) && model.favorites.count >= WeatherModel.favoriteLimit)
                    }
                    Button("设置…", action: showSettings)
                    Divider()
                    Button("退出 LumiBar", action: quit)
                } label: {
                    Image(systemName: "ellipsis").font(.system(size: 14, weight: .semibold)).frame(width: 25, height: 25)
                }
                .menuStyle(.borderlessButton).menuIndicator(.hidden).fixedSize().accessibilityLabel("更多选项")
            }
            let status = weatherStatus
            if model.weather != nil || !status.isEmpty {
                HStack(alignment: .firstTextBaseline, spacing: 0) {
                    if let weather = model.weather {
                        Button(weather.source) { showSources.toggle() }
                            .buttonStyle(.plain).fixedSize().help("数据来源")
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
                    if !status.isEmpty {
                        Text(model.weather == nil ? status : "·" + status)
                    }
                }
                .font(.caption2).foregroundStyle(.secondary).lineLimit(1)
                .frame(maxWidth: .infinity, alignment: .trailing)
            }
        }
    }

    private var weatherStatus: String {
        if model.loading { return "正在更新…" }
        if model.error != nil { return "更新失败" }
        if let updated = (model.weather?.updated).flatMap(WeatherAPI.date) {
            let timeFormat = Date.VerbatimFormatStyle(
                format: "\(hour: .twoDigits(clock: .twentyFourHour, hourCycle: .zeroBased)):\(minute: .twoDigits)",
                timeZone: .current,
                calendar: .current
            )
            return "上次更新" + (referenceDate == nil ? updated.formatted(timeFormat) : "12:00")
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

    private var citySwitcher: some View {
        let current = model.usingLocation ? [] : [model.city].compactMap { $0 }
        let cities = (model.favorites + current).uniqued()
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
                        if model.isFavorite(city) {
                            Button("移除已保存城市") { model.removeFavorite(city) }
                        }
                    }
                }
                iconButton("plus", "添加城市") { searching.toggle() }
            }
        }.scrollIndicators(.hidden)
    }

    private func current(_ weather: Weather, temperatureDetails: String?) -> some View {
        let condition = Weather.appearance(weather.code, day: weather.isDay)
        return HStack(spacing: 12) {
            WeatherIcon(code: weather.code, day: weather.isDay, size: 68, centersArtwork: true)
                .background {
                    weatherGlow(weather, expanded: true).offset(x: 64, y: 13)
                }
                .offset(y: -3)
            appearance.styledText(degrees(weather.temperature) + (model.fahrenheit ? "F" : "C"), size: 52, weight: .light)
                .fixedSize().accessibilityLabel("实况温度 " + degrees(weather.temperature))
            VStack(alignment: .leading, spacing: 6) {
                HStack(spacing: 4) {
                    appearance.styledText(weather.conditionText ?? condition.name, size: 20, weight: .medium)
                        .lineLimit(1).layoutPriority(1)
                    weatherAlerts(weather)
                }
                if let temperatureDetails { temperatureDetailLabel(temperatureDetails) }
            }
            Spacer(minLength: 0)
        }
    }

    private func temperatureDetails(_ weather: Weather) -> String? {
        var details: [String] = []
        if model.showsDetail(.temperatureRange), let today = weather.days.first {
            details.append("最低 \(degrees(today.min))  最高 \(degrees(today.max))")
        }
        if model.showsDetail(.feelsLike) { details.append("体感 \(degrees(weather.feelsLike))") }
        return details.isEmpty ? nil : details.joined(separator: " · ")
    }

    private func temperatureDetailLabel(_ text: String) -> some View {
        appearance.styledText(text, size: 12).foregroundStyle(.secondary)
            .lineLimit(1).minimumScaleFactor(0.76)
    }

    private func weatherGlow(_ weather: Weather, expanded: Bool = false) -> some View {
        let tones = WeatherLight.colors(code: weather.code, day: weather.isDay, saturation: model.capsuleTintSaturation)
        let opacity = expanded ? (colorScheme == .dark ? 0.47 : 0.34) : (colorScheme == .dark ? 0.29 : 0.18)
        let strength = opacity * model.tintScale
        let moving = strength > 0 && expanded && model.capsuleGlow && appearance.animation && !reduceMotion
        let field = expanded ? CGSize(width: 334, height: 244) : CGSize(width: 194, height: 190)
        return TimelineView(.animation(minimumInterval: 1 / 24, paused: !moving)) { context in
            let phase = context.date.timeIntervalSinceReferenceDate * 2 * .pi / 11
            let sweep = moving ? sin(phase) * 0.11 : 0
            let drift = moving ? cos(phase) * 0.08 : 0
            ZStack {
                glowSpot(tones.1, strength: strength * (expanded ? 0.72 : 0.58),
                         startRadius: 2, endRadius: expanded ? 152 : 89)
                    .offset(x: field.width * ((expanded ? 0.61 : 0.54) + sweep - 0.5),
                            y: field.height * (0.49 - drift - 0.5))
                glowSpot(tones.0, strength: strength, startRadius: 3, endRadius: expanded ? 124 : 72)
                    .offset(x: field.width * ((expanded ? 0.31 : 0.50) + sweep * 0.5 - 0.5),
                            y: field.height * drift)
            }
        }
        .allowsHitTesting(false)
    }

    private func glowSpot(_ color: Color, strength: Double, startRadius: CGFloat, endRadius: CGFloat) -> some View {
        // The gradient reaches zero opacity within its own frame as the spot moves.
        RadialGradient(colors: [color.opacity(strength), color.opacity(0)], center: .center,
                       startRadius: startRadius, endRadius: endRadius)
            .frame(width: endRadius * 2, height: endRadius * 2)
    }

    private func rainTrend(_ weather: Weather) -> some View {
        let series = weather.shortTermPrecipitation
        let now = self.now
        let items = series?.nextHour(at: now) ?? []
        let peak = max(2, items.compactMap(\.amountMm).max() ?? 0)
        return HStack(alignment: .top, spacing: 13) {
            VStack(alignment: .leading, spacing: 6) {
                appearance.styledText("未来 1 小时降水", size: 15, weight: .medium)
                appearance.styledText(series?.precisionNote ?? "短时预报", size: 10).foregroundStyle(.secondary)
                    .lineLimit(2)
            }
            .frame(width: 144, alignment: .leading)
            Canvas { context, size in
                let left: CGFloat = 40, top: CGFloat = 13, bottom: CGFloat = 57
                let width = max(1, size.width - left - 3)
                let label = Color.secondary
                for tick in 0...2 {
                    let fraction = Double(tick) / 2
                    let y = bottom - fraction * (bottom - top)
                    var grid = Path()
                    grid.move(to: CGPoint(x: left, y: y))
                    grid.addLine(to: CGPoint(x: left + width, y: y))
                    context.stroke(grid, with: .color(.secondary.opacity(0.16)), lineWidth: 1)
                    let amount = decimal(peak * fraction, digits: 0) + " mm"
                    context.draw(appearance.styledText(amount, size: 9).foregroundStyle(label),
                                 at: CGPoint(x: left - 5, y: y), anchor: .trailing)
                }
                for (fraction, text, anchor) in [(0.0, "现在", UnitPoint.leading), (0.5, "30 分钟", .center), (1.0, "60 分钟", .trailing)] {
                    context.draw(appearance.styledText(text, size: 9).foregroundStyle(label),
                                 at: CGPoint(x: left + fraction * width, y: 67), anchor: anchor)
                }
                let points = items.compactMap { item -> CGPoint? in
                    guard let date = WeatherAPI.date(item.end), let amount = item.amountMm else { return nil }
                    let fraction = min(1, max(0, date.timeIntervalSince(now) / 3600))
                    return CGPoint(x: left + fraction * width, y: bottom - amount / peak * (bottom - top))
                }
                if points.isEmpty || points.count != items.count {
                    let message = items.isEmpty ? "暂无数据" : "数据不完整"
                    context.draw(appearance.styledText(message, size: 10).foregroundStyle(label),
                                 at: CGPoint(x: left + width * 0.5, y: 39))
                } else if let first = points.first, let last = points.last {
                    let curve = smoothCurve(points)
                    var area = curve
                    area.addLine(to: CGPoint(x: last.x, y: bottom))
                    area.addLine(to: CGPoint(x: first.x, y: bottom))
                    area.closeSubpath()
                    context.fill(area, with: .linearGradient(Gradient(colors: [accent.opacity(0.28), .clear]),
                                                             startPoint: CGPoint(x: 0, y: top), endPoint: CGPoint(x: 0, y: bottom)))
                    context.stroke(curve, with: .color(accent), style: StrokeStyle(lineWidth: 1.5, lineCap: .round))
                }
            }.frame(height: 74).accessibilityLabel("未来 1 小时降水量曲线")
        }
        .padding(.horizontal, 14).padding(.vertical, 11)
        .background(Color.primary.opacity(0.062), in: RoundedRectangle(cornerRadius: 16))
        .overlay {
            RoundedRectangle(cornerRadius: 16).strokeBorder(Color.primary.opacity(0.06), lineWidth: 1)
        }
    }

    private func smoothCurve(_ points: [CGPoint]) -> Path {
        var curve = Path()
        guard let first = points.first else { return curve }
        curve.move(to: first)
        for (start, end) in zip(points, points.dropFirst()) {
            let dx = (end.x - start.x) / 3
            curve.addCurve(to: end, control1: CGPoint(x: start.x + dx, y: start.y),
                           control2: CGPoint(x: end.x - dx, y: end.y))
        }
        return curve
    }

    private func hourly(_ weather: Weather) -> some View {
        let count = min(8, weather.hours.count)
        let entries = (0..<count).map { index in
            weather.hours[count == 1 ? 0 : Int((Double(index) * Double(weather.hours.count - 1) / Double(count - 1)).rounded())]
        }
        let low = entries.map(\.temperature).min() ?? 0
        let span = max(3, (entries.map(\.temperature).max() ?? 0) - low)
        return VStack(alignment: .leading, spacing: 5) {
            Text("未来 24 小时").font(.system(size: 16, weight: .medium))
            Canvas { context, size in
                guard !entries.isEmpty else { return }
                let column = size.width / CGFloat(entries.count)
                let points = entries.enumerated().map { index, hour in
                    CGPoint(x: column * (Double(index) + 0.5), y: 48 - (hour.temperature - low) / span * 25)
                }
                let curve = smoothCurve(points)
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
            }.frame(height: 64).accessibilityHidden(true)
            HStack(spacing: 0) {
                ForEach(Array(entries.enumerated()), id: \.offset) { _, hour in
                    VStack(spacing: 3) {
                        WeatherIcon(code: hour.code, day: hour.isDay, size: 26, centersArtwork: true)
                        Text(hour.time.count >= 16 ? String(hour.time.dropFirst(11).prefix(5)) : hour.time)
                            .font(.system(size: 11)).foregroundStyle(.secondary)
                        appearance.styledText(degrees(hour.temperature), size: 13)
                    }.frame(maxWidth: .infinity)
                }
            }.padding(.vertical, 3)
        }
    }

    private func daily(_ weather: Weather) -> some View {
        let minTemp = weather.days.map(\.min).min() ?? 0
        let span = max(1, (weather.days.map(\.max).max() ?? 0) - minTemp)
        let items = Array(weather.days.prefix(moreDays ? weather.days.count : 4))
        return VStack(alignment: .leading, spacing: 4) {
            HStack {
                Text("\(weather.days.count) 天天气预报").font(.system(size: 16, weight: .medium))
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
                    WeatherIcon(code: day.code, size: 24, centersArtwork: true).frame(width: 32)
                    appearance.styledText(degrees(day.min), size: 14).foregroundStyle(.secondary).frame(width: 58, alignment: .trailing)
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
                    appearance.styledText(degrees(day.max), size: 14).frame(width: 36, alignment: .leading)
                }.frame(minHeight: 28).font(.system(size: 14))
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
        HStack(spacing: 12) {
            if model.showsDetail(.humidity) {
                reading("湿度 · 相对", "\(decimal(weather.humidity))%", "drop")
            }
            if model.showsDetail(.wind) { windReading(weather) }
            if model.showsDetail(.airQuality) {
                reading("空气 · \(weather.aqiStandard ?? "AQI")",
                        weather.aqi.map { decimal($0) } ?? "—", "leaf")
                    .help([weather.pm25.map { "PM₂.₅ " + decimal($0) },
                           weather.pm10.map { "PM₁₀ " + decimal($0) }].compactMap { $0 }.joined(separator: "  "))
            }
            if model.showsDetail(.pressure) {
                reading("气压 · hPa", weather.pressureHpa.map { decimal($0) } ?? "—", "gauge.with.dots.needle.50percent")
            }
            if model.showsDetail(.ultraviolet) {
                reading("紫外线 · UVI", weather.uvIndex.map { decimal($0, digits: 1) } ?? "—", "sun.max")
            }
        }.padding(.top, 6)
    }

    @ViewBuilder
    private func windReading(_ weather: Weather) -> some View {
        let unit = model.windUnit
        let level = weather.windLevel
        let note = weather.windDirection.flatMap { angle in
            level != 0 && angle.isFinite && angle >= 0 ? direction(angle) : nil
        }
        if unit == .beaufort {
            let text = level.map { $0 > 17 ? ">17" : String($0) } ?? "—"
            reading("风级· 蒲氏", text, "wind", note: note).help("按蒲氏风级换算")
        } else {
            let wind = unit == .metersPerSecond ? weather.wind / 3.6 : weather.wind
            reading("风速 · \(unit.title)", decimal(wind, digits: 1), "wind", note: note)
        }
    }

    private func reading(_ title: String, _ value: String, _ icon: String, note: String? = nil) -> some View {
        VStack(spacing: 4) {
            HStack(spacing: 4) {
                Image(systemName: icon).foregroundStyle(accent)
                Text(title).foregroundStyle(.secondary)
            }.font(.system(size: 10)).lineLimit(1).minimumScaleFactor(0.75).frame(height: 20)
            HStack(alignment: .firstTextBaseline, spacing: 3) {
                appearance.styledText(value, size: 16)
                if let note { appearance.styledText(note, size: 10).foregroundStyle(.secondary) }
            }.lineLimit(1).minimumScaleFactor(0.74).frame(height: 28)
        }.frame(maxWidth: .infinity)
    }

    private func degrees(_ temperature: Double) -> String { model.temperature(temperature) }

    private func decimal(_ value: Double, digits: Int = 0) -> String {
        value.formatted(.number.precision(.fractionLength(digits)).grouping(.never))
    }

    private func direction(_ value: Double) -> String {
        let labels = ["北风", "东北风", "东风", "东南风", "南风", "西南风", "西风", "西北风"]
        return labels[Int((max(0, min(360, value)) / 45).rounded()) % 8]
    }
}

private let weatherAlertDetailSize = CGSize(width: 300, height: 220)

private struct WeatherAlertBadges: View {
    private enum Selection: Hashable {
        case event(String)
        case all
    }

    let groups: [Weather.AlertGroup]
    let windowChanged: (NSWindow?) -> Void
    @ViewState private var selection: Selection?
    private let visibleCount = 2

    var body: some View {
        HStack(spacing: 4) {
            ForEach(groups.prefix(visibleCount)) { group in
                Button { select(.event(group.id)) } label: {
                    let shape = RoundedRectangle(cornerRadius: 8)
                    let tone: Color = switch group.level {
                    case "red": .red
                    case "orange": .orange
                    case "blue": .blue
                    default: .yellow
                    }
                    HStack(spacing: 4) {
                        Image(systemName: "exclamationmark.triangle.fill").foregroundStyle(tone)
                        Text(group.event).lineLimit(1)
                    }
                    .font(.system(size: 10, weight: .semibold))
                    .padding(.horizontal, 6).padding(.vertical, 4)
                    .background(tone.opacity(0.10), in: shape)
                    .contentShape(shape)
                }
                .buttonStyle(.plain)
                .accessibilityLabel(group.event)
            }
            if groups.count > visibleCount {
                Button("+\(groups.count - visibleCount)") { select(.all) }
                    .buttonStyle(.plain).font(.caption2)
                    .accessibilityLabel("查看所有天气预警")
            }
        }
        .background {
            WeatherAlertPopover(isPresented: selection != nil, content: alertDetails,
                                dismiss: { selection = nil }, windowChanged: windowChanged)
                .allowsHitTesting(false)
                .accessibilityHidden(true)
        }
        .onChange(of: groups.map(\.id)) {
            if case let .event(id) = selection, !groups.contains(where: { $0.id == id }) {
                selection = nil
            }
        }
    }

    private func select(_ next: Selection) {
        selection = selection == next ? nil : next
    }

    private var alertDetails: some View {
        let notices: [Weather.Alert] = switch selection {
        case let .event(id): groups.first(where: { $0.id == id })?.notices ?? []
        case .all: groups.flatMap(\.notices)
        case nil: []
        }
        return ScrollView {
            VStack(alignment: .leading, spacing: 12) {
                ForEach(notices) { alert in
                    if alert.id != notices.first?.id { Divider() }
                    VStack(alignment: .leading, spacing: 8) {
                        Text(alert.headline).font(.headline)
                        Text(alert.description)
                        if !alert.instruction.isEmpty { Text(alert.instruction) }
                        Text(alert.source).foregroundStyle(.secondary)
                    }
                }
            }
            .font(.subheadline).padding(14)
            .frame(maxWidth: .infinity, alignment: .leading)
            .nativeOverlayScrollIndicator()
        }
        .id(selection)
        .frame(width: weatherAlertDetailSize.width, height: weatherAlertDetailSize.height)
    }
}

// The badges and the content share one native popover. Its outside-click rule
// includes the entire badge row so a click can switch the displayed warning.
private struct WeatherAlertPopover<Content: View>: NSViewRepresentable {
    let isPresented: Bool
    let content: Content
    let dismiss: () -> Void
    let windowChanged: (NSWindow?) -> Void
    @Environment(\.colorScheme) private var scheme

    func makeNSView(context: Context) -> NSView { Anchor() }
    func makeCoordinator() -> Coordinator { Coordinator() }

    func updateNSView(_ anchor: NSView, context: Context) {
        let coordinator = context.coordinator
        coordinator.onDismiss = dismiss
        coordinator.onWindowChange = windowChanged
        if isPresented, anchor.window != nil {
            coordinator.present(AnyView(content.environment(\.colorScheme, scheme)), from: anchor)
        } else {
            coordinator.close()
        }
    }

    static func dismantleNSView(_ anchor: NSView, coordinator: Coordinator) {
        coordinator.close()
    }

    private final class Anchor: NSView {
        override func hitTest(_ point: NSPoint) -> NSView? { nil }
    }

    @MainActor final class Coordinator {
        var onDismiss: () -> Void = {}
        var onWindowChange: (NSWindow?) -> Void = { _ in }
        private let popover = NSPopover()
        private var host: NSHostingController<AnyView>?
        private var monitor: Any?

        func present(_ content: AnyView, from anchor: NSView) {
            popover.appearance = anchor.effectiveAppearance
            if let host, popover.isShown {
                host.rootView = content
                return
            }
            let hosting = NSHostingController(rootView: content)
            host = hosting
            popover.contentViewController = hosting
            popover.contentSize = weatherAlertDetailSize
            popover.behavior = .applicationDefined
            popover.show(relativeTo: anchor.bounds, of: anchor, preferredEdge: .minY)
            onWindowChange(hosting.view.window)
            if monitor == nil {
                monitor = NSEvent.addLocalMonitorForEvents(matching: [.leftMouseDown, .rightMouseDown, .keyDown]) { [weak self, weak anchor] event in
                    guard let self, let anchor else { return event }
                    let insidePopover = event.window != nil && event.window === self.host?.view.window
                    let insideParent = event.window != nil && event.window === anchor.window
                    if event.type == .keyDown {
                        if event.keyCode == 53, insidePopover || insideParent {
                            self.onDismiss()
                            return nil
                        }
                    } else if !insidePopover && !(insideParent && anchor.bounds.contains(anchor.convert(event.locationInWindow, from: nil))) {
                        self.onDismiss()
                    }
                    return event
                }
            }
        }

        func close() {
            if let monitor {
                NSEvent.removeMonitor(monitor)
                self.monitor = nil
            }
            guard host != nil else { return }
            host = nil
            onWindowChange(nil)
            popover.close()
            popover.contentViewController = nil
        }
    }
}

struct WeatherCitySearch: View {
    let model: WeatherModel
    var canSelect: (City) -> Bool = { _ in true }
    let select: (City) -> Void
    @ViewState private var term = ""
    @ViewState private var results: [City] = []
    @ViewState private var searchStatus: String?
    @ViewState private var searchTask: Task<Void, Never>?

    var body: some View {
        VStack(alignment: .leading, spacing: 8) {
            HStack {
                TextField("搜索城市，例如：天津 / Tianjin", text: $term).onSubmit(runSearch)
                Button("搜索", action: runSearch).disabled(term.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty)
            }
            ForEach(results) { city in
                let selectable = canSelect(city)
                Button {
                    select(city)
                    results = []
                    term = ""
                } label: {
                    HStack {
                        Text(city.name).fontWeight(.medium)
                        if let region = city.region { Text(region).foregroundStyle(.secondary).lineLimit(1) }
                        Spacer()
                        Image(systemName: selectable ? "arrow.up.left" : "checkmark").foregroundStyle(.secondary)
                    }.padding(.vertical, 5).contentShape(Rectangle())
                }
                .buttonStyle(.plain)
                .disabled(!selectable)
            }
            if let searchStatus { Text(searchStatus).font(.caption).foregroundStyle(.secondary) }
        }.onDisappear { searchTask?.cancel() }
    }

    private func runSearch() {
        let query = term.trimmingCharacters(in: .whitespacesAndNewlines)
        guard !query.isEmpty else { return }
        if model.isPreview {
            results = ([model.city].compactMap { $0 } + model.favorites).uniqued()
                .filter { $0.name.localizedCaseInsensitiveContains(query) }
            searchStatus = results.isEmpty ? "没有匹配的样例城市。" : nil
            return
        }
        searchTask?.cancel()
        searchStatus = "正在搜索…"
        results = []
        searchTask = Task {
            do {
                let matches = try await WeatherAPI.search(query)
                try Task.checkCancellation()
                results = matches
                searchStatus = matches.isEmpty ? "没有找到这个城市。" : nil
            } catch {
                if !Task.isCancelled { searchStatus = "城市搜索暂不可用，请重试。" }
            }
        }
    }
}
