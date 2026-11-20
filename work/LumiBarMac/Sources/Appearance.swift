import SwiftUI

struct AccentPalette: Identifiable {
    let id: String
    let name: String
    let light: UInt32
    let mid: UInt32
    let dark: UInt32

    var color: Color { Self.color(mid) }
    var gradient: LinearGradient {
        LinearGradient(stops: [.init(color: Self.color(light), location: 0),
                               .init(color: Self.color(mid), location: 0.46),
                               .init(color: Self.color(dark), location: 1)],
                       startPoint: .topLeading, endPoint: .bottomTrailing)
    }

    static let all: [Self] = [
        .init(id: "blue", name: "冰糖蓝", light: 0x6CE7DA, mid: 0x21BEC2, dark: 0x118FB1),
        .init(id: "mint", name: "薄荷绿", light: 0xBAF0D6, mid: 0x6BCDAA, dark: 0x399F8C),
        .init(id: "lime", name: "青柠糖", light: 0xE0EEB3, mid: 0xB4CF82, dark: 0x7DA56C),
        .init(id: "butter", name: "奶油黄", light: 0xFFF1B6, mid: 0xF0D47A, dark: 0xD0B365),
        .init(id: "apricot", name: "奶杏橙", light: 0xFFE0BB, mid: 0xF2BA91, dark: 0xD99579),
        .init(id: "peach", name: "蜜桃粉", light: 0xFFD3DF, mid: 0xF0ACC9, dark: 0xD48EB6),
        .init(id: "berry", name: "莓果红", light: 0xFFD0CE, mid: 0xEF9EA9, dark: 0xD17F9C),
        .init(id: "lavender", name: "薰衣紫", light: 0xE5D5FF, mid: 0xC6ACEF, dark: 0xA58BDA),
        .init(id: "periwinkle", name: "绣球蓝", light: 0xD2E3FF, mid: 0xA9C4F3, dark: 0x86A5DE)
    ]

    static func color(_ value: UInt32) -> Color {
        Color(red: Double((value >> 16) & 255) / 255, green: Double((value >> 8) & 255) / 255,
              blue: Double(value & 255) / 255)
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
    var accentID: String {
        didSet { defaults.set(accentID, forKey: "appearance.accent") }
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
    var accent: AccentPalette { AccentPalette.all.first { $0.id == accentID } ?? AccentPalette.all[7] }
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
        accentID = defaults.string(forKey: "appearance.accent") ?? "lavender"
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
            return AccentPalette.color(0x141927).opacity(0.40 + (density - 0.35) * 0.62)
        }
        return Color.white.opacity(0.34 + (density - 0.35) * 0.37)
    }

    func styledText(_ value: String, size: CGFloat, weight: Font.Weight = .regular) -> Text {
        let pieces = scriptRuns(value)
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

    func textWidth(_ value: String, size: CGFloat, semibold: Bool = false) -> CGFloat {
        scriptRuns(value).reduce(0) { total, run in
            let fallback = NSFont.systemFont(ofSize: size, weight: semibold ? .semibold : .regular)
            let font = fontFamily(forChinese: run.1).flatMap {
                NSFontManager.shared.font(withFamily: $0, traits: [], weight: semibold ? 8 : 5, size: size)
            } ?? fallback
            return total + (run.0 as NSString).size(withAttributes: [.font: font]).width
        }
    }

    private func fontFamily(forChinese chinese: Bool) -> String? {
        if chinese { return chineseFont.isEmpty ? nil : chineseFont }
        return westernFont.isEmpty ? "Geist" : westernFont
    }

    private func scriptRuns(_ value: String) -> [(String, Bool)] {
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
    enum Page: String, CaseIterable {
        case general = "常规"
        case appearance = "外观"
        case entries = "菜单栏"
        case weather = "天气"
        case music = "音乐与歌词"
        case plugins = "插件"
        case about = "关于"

        var symbol: String {
            switch self {
            case .general: "gearshape"
            case .appearance: "pencil"
            case .entries: "menubar.rectangle"
            case .weather: "sun.max"
            case .music: "music.note"
            case .plugins: "puzzlepiece.extension"
            case .about: "info.circle"
            }
        }
    }

    @Bindable var appearance: AppearanceModel
    @Bindable var weather: WeatherModel
    @Bindable var music: MusicModel
    var close: () -> Void = {}
    @ViewState private var page: Page = .appearance
    @Environment(\.colorScheme) private var colorScheme

    var body: some View {
        VStack(spacing: 0) {
            HStack(spacing: 10) {
                Image(nsImage: NSApplication.shared.applicationIconImage).resizable()
                    .scaledToFit().frame(width: 26, height: 26)
                appearance.styledText("LumiBar", size: 21, weight: .semibold)
                Spacer()
                Button(action: close) {
                    Image(systemName: "xmark").font(.system(size: 16, weight: .light))
                        .frame(width: 31, height: 34).contentShape(Rectangle())
                }.buttonStyle(.plain).keyboardShortcut("w", modifiers: .command).accessibilityLabel("关闭设置")
            }.padding(.horizontal, 20).frame(height: 54)
            Divider()
            HStack(spacing: 0) {
                VStack(alignment: .leading, spacing: 6) {
                    ForEach(Page.allCases, id: \.self) { item in
                        Button { page = item } label: {
                            HStack(spacing: 10) {
                                Image(systemName: item.symbol).font(.system(size: 16, weight: .light)).frame(width: 18)
                                appearance.styledText(item.rawValue, size: 15)
                            }
                            .frame(maxWidth: .infinity, alignment: .leading)
                            .padding(.horizontal, 12).frame(height: 42)
                            .contentShape(Rectangle())
                        }
                        .buttonStyle(SettingsNavigationStyle(selected: page == item, color: appearance.accent.color))
                        .accessibilityAddTraits(page == item ? .isSelected : [])
                    }
                    Spacer(minLength: 0)
                }
                .frame(width: 144).padding(.horizontal, 14).padding(.top, 22)
                ScrollView {
                    switch page {
                    case .general:
                        VStack(alignment: .leading, spacing: 18) {
                            heading("常规", "LumiBar 在菜单栏运行")
                            Toggle("显示音乐入口", isOn: $music.showMusic)
                            Toggle("动态光效", isOn: $appearance.animation)
                        }.padding(24).frame(maxWidth: .infinity, alignment: .leading)
                    case .appearance: appearancePage
                    case .entries:
                        VStack(alignment: .leading, spacing: 18) {
                            heading("菜单栏", "调整菜单栏入口")
                            Toggle("显示音乐入口", isOn: $music.showMusic)
                        }.padding(24).frame(maxWidth: .infinity, alignment: .leading)
                    case .weather: weatherPage
                    case .music: musicPage
                    case .plugins:
                        heading("插件", "当前 Mac 版使用内置前端。Windows 外部前端的配置由 Windows 客户端管理。")
                            .padding(24).frame(maxWidth: .infinity, alignment: .leading)
                    case .about:
                        heading("关于 LumiBar", "macOS 版 · " + (Bundle.main.infoDictionary?["CFBundleShortVersionString"] as? String ?? ""))
                            .padding(24).frame(maxWidth: .infinity, alignment: .leading)
                    }
                }.scrollIndicators(.visible)
            }
        }
        .frame(width: 560, height: 650)
        .tint(appearance.accent.color)
        .modifier(FlyoutChrome(appearance: appearance))
    }

    private var appearancePage: some View {
        VStack(alignment: .leading, spacing: 26) {
            heading("外观与光效", "让界面通透起来，让音乐染上颜色。")
            VStack(alignment: .leading, spacing: 9) {
                Text("主题").font(.headline)
                HStack(spacing: 0) {
                    ForEach([("System", "跟随系统"), ("Light", "浅色"), ("Dark", "深色")], id: \.0) { value, label in
                        Button { appearance.theme = value } label: {
                            appearance.styledText(label, size: 12, weight: .medium)
                                .frame(maxWidth: .infinity).frame(height: 27).contentShape(Rectangle())
                        }
                        .buttonStyle(SettingsThemeStyle(selected: appearance.theme == value, color: appearance.accent.color))
                        .accessibilityAddTraits(appearance.theme == value ? .isSelected : [])
                    }
                }.padding(3).background(Color.primary.opacity(0.045), in: Capsule())
            }
            VStack(alignment: .leading, spacing: 13) {
                HStack {
                    Text("强调色").font(.headline)
                    Spacer()
                    Text(appearance.accent.name).font(.caption).foregroundStyle(.secondary)
                }
                HStack(spacing: 7) {
                    ForEach(AccentPalette.all) { palette in
                        let selected = appearance.accentID == palette.id
                        Button { appearance.accentID = palette.id } label: {
                            RoundedRectangle(cornerRadius: 8)
                                .fill(palette.gradient).frame(width: 28, height: 28)
                                .overlay {
                                    if selected {
                                        Image(systemName: "checkmark.circle.fill").font(.system(size: 14))
                                            .symbolRenderingMode(.palette).foregroundStyle(.black.opacity(0.65), .white)
                                    }
                                }
                                .overlay {
                                    if selected { RoundedRectangle(cornerRadius: 10).strokeBorder(.primary.opacity(0.7))
                                        .padding(-3) }
                                }
                                .padding(2)
                        }
                        .buttonStyle(.plain).help(palette.name).accessibilityLabel(palette.name)
                        .accessibilityAddTraits(selected ? .isSelected : [])
                    }
                }
            }
            VStack(alignment: .leading, spacing: 10) {
                Text("玻璃密度").font(.headline)
                HStack(spacing: 12) {
                    Slider(value: $appearance.glassDensity, in: 0.35...0.95, step: 0.01)
                        .accessibilityLabel("玻璃密度")
                    Text(appearance.glassDensity.formatted(.percent.precision(.fractionLength(0))))
                        .font(.subheadline.monospacedDigit()).frame(width: 42, alignment: .trailing)
                }.frame(height: 26).padding(.trailing, 10)
            }
            Divider()
            toggleRow("封面着色", "根据当前播放的封面，在界面上融入柔和的色彩", binding: $appearance.coverTint)
            Divider()
            toggleRow("动态光效", "随着音乐播放呈现轻柔的流动光效", binding: $appearance.animation)
            Divider()
            Text("效果预览").font(.headline)
            HStack(spacing: 9) {
                HStack(spacing: 8) {
                    if let art = music.artwork {
                        Image(nsImage: art).resizable().scaledToFill().frame(width: 36, height: 36)
                            .clipShape(RoundedRectangle(cornerRadius: 7))
                    } else {
                        Image(systemName: "music.note").frame(width: 36, height: 36)
                            .background(.white.opacity(0.18), in: RoundedRectangle(cornerRadius: 7))
                    }
                    VStack(alignment: .leading, spacing: 3) {
                        Text(music.track?.title ?? "音乐岛").font(lumiFont(15, weight: .semibold)).lineLimit(1)
                        Text(music.track?.artist ?? "预览").font(lumiFont(11)).foregroundStyle(.secondary).lineLimit(1)
                    }
                    Spacer(minLength: 0)
                }
                .padding(9)
                .background(appearance.coverTint ? music.colors[0].opacity(appearance.coverBlend) : .clear,
                            in: RoundedRectangle(cornerRadius: 15))
                HStack(spacing: 4) {
                    if let glyph = WeatherGlyph.image(0, day: false) {
                        Image(nsImage: glyph).resizable().scaledToFit().frame(width: 20, height: 20)
                    }
                    Text(weather.weather.map { weather.temperature($0.temperature) } ?? "3°")
                }.padding(8).background(.primary.opacity(0.08), in: RoundedRectangle(cornerRadius: 11))
            }
            .padding(8).glassEffect(.regular, in: RoundedRectangle(cornerRadius: 18))
            percentControl("着色浓度", value: $appearance.coverBlend, range: 0.08...0.55)
            percentControl("封面饱和度", value: $appearance.coverSaturation, range: 0...1)
            FontChoice(title: "中文字体", defaultName: "系统字体", family: $appearance.chineseFont)
            FontChoice(title: "西文字体", defaultName: "Geist", family: $appearance.westernFont)
        }
        .padding(24)
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
        VStack(alignment: .leading, spacing: 16) {
            heading("天气", "城市、单位和数据来源")
            Toggle("华氏温度", isOn: $weather.fahrenheit)
            Toggle("风速使用 m/s", isOn: $weather.windMetersPerSecond)
            Divider()
            Toggle("优先使用 LumiBar 天气代理", isOn: $weather.proxyEnabled)
            if weather.proxyEnabled {
                TextField("HTTPS 服务地址", text: $weather.proxy)
                if WeatherAPI.proxyURL(weather.proxy) == nil { Text("请输入有效的 HTTPS 地址。").foregroundStyle(.red) }
            }
            Button("应用并刷新天气") { weather.refresh() }
                .disabled(weather.city == nil || (weather.proxyEnabled && WeatherAPI.proxyURL(weather.proxy) == nil))
            Button("定位当前区县") { weather.locate() }
        }.padding(24).frame(maxWidth: .infinity, alignment: .leading)
    }

    private var musicPage: some View {
        VStack(alignment: .leading, spacing: 16) {
            heading("音乐与歌词", "设置音乐岛、播放器与同步歌词")
            Button("连接网易云音乐…") { music.connectNetease() }.buttonStyle(.glassProminent)
            if let status = music.neteaseStatus { Text(status).font(.caption).foregroundStyle(.secondary) }
            Divider()
            Toggle("显示音乐岛", isOn: $music.showMusic)
            Toggle("实时频谱（系统音频权限）", isOn: $music.spectrumEnabled)
            Toggle("在线查找同步歌词", isOn: $music.onlineLyrics)
            VStack(alignment: .leading, spacing: 8) {
                Text("歌词字号").font(.headline)
                HStack {
                    Slider(value: $appearance.lyricsSize, in: 18...36, step: 1).accessibilityLabel("歌词字号")
                    Text(Int(appearance.lyricsSize).formatted()).frame(width: 30, alignment: .trailing)
                }
            }
            if let error = music.audioError { Text(error).font(.caption).foregroundStyle(.secondary) }
            Text("Apple Music 和 Spotify 使用 macOS 自动化权限。")
                .font(.caption).foregroundStyle(.secondary)
            if let error = music.automationError {
                Text(error).font(.caption).foregroundStyle(.secondary)
                Button("重新检查自动化权限") { music.retryAutomation() }
            }
        }.padding(24).frame(maxWidth: .infinity, alignment: .leading)
    }
}


private struct SettingsThemeStyle: ButtonStyle {
    let selected: Bool
    let color: Color
    @ViewState private var hovered = false

    func makeBody(configuration: Configuration) -> some View {
        configuration.label
            .background(color.opacity(selected ? (hovered ? 0.32 : 0.24) : hovered ? 0.10 : 0), in: Capsule())
            .onHover { hovered = $0 }
    }
}

private struct SettingsNavigationStyle: ButtonStyle {
    let selected: Bool
    let color: Color
    @ViewState private var hovered = false

    func makeBody(configuration: Configuration) -> some View {
        configuration.label
            .background(color.opacity(selected ? (hovered ? 0.30 : 0.22) : hovered ? 0.10 : 0),
                        in: RoundedRectangle(cornerRadius: 9))
            .onHover { hovered = $0 }
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
            Button(family.isEmpty ? "默认：\(defaultName)" : family) { searching.toggle() }
                .popover(isPresented: $searching) {
                    let names = NSFontManager.shared.availableFontFamilies
                    let results = query.isEmpty ? names : names.filter { $0.localizedCaseInsensitiveContains(query) }
                    VStack(alignment: .leading, spacing: 10) {
                        TextField("搜索已安装字体", text: $query)
                        List(results, id: \.self) { name in
                            Button(name) { family = name; searching = false }.buttonStyle(.plain)
                        }
                    }.padding(13).frame(width: 290, height: 290)
                }
            Button("恢复默认：\(defaultName)") { family = "" }.disabled(family.isEmpty)
        }
    }
}
