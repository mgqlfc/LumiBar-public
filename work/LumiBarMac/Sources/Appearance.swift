import SwiftUI

extension Color {
    static func rgb(_ value: UInt32, saturation: Double = 1) -> Color {
        let red = Double((value >> 16) & 255) / 255
        let green = Double((value >> 8) & 255) / 255
        let blue = Double(value & 255) / 255
        guard saturation != 1 else { return Color(red: red, green: green, blue: blue) }
        let gray = red * 0.2126 + green * 0.7152 + blue * 0.0722
        return Color(red: gray + (red - gray) * saturation,
                     green: gray + (green - gray) * saturation,
                     blue: gray + (blue - gray) * saturation)
    }
}

@MainActor
@Observable
final class AppearanceModel {
    private let defaults: UserDefaults
    var onThemeChange: (() -> Void)?
    var onEntryChange: (() -> Void)?
    var westernFont: String {
        didSet { defaults.set(westernFont, forKey: "appearance.westernFont"); onEntryChange?() }
    }
    var chineseFont: String {
        didSet { defaults.set(chineseFont, forKey: "appearance.chineseFont"); onEntryChange?() }
    }
    var lyricsSize: Double {
        didSet { defaults.set(lyricsSize, forKey: "appearance.lyricsSize") }
    }
    var theme: String {
        didSet {
            defaults.set(theme, forKey: "appearance.theme")
            onThemeChange?()
        }
    }
    var glassDensity: Double {
        didSet { defaults.set(glassDensity, forKey: "appearance.glassDensity") }
    }
    var coverTint: Bool {
        didSet { defaults.set(coverTint, forKey: "appearance.coverTint"); onEntryChange?() }
    }
    var animation: Bool {
        didSet { defaults.set(animation, forKey: "appearance.animation"); onEntryChange?() }
    }
    var coverBlend: Double {
        didSet { defaults.set(coverBlend, forKey: "appearance.coverBlend"); onEntryChange?() }
    }
    var coverSaturation: Double {
        didSet { defaults.set(coverSaturation, forKey: "appearance.coverSaturation"); onEntryChange?() }
    }
    var windowAppearance: NSAppearance? {
        switch theme {
        case "Dark": NSAppearance(named: .darkAqua)
        case "Light": NSAppearance(named: .aqua)
        default: nil
        }
    }

    init(defaults: UserDefaults) {
        self.defaults = defaults
        westernFont = defaults.string(forKey: "appearance.westernFont") ?? ""
        chineseFont = defaults.string(forKey: "appearance.chineseFont") ?? ""
        let size = defaults.object(forKey: "appearance.lyricsSize") as? Double ?? 22
        lyricsSize = size.isFinite ? min(36, max(18, size)) : 22
        theme = defaults.string(forKey: "appearance.theme") ?? "System"
        let density = defaults.object(forKey: "appearance.glassDensity") as? Double ?? 0.50
        glassDensity = density.isFinite ? min(0.95, max(0.35, density)) : 0.50
        coverTint = defaults.object(forKey: "appearance.coverTint") as? Bool ?? true
        animation = defaults.object(forKey: "appearance.animation") as? Bool ?? true
        let blend = defaults.object(forKey: "appearance.coverBlend") as? Double ?? 0.42
        coverBlend = blend.isFinite ? min(0.55, max(0.08, blend)) : 0.42
        let saturation = defaults.object(forKey: "appearance.coverSaturation") as? Double ?? 0.69
        coverSaturation = saturation.isFinite ? min(1, max(0, saturation)) : 0.69
    }

    func tint(_ scheme: ColorScheme) -> Color {
        let density = min(0.95, max(0.35, glassDensity))
        if scheme == .dark {
            return Color.rgb(0x141927).opacity(0.40 + (density - 0.35) * 0.62)
        }
        return Color.white.opacity(0.34 + (density - 0.35) * 0.37)
    }

    func styledText(_ value: String, size: CGFloat, weight: Font.Weight = .regular) -> Text {
        let pieces = Self.scriptRuns(value)
        var content = AttributedString()
        for (part, chinese) in pieces {
            var run = AttributedString(part)
            if let family = fontFamily(forChinese: chinese) {
                run.font = .custom(family, size: size).weight(weight)
            } else {
                run.font = .system(size: size, weight: weight)
            }
            content += run
        }
        return Text(content)
    }

    func textWidth(_ value: String, size: CGFloat, weight: Font.Weight = .regular) -> CGFloat {
        let (systemWeight, familyWeight): (NSFont.Weight, Int) = switch weight {
        case .medium: (.medium, 6)
        case .semibold: (.semibold, 8)
        default: (.regular, 5)
        }
        let fallback = NSFont.systemFont(ofSize: size, weight: systemWeight)
        return Self.scriptRuns(value).reduce(0) { total, run in
            let font = fontFamily(forChinese: run.1).flatMap {
                NSFontManager.shared.font(withFamily: $0, traits: [], weight: familyWeight, size: size)
            } ?? fallback
            return total + (run.0 as NSString).size(withAttributes: [.font: font]).width
        }
    }

    func fontFamily(forChinese chinese: Bool) -> String? {
        if chinese { return chineseFont.isEmpty ? nil : chineseFont }
        return westernFont.isEmpty ? "Geist" : westernFont
    }

    nonisolated static func scriptRuns(_ value: String) -> [(String, Bool)] {
        var result: [(String, Bool)] = []
        for character in value {
            let han = character.unicodeScalars.contains { scalar in
                (0x2E80...0x9FFF).contains(scalar.value) || (0xF900...0xFAFF).contains(scalar.value)
                    || (0x20000...0x323AF).contains(scalar.value)
            }
            if let last = result.indices.last, result[last].1 == han {
                result[last].0.append(character)
            } else {
                result.append((String(character), han))
            }
        }
        return result
    }
}

struct SettingsView: View {
    private enum Page: String, CaseIterable {
        case general = "通用"
        case menuBar = "菜单栏"
        case lyrics = "歌词"
        case about = "关于"

        var symbol: String {
            switch self {
            case .general: "gearshape"
            case .menuBar: "menubar.rectangle"
            case .lyrics: "captions.bubble"
            case .about: "info.circle"
            }
        }
    }

    private enum MenuBarPage: String, CaseIterable {
        case weather = "天气胶囊"
        case music = "音乐胶囊"
    }

    private enum LyricsPage: String, CaseIterable {
        case display = "Lumi 歌词"
        case sources = "播放与歌词来源"
    }

    @Bindable var appearance: AppearanceModel
    @Bindable var weather: WeatherModel
    @Bindable var music: MusicModel
    var openLyricLibrary: () -> Void = {}
    @ViewState private var page: Page = .general
    @ViewState private var menuBarPage: MenuBarPage = .weather
    @ViewState private var lyricsSubpage: LyricsPage = .display
    @ViewState private var addingFavoriteCity = false
    @Environment(\.accessibilityReduceMotion) private var reduceMotion

    nonisolated static let windowSize = CGSize(width: 800, height: 650)
    private static let sidebarWidth: CGFloat = 188

    var body: some View {
        HStack(spacing: 0) {
            sidebar
            Divider()
            VStack(spacing: 0) {
                pageHeader
                Divider().opacity(0.65)
                ScrollView {
                    content
                        .padding(.horizontal, 26).padding(.vertical, 21)
                        .frame(maxWidth: .infinity, alignment: .leading)
                        .nativeOverlayScrollIndicator()
                }
                .scrollBounceBehavior(.basedOnSize)
                .id([page.rawValue, menuBarPage.rawValue, lyricsSubpage.rawValue])
            }
            .frame(maxWidth: .infinity, maxHeight: .infinity)
        }
        .frame(maxWidth: .infinity, maxHeight: .infinity)
    }

    private var sidebar: some View {
        VStack(alignment: .leading, spacing: 5) {
            ForEach(Page.allCases, id: \.self) { option in
                let selected = page == option
                Button { page = option } label: {
                    HStack(spacing: 10) {
                        Image(systemName: option.symbol)
                            .font(.system(size: 15, weight: .medium))
                            .foregroundStyle(selected ? Color.accentColor : .secondary)
                            .frame(width: 19)
                            .accessibilityHidden(true)
                        Text(option.rawValue)
                            .font(.body.weight(selected ? .semibold : .medium))
                            .lineLimit(1)
                    }
                    .padding(.horizontal, 10)
                    .frame(maxWidth: .infinity, minHeight: 36, alignment: .leading)
                    .contentShape(RoundedRectangle(cornerRadius: 10))
                    .glassEffect(selected ? .regular.tint(Color.accentColor.opacity(0.34)).interactive() : .identity,
                                 in: RoundedRectangle(cornerRadius: 10))
                }
                .buttonStyle(.plain)
                .accessibilityAddTraits(selected ? .isSelected : [])
            }
            Spacer(minLength: 0)
        }
        .padding(.horizontal, 13).padding(.top, 17)
        .frame(width: Self.sidebarWidth)
        .accessibilityElement(children: .contain).accessibilityLabel("设置分类")
    }

    private var pageHeader: some View {
        HStack(spacing: 10) {
            Text(page.rawValue)
                .font(.title2.weight(.semibold))
                .accessibilityAddTraits(.isHeader)
            Spacer(minLength: 0)
            switch page {
            case .menuBar:
                LibraryGlassSegments(selection: $menuBarPage, choices: MenuBarPage.allCases) { $0.rawValue }
                    .accessibilityElement(children: .contain).accessibilityLabel("菜单栏组件")
            case .lyrics:
                LibraryGlassSegments(selection: $lyricsSubpage, choices: LyricsPage.allCases) { $0.rawValue }
                    .accessibilityElement(children: .contain).accessibilityLabel("歌词设置分类")
                Button("歌词管理", systemImage: "music.note.list", action: openLyricLibrary)
                    .buttonStyle(.glass)
                    .accessibilityLabel("打开歌词管理…")
            case .general, .about:
                EmptyView()
            }
        }
        .controlSize(.small)
        .padding(.horizontal, 26).frame(height: 51)
    }

    @ViewBuilder private var content: some View {
        switch page {
        case .general: generalPage
        case .menuBar:
            switch menuBarPage {
            case .weather: weatherPage
            case .music: musicCapsulePage
            }
        case .lyrics:
            switch lyricsSubpage {
            case .display: lyricsPage
            case .sources: musicPage
            }
        case .about: aboutPage
        }
    }

    private var generalPage: some View {
        VStack(alignment: .leading, spacing: 23) {
            settingsSection("外观") {
                settingsSegments("主题", selection: $appearance.theme, choices: ["System", "Light", "Dark"]) {
                    switch $0 {
                    case "Light": "浅色"
                    case "Dark": "深色"
                    default: "跟随系统"
                    }
                }
                percentControl("玻璃密度", value: $appearance.glassDensity, range: 0.35...0.95)
                toggleRow("动态光效", "让胶囊和面板的光效流动", binding: $appearance.animation)
            }
            Divider()
            settingsSection("界面字体") {
                HStack(alignment: .top, spacing: 21) {
                    FontChoice(title: "中文字体", defaultName: "系统字体", family: $appearance.chineseFont)
                        .frame(maxWidth: .infinity, alignment: .leading)
                    FontChoice(title: "西文字体", defaultName: "Geist", family: $appearance.westernFont)
                        .frame(maxWidth: .infinity, alignment: .leading)
                }
            }
        }
    }

    private func settingsSection<Content: View>(_ title: String, @ViewBuilder content: () -> Content) -> some View {
        VStack(alignment: .leading, spacing: 11) {
            Text(title).font(.headline).accessibilityAddTraits(.isHeader)
            content()
        }
        .frame(maxWidth: .infinity, alignment: .leading)
    }

    private func settingsSegments<Value: Hashable>(_ label: String, selection: Binding<Value>, choices: [Value],
                                                    title: @escaping (Value) -> String) -> some View {
        VStack(alignment: .leading, spacing: 6) {
            Text(label).font(.subheadline.weight(.medium)).accessibilityHidden(true)
            LibraryGlassSegments(selection: selection, choices: choices, stretches: true, title: title)
                .accessibilityElement(children: .contain).accessibilityLabel(label)
        }
    }

    private func capsulePreview<Content: View>(moving: Bool,
                                               @ViewBuilder content: @escaping (TimeInterval?) -> Content) -> some View {
        let animates = moving && appearance.animation && !reduceMotion
        return TimelineView(.animation(minimumInterval: 1 / 18, paused: !animates)) { context in
            content(animates ? context.date.timeIntervalSinceReferenceDate : nil)
        }
        .frame(maxWidth: .infinity, minHeight: 42, alignment: .leading)
    }

    private func percentControl(_ label: String, value: Binding<Double>, range: ClosedRange<Double>) -> some View {
        VStack(alignment: .leading, spacing: 9) {
            Text(label).font(.headline)
            HStack(spacing: 12) {
                Slider(value: value, in: range, step: 0.01).accessibilityLabel(label)
                Text(value.wrappedValue.formatted(.percent.precision(.fractionLength(0))))
                    .font(.subheadline.monospacedDigit()).frame(width: 42, alignment: .trailing)
            }
        }
    }

    private func heading(_ title: String, _ detail: String) -> some View {
        VStack(alignment: .leading, spacing: 9) {
            Text(title).font(lumiFont(28, weight: .semibold))
            Text(detail).font(.subheadline).foregroundStyle(.secondary)
        }
    }

    private func toggleRow(_ title: String, _ subtitle: String, binding: Binding<Bool>) -> some View {
        HStack(spacing: 12) {
            VStack(alignment: .leading, spacing: 6) {
                Text(title).font(.headline)
                Text(subtitle).font(.caption).foregroundStyle(.secondary)
            }
            Spacer(minLength: 0)
            Toggle(title, isOn: binding).labelsHidden().toggleStyle(.switch)
        }.padding(.horizontal, 1).padding(.vertical, 3)
    }

    private var weatherPage: some View {
        VStack(alignment: .leading, spacing: 20) {
            settingsSection("预览与显示") {
                capsulePreview(moving: weather.capsuleGlow && weather.tintScale > 0) { time in
                    let lite = weather.liteCapsule
                    let width = lite ? MusicView.entryWidth : 2 * MusicView.entryWidth + MenuBarCapsuleShape.entryGap
                    WeatherCapsule(model: weather, appearance: appearance, height: MenuBarCapsuleShape.entryHeight,
                                   minimumWidth: width, showsPrecipitation: !lite, lightTime: time, showsSample: true)
                }
                .accessibilityLabel("天气胶囊预览")
                Toggle("显示天气胶囊", isOn: $weather.showWeather)
                Toggle("始终使用 Lite 天气胶囊", isOn: $weather.liteCapsule)
            }
            Divider()
            settingsSection("流光与着色") {
                Toggle("天气胶囊流光", isOn: $weather.capsuleGlow)
                Toggle("天气着色", isOn: $weather.capsuleTint)
                HStack(alignment: .top, spacing: 21) {
                    percentControl("着色强度", value: $weather.capsuleTintStrength, range: 0...1)
                    percentControl("着色饱和度", value: $weather.capsuleTintSaturation, range: 0...1)
                }.disabled(!weather.capsuleTint)
            }
            Divider()
            favoriteCities
            Divider()
            settingsSection("显示单位") {
                Toggle("华氏温度", isOn: $weather.fahrenheit)
                settingsSegments("风速显示", selection: $weather.windUnit, choices: WeatherWindUnit.allCases) { $0.title }
            }
            Divider()
            settingsSection("天气详情显示") {
                LazyVGrid(columns: Array(repeating: GridItem(.flexible(), alignment: .leading), count: 2), spacing: 10) {
                    ForEach(WeatherDetailItem.allCases, id: \.self) { item in
                        Toggle(item.title, isOn: Binding(get: { weather.showsDetail(item) },
                                                         set: { weather.setDetail(item, visible: $0) }))
                    }
                }
            }
            Divider()
            settingsSection("数据来源") {
                Toggle("优先使用 LumiBar 天气代理", isOn: $weather.proxyEnabled)
                if weather.proxyEnabled {
                    TextField("HTTPS 服务地址", text: $weather.proxy).textFieldStyle(.roundedBorder)
                    if WeatherAPI.proxyURL(weather.proxy) == nil { Text("请输入有效的 HTTPS 地址。").foregroundStyle(.red) }
                }
                Button("应用并刷新天气") { weather.refresh() }
                    .buttonStyle(.glass)
                    .disabled(weather.city == nil || (weather.proxyEnabled && WeatherAPI.proxyURL(weather.proxy) == nil))
            }
        }
    }

    private var favoriteCities: some View {
        VStack(alignment: .leading, spacing: 10) {
            HStack {
                Text("收藏城市").font(.headline).accessibilityAddTraits(.isHeader)
                Spacer()
                Button("添加城市…") { addingFavoriteCity = true }
                    .buttonStyle(.glass)
                    .disabled(weather.favorites.count >= WeatherModel.favoriteLimit)
                    .popover(isPresented: $addingFavoriteCity) {
                        ScrollView {
                            WeatherCitySearch(model: weather, canSelect: { !weather.isFavorite($0) }) { city in
                                weather.addFavorite(city)
                                addingFavoriteCity = false
                            }.padding(14)
                        }.frame(width: 360).frame(maxHeight: 320)
                    }
            }
            if weather.favorites.isEmpty {
                Text("还没有收藏城市。").font(.subheadline).foregroundStyle(.secondary)
            } else {
                VStack(spacing: 0) {
                    ForEach(Array(weather.favorites.enumerated()), id: \.element.id) { index, city in
                        if index > 0 { Divider().padding(.horizontal, 10) }
                        favoriteCityRow(city, index: index)
                    }
                }
            }
            HStack(spacing: 10) {
                Button("定位当前区县") { weather.locate() }
                if let city = weather.city, !weather.isFavorite(city) {
                    Button("收藏当前城市") { weather.addFavorite(city) }
                        .disabled(weather.favorites.count >= WeatherModel.favoriteLimit)
                }
            }
            if weather.favorites.count >= WeatherModel.favoriteLimit {
                Text("最多收藏 \(WeatherModel.favoriteLimit) 个城市。").font(.caption).foregroundStyle(.secondary)
            }
            if let error = weather.locationError { Text(error).font(.caption).foregroundStyle(.secondary) }
        }
    }

    private func favoriteCityRow(_ city: City, index: Int) -> some View {
        let selected = !weather.usingLocation && weather.city?.id == city.id
        return HStack(spacing: 4) {
            Button { weather.select(city) } label: {
                HStack(spacing: 7) {
                    Text(city.name).fontWeight(selected ? .semibold : .regular).lineLimit(1)
                    if let region = city.region {
                        Text(region).font(.caption).foregroundStyle(.secondary).lineLimit(1)
                    }
                    if selected { Image(systemName: "checkmark").font(.caption).foregroundStyle(Color.accentColor) }
                }.frame(maxWidth: .infinity, alignment: .leading).contentShape(Rectangle())
            }
            .buttonStyle(.plain)
            .accessibilityAddTraits(selected ? .isSelected : [])
            .help("查看\(city.name)的天气")
            favoriteCityButton("chevron.up", label: "上移\(city.name)") { weather.moveFavorite(city, by: -1) }
                .disabled(index == 0)
            favoriteCityButton("chevron.down", label: "下移\(city.name)") { weather.moveFavorite(city, by: 1) }
                .disabled(index == weather.favorites.count - 1)
            favoriteCityButton("trash", label: "移除\(city.name)收藏") { weather.removeFavorite(city) }
        }.padding(.leading, 12).padding(.trailing, 6).frame(height: 34)
    }

    private func favoriteCityButton(_ symbol: String, label: String, action: @escaping () -> Void) -> some View {
        Button(action: action) {
            Image(systemName: symbol).font(.system(size: 11, weight: .medium)).frame(width: 16, height: 15)
                .contentShape(Rectangle())
        }.buttonStyle(.glass).controlSize(.small).accessibilityLabel(label).help(label)
    }

    private var musicCapsulePage: some View {
        VStack(alignment: .leading, spacing: 20) {
            settingsSection("预览与显示") {
                capsulePreview(moving: music.capsuleGlow && appearance.coverTint) { time in
                    let bands = MusicView.entryBands(from: [0.22, 0.62, 0.38, 0.84, 0.54],
                                                    response: music.capsuleSpectrumResponse)
                    MusicView(model: music, appearance: appearance,
                              surface: .entry(height: MenuBarCapsuleShape.entryHeight, bands: bands,
                                              playback: nil, lightTime: time), toggle: {})
                }
                .accessibilityLabel("音乐胶囊预览")
                Toggle("显示音乐胶囊", isOn: $music.showMusic)
            }
            Divider()
            settingsSection("流光与着色") {
                Toggle("音乐胶囊流光", isOn: $music.capsuleGlow)
                Toggle("封面着色", isOn: $appearance.coverTint)
                    .help("用于音乐胶囊、曲目信息浮窗和展开的 Lumi 歌词。")
                HStack(alignment: .top, spacing: 21) {
                    percentControl("着色强度", value: $appearance.coverBlend, range: 0.08...0.55)
                    percentControl("着色饱和度", value: $appearance.coverSaturation, range: 0...1)
                }.disabled(!appearance.coverTint)
            }
            Divider()
            settingsSection("音乐频谱") {
                Toggle("实时频谱（系统音频权限）", isOn: $music.spectrumEnabled)
                lyricSettingSlider("频谱响应", value: $music.capsuleSpectrumResponse,
                                   range: MusicModel.capsuleSpectrumResponseRange, step: 0.1, unit: "×")
                Text("调高后，较轻的声音也能带动频谱柱条。")
                    .font(.caption).foregroundStyle(.secondary)
                if let error = music.audioError { Text(error).font(.caption).foregroundStyle(.secondary) }
            }
        }
    }

    private var musicPage: some View {
        VStack(alignment: .leading, spacing: 16) {
            Text("播放器连接").font(.title3.weight(.semibold))
            Button {
                music.connectNetease()
            } label: {
                Text(SystemMedia.executable == nil ? "连接网易云音乐…" : "检查系统播放连接")
            }.buttonStyle(.glassProminent)
            if let status = music.neteaseStatus { Text(status).font(.caption).foregroundStyle(.secondary) }
            Text(SystemMedia.executable == nil ? "Apple Music 和 Spotify 使用 macOS 自动化权限。"
                 : "支持通过 macOS 正在播放识别和控制播放器。")
                .font(.caption).foregroundStyle(.secondary)
            if let error = music.automationError {
                Text(error).font(.caption).foregroundStyle(.secondary)
                Button("重新检查自动化权限") { music.retryAutomation() }
            }
            Divider()
            VStack(alignment: .leading, spacing: 8) {
                lyricSettingSlider("播放时间校准", value: $music.playbackCalibration,
                                   range: MusicModel.playbackCalibrationRange, step: 0.1, unit: "秒", signed: true)
                Text("正值让进度和歌词提前显示。每首歌的歌词时移仍可单独设置。")
                    .font(.caption).foregroundStyle(.secondary)
                Button("重置播放时间校准") { music.playbackCalibration = 0 }
                    .buttonStyle(.glass)
                    .disabled(music.playbackCalibration == 0)
            }
            Divider()
            lyricSources
        }
    }

    private var lyricSources: some View {
        VStack(alignment: .leading, spacing: 12) {
            Text("歌词来源").font(.title3.weight(.semibold))
            Toggle("在线查找同步歌词", isOn: $music.onlineLyrics)
            Text("自动选择会比较各来源的匹配度与时间轴。分数相同时，排在上方的来源优先。")
                .font(.caption).foregroundStyle(.secondary)
            VStack(spacing: 0) {
                ForEach(Array(music.lyricSourceOrder.enumerated()), id: \.element) { index, source in
                    if index > 0 { Divider().padding(.horizontal, 10) }
                    HStack(spacing: 6) {
                        if source == .appleMusic {
                            AppleMusicSourceSettings(music: music)
                        } else {
                            LyricSourceLabel(source: source)
                            Spacer()
                        }
                        sourceOrderButton(source, up: true, enabled: index > 0)
                        sourceOrderButton(source, up: false, enabled: index < music.lyricSourceOrder.count - 1)
                    }.padding(.leading, 12).padding(.trailing, 6).frame(minHeight: source == .appleMusic ? 44 : 32)
                }
            }
            Button("恢复默认顺序") { music.resetLyricSourceOrder() }
                .buttonStyle(.glass)
                .disabled(music.lyricSourceOrder == MusicModel.LyricSource.defaultOrder)
        }
    }

    private func sourceOrderButton(_ source: MusicModel.LyricSource, up: Bool, enabled: Bool) -> some View {
        Button {
            music.moveLyricSource(source, by: up ? -1 : 1)
        } label: {
            Image(systemName: up ? "chevron.up" : "chevron.down")
                .font(.system(size: 10, weight: .semibold))
                .frame(width: 16, height: 15).contentShape(Rectangle())
        }
        .buttonStyle(.glass).controlSize(.small)
        .accessibilityLabel(source.title + (up ? "上移" : "下移"))
        .disabled(!enabled)
    }

    private var lyricsPage: some View {
        let cameraOffsetRange = music.minimumCompactCameraOffset...NotchLyricsOptions.compactCameraOffsetRange.upperBound
        let cameraOffset = Binding {
            max(cameraOffsetRange.lowerBound, music.notchLyrics.compactCameraOffset)
        } set: { offset in
            music.notchLyrics.compactCameraOffset = offset
        }
        return VStack(alignment: .leading, spacing: 16) {
            settingsSection("显示与隐藏") {
                Toggle("在摄像头下显示 Lumi 歌词", isOn: $music.notchLyrics.enabled)
                Toggle(NotchLyricsOptions.hideWhenPausedLabel, isOn: $music.notchLyrics.hidesWhenPaused)
                Toggle(NotchLyricsOptions.hideInFullScreenLabel, isOn: $music.notchLyrics.hidesInFullScreen)
                Toggle("鼠标指向时渐淡", isOn: $music.notchLyrics.fadesOnHover)
            }
            Divider()
            Text("面板尺寸").font(.title3.weight(.semibold))
            HStack(spacing: 20) {
                lyricSettingSlider("收起宽度", value: $music.notchLyrics.width,
                                   range: NotchLyricsOptions.compactWidthRange, step: 1)
                lyricSettingSlider("收起基础高度", value: $music.notchLyrics.compactHeight,
                                   range: NotchLyricsOptions.compactHeightRange, step: 1)
                    .help("摄像头与歌词区域的距离保持默认时，面板的最小高度。")
            }
            HStack(spacing: 20) {
                lyricSettingSlider("展开宽度", value: $music.notchLyrics.expandedWidth,
                                   range: NotchLyricsOptions.expandedWidthRange, step: 1)
                lyricSettingSlider("展开高度", value: $music.notchLyrics.expandedHeight,
                                   range: NotchLyricsOptions.expandedHeightRange, step: 1)
            }
            Text("面板会为摄像头和两行歌词留足空间。展开宽度至少与收起相同，分段会随实际宽度重算。")
                .font(.caption).foregroundStyle(.secondary)
            Toggle("顶部圆角（收起与展开）", isOn: $music.notchLyrics.roundsTopCorners)
            Divider()
            VStack(alignment: .leading, spacing: 13) {
                Text("字体与间距").font(.title3.weight(.semibold))
                HStack(alignment: .top, spacing: 20) {
                    FontChoice(title: "歌词中文字体", defaultName: "跟随界面外观", family: $music.notchLyrics.chineseFontFamily)
                        .frame(maxWidth: .infinity, alignment: .leading)
                    FontChoice(title: "歌词西文字体", defaultName: "跟随界面外观", family: $music.notchLyrics.westernFontFamily)
                        .frame(maxWidth: .infinity, alignment: .leading)
                }
                Picker("歌词与译文字重", selection: $music.notchLyrics.fontWeight) {
                    Text("常规").tag(400)
                    Text("中等").tag(500)
                    Text("半粗").tag(600)
                    Text("粗体").tag(700)
                    Text("特粗").tag(800)
                }
                HStack(spacing: 20) {
                    lyricSettingSlider("收起字号", value: $music.notchLyrics.fontSize, range: 10...20, step: 0.5)
                    lyricSettingSlider("展开字号", value: $appearance.lyricsSize, range: 18...36, step: 1)
                }
                lyricSettingSlider("收起首行放大", value: $music.notchLyrics.firstLineExtraSize,
                                   range: NotchLyricsOptions.firstLineExtraSizeRange, step: 0.5)
                VStack(alignment: .leading, spacing: 5) {
                    lyricSettingSlider("摄像头与歌词区域的距离", value: cameraOffset,
                                       range: cameraOffsetRange, step: 1, signed: true)
                    Text("以默认距离为 0 pt。调小距离时，收起面板的底部也会随之上移。")
                        .font(.caption).foregroundStyle(.secondary)
                }
                HStack(spacing: 20) {
                    lyricSettingSlider("收起上下留白", value: $music.notchLyrics.verticalPadding, range: 1...12, step: 1)
                    lyricSettingSlider("收起行间距", value: $music.notchLyrics.lineSpacing, range: 0...10, step: 1)
                }
            }
            Divider()
            VStack(alignment: .leading, spacing: 13) {
                Text("颜色与光效").font(.title3.weight(.semibold))
                settingsSegments("主题", selection: $music.notchLyrics.theme, choices: ["System", "Light", "Dark"]) {
                    switch $0 {
                    case "Light": "日间"
                    case "Dark": "夜间"
                    default: "跟随系统"
                    }
                }
                percentControl("文字柔影", value: $music.notchLyrics.shadow, range: 0...1)
                Toggle("浅色模式减弱文字阴影", isOn: $music.notchLyrics.reducesLightTextShadow)
                Toggle("歌词颜色跟随封面", isOn: $music.notchLyrics.useCoverColors)
                if !music.notchLyrics.useCoverColors {
                    ColorPicker("歌词颜色", selection: hexColor($music.notchLyrics.lyricColor, fallback: 0xD7EEE6), supportsOpacity: false)
                }
                percentControl("译文与下一句不透明度", value: $music.notchLyrics.translationOpacity,
                               range: NotchLyricsOptions.translationOpacityRange)
                Toggle("系统液态玻璃", isOn: $music.notchLyrics.usesSystemGlassBorder)
                if !music.notchLyrics.usesSystemGlassBorder {
                    Toggle("面板描边跟随封面取色", isOn: $music.notchLyrics.tintsBorderFromCover)
                    percentControl("面板描边强度", value: $music.notchLyrics.borderIntensity, range: 0...1)
                }
                Toggle("Lumi 歌词流光", isOn: $music.notchLyrics.glow)
                HStack(spacing: 20) {
                    lyricSettingSlider("收起流光速度", value: $music.notchLyrics.compactGlowSpeed,
                                       range: NotchLyricsOptions.glowSpeedRange, step: 0.1, unit: "×")
                    lyricSettingSlider("展开流光速度", value: $music.notchLyrics.expandedGlowSpeed,
                                       range: NotchLyricsOptions.glowSpeedRange, step: 0.1, unit: "×")
                }.disabled(!music.notchLyrics.glow)
                Toggle("HDR 歌词高亮", isOn: $music.notchLyrics.usesHDRLyricHighlights)
                    .toggleStyle(.checkbox)
                    .help("支持 HDR 的屏幕可在夜间模式提亮逐字高亮。")
            }
            Divider()
            VStack(alignment: .leading, spacing: 11) {
                Text("歌词内容与片头").font(.title3.weight(.semibold))
                Toggle("显示译文", isOn: $music.notchLyrics.showTranslation)
                Toggle("长歌词自动分段", isOn: $music.notchLyrics.splitLongLines)
                Toggle("逐字高亮（卡拉 OK）", isOn: $music.notchLyrics.karaoke)
                Toggle("无倒计时的前奏显示歌名和歌手", isOn: $music.notchLyrics.showIntroMetadata)
                Toggle("前奏与间奏三点倒计时", isOn: $music.notchLyrics.showCountdown)
            }
        }
    }

    private var aboutPage: some View {
        let version = Bundle.main.object(forInfoDictionaryKey: "CFBundleShortVersionString") as? String ?? ""
        let build = Bundle.main.object(forInfoDictionaryKey: "CFBundleVersion") as? String ?? ""
        return VStack(alignment: .leading, spacing: 18) {
            HStack(spacing: 14) {
                Image(nsImage: NSApplication.shared.applicationIconImage)
                    .resizable().scaledToFit().frame(width: 56, height: 56)
                    .accessibilityHidden(true)
                heading("LumiBar", "macOS 版 · \(version) (\(build))")
            }
            Text("天气、音乐与歌词，留在菜单栏。").foregroundStyle(.secondary)
            Divider()
            aboutItem("制作人", "Ma Guanqun with Codex")
            aboutItem("前端", "SwiftUI · AppKit · Liquid Glass")
            aboutItem("后端", "Swift · 系统媒体事件 · 本地偏好设置")
            aboutItem("天气与空气", "和风天气代理 · Open-Meteo · MET Norway\n空气质量按数据源标注 · macOS 定位服务")
            aboutItem("音乐与歌词", "macOS 正在播放 · 系统音频频谱\n在线逐字与同步歌词 · 本地 LRC 和 TTML")
            aboutItem("默认字体", "中文为 macOS 系统字体，西文为内置 Geist。")
            VStack(alignment: .leading, spacing: 5) {
                Text("特别鸣谢").font(.headline)
                Link("LyriMuse", destination: URL(string: "https://github.com/Yudaotor/lyrimuse")!)
                    .foregroundStyle(Color.accentColor)
                Text("歌词匹配、歌词源和管理流程参考。").font(.subheadline).foregroundStyle(.secondary)
            }
            Link("在 GitHub 上查看 LumiBar", destination: URL(string: "https://github.com/mgqlfc/LumiBar-public")!)
                .foregroundStyle(Color.accentColor)
        }
    }

    private func aboutItem(_ title: String, _ detail: String) -> some View {
        VStack(alignment: .leading, spacing: 5) {
            Text(title).font(.headline)
            Text(detail).font(.subheadline).foregroundStyle(.secondary)
        }
    }

    private func lyricSettingSlider(_ label: String, value: Binding<Double>, range: ClosedRange<Double>,
                                    step: Double, unit: String = "pt", signed: Bool = false) -> some View {
        VStack(alignment: .leading, spacing: 5) {
            HStack {
                Text(label).font(.subheadline.weight(.medium))
                Spacer(minLength: 8)
                Text(value.wrappedValue.formatted(.number.precision(.fractionLength(0...1))
                    .sign(strategy: signed ? .always(includingZero: false) : .automatic)) + " " + unit)
                    .font(.caption.monospacedDigit()).foregroundStyle(.secondary)
            }
            Slider(value: value, in: range, step: step).accessibilityLabel(label)
        }.frame(maxWidth: .infinity, alignment: .leading)
    }

    private func hexColor(_ value: Binding<String>, fallback: UInt32) -> Binding<Color> {
        Binding {
            let digits = value.wrappedValue.trimmingCharacters(in: CharacterSet(charactersIn: "#"))
            return Color.rgb(UInt32(digits, radix: 16) ?? fallback)
        } set: { color in
            guard let rgb = NSColor(color).usingColorSpace(.sRGB) else { return }
            let channels = [rgb.redComponent, rgb.greenComponent, rgb.blueComponent]
                .map { Int((min(1, max(0, $0)) * 255).rounded()) }
            value.wrappedValue = String(format: "#%02X%02X%02X", channels[0], channels[1], channels[2])
        }
    }
}


struct LyricSourceLabel: View {
    let source: MusicModel.LyricSource

    var body: some View {
        HStack(spacing: 6) {
            Text(source.title)
            if source.supportsWordTiming {
                Text("支持逐字").font(.caption).foregroundStyle(.secondary)
            }
        }
    }
}

private struct FontChoice: View {
    let title: String
    let defaultName: String
    @Binding var family: String
    @ViewState private var searching = false
    @ViewState private var query = ""

    var body: some View {
        VStack(alignment: .leading, spacing: 8) {
            Text(title).font(.headline)
            Button { searching.toggle() } label: {
                Text(family.isEmpty ? "默认：\(defaultName)" : family).lineLimit(1)
            }
                .buttonStyle(.glass)
                .accessibilityIdentifier("LumiBar.font." + title)
                .popover(isPresented: $searching) {
                    let names = NSFontManager.shared.availableFontFamilies
                    let results = query.isEmpty ? names : names.filter { $0.localizedCaseInsensitiveContains(query) }
                    VStack(alignment: .leading, spacing: 10) {
                        TextField("搜索已安装字体", text: $query).textFieldStyle(.roundedBorder)
                        List(results, id: \.self) { name in
                            Button(name) { family = name; searching = false }.buttonStyle(.plain)
                        }
                    }.padding(13).frame(width: 290, height: 290)
                }
            Button("恢复默认：\(defaultName)") { family = "" }
                .buttonStyle(.glass)
                .accessibilityIdentifier("LumiBar.font.reset." + title)
                .disabled(family.isEmpty)
        }
    }
}
