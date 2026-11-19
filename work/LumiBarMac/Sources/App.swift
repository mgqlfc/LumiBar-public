import AppKit
import OSLog
import SwiftUI

// Command Line Tools ships the state wrapper without the SwiftUI macro plug-in.
typealias ViewState<Value> = SwiftUI.State<Value>

@main
enum LumiBarApp {
    @MainActor
    static func main() {
        let app = NSApplication.shared
        let delegate = LumiBarController()
        app.delegate = delegate
        app.setActivationPolicy(.accessory)
        app.run()
    }
}

private final class SurfacePanel: NSPanel {
    override var canBecomeKey: Bool { true }

    init() {
        super.init(contentRect: .zero, styleMask: [.borderless, .nonactivatingPanel],
                   backing: .buffered, defer: false)
        isOpaque = false
        backgroundColor = .clear
        hasShadow = false
        hidesOnDeactivate = false
        isMovable = false
        isFloatingPanel = true
        isReleasedWhenClosed = false
        level = .statusBar
        collectionBehavior = [.moveToActiveSpace, .fullScreenNone, .ignoresCycle]
        animationBehavior = .none
    }

    func present(_ frame: NSRect, key: Bool = false) {
        setFrame(frame, display: true)
        if key { makeKeyAndOrderFront(nil) }
        else if !isVisible { orderFrontRegardless() }
    }

    override func cancelOperation(_ sender: Any?) {
        orderOut(sender)
    }
}

@MainActor
final class LumiBarController: NSObject, NSApplicationDelegate {
    private let preview = ProcessInfo.processInfo.arguments.contains("--preview")
    private let logger = Logger(subsystem: "com.lumibar.macos", category: "lifecycle")
    private var previewDomain: String?
    private lazy var defaults: UserDefaults = {
        // The preview never reads or writes the user's preferences.
        if preview {
            let name = "com.lumibar.macos.preview.\(UUID())"
            if let isolated = UserDefaults(suiteName: name) {
                previewDomain = name
                return isolated
            }
        }
        return .standard
    }()
    private lazy var weather = WeatherModel(defaults: defaults)
    private lazy var music = MusicModel(defaults: defaults)
    private let status = NSStatusBar.system.statusItem(withLength: NSStatusItem.variableLength)
    private let weatherPanel = SurfacePanel()
    private let leadingPanel = SurfacePanel()
    private let trailingPanel = SurfacePanel()
    private let capsulePanel = SurfacePanel()
    private let expandedPanel = SurfacePanel()
    private var settings: NSWindow?
    private var timer: Timer?
    private var weatherTimer: Timer?
    private var globalClicks: Any?
    private var localClicks: Any?
    private var pointedAtWeather: Date?
    private var weatherPinned = false
    private var movingAwayAt: Date?
    private var musicHoverStartsAt: Date?
    private var wasAtMusic = false
    private var musicDismissedUntilExit = false
    private var backgrounded = false
    private var musicScreen: NSScreen? { NSScreen.screens.first }

    func applicationDidFinishLaunching(_ notification: Notification) {
        status.button?.target = self
        status.button?.action = #selector(weatherClicked)
        status.button?.sendAction(on: [.leftMouseUp, .rightMouseUp])
        status.button?.setAccessibilityLabel("LumiBar 天气")
        weather.onChange = { [weak self] in self?.updateStatus() }
        music.onTrackChange = { [weak self] in self?.layoutMusic() }

        for (panel, surface) in [(leadingPanel, MusicView.Surface.leading), (trailingPanel, .trailing),
                                 (capsulePanel, .capsule), (expandedPanel, .expanded)] {
            panel.contentView = NSHostingView(rootView: MusicView(model: music, surface: surface) { [weak self] in
                self?.toggleLyrics()
            })
        }

        let center = NotificationCenter.default
        center.addObserver(self, selector: #selector(screensChanged), name: NSApplication.didChangeScreenParametersNotification, object: nil)
        let workspace = NSWorkspace.shared.notificationCenter
        workspace.addObserver(self, selector: #selector(spaceChanged), name: NSWorkspace.activeSpaceDidChangeNotification, object: nil)
        workspace.addObserver(self, selector: #selector(sleeping), name: NSWorkspace.willSleepNotification, object: nil)
        workspace.addObserver(self, selector: #selector(woke), name: NSWorkspace.didWakeNotification, object: nil)
        globalClicks = NSEvent.addGlobalMonitorForEvents(matching: [.leftMouseDown, .rightMouseDown]) { [weak self] _ in
            Task { @MainActor in self?.outsideClick() }
        }
        localClicks = NSEvent.addLocalMonitorForEvents(matching: [.leftMouseDown, .rightMouseDown]) { [weak self] event in
            self?.outsideClick()
            return event
        }
        timer = Timer(timeInterval: 0.10, target: self, selector: #selector(pointerTick), userInfo: nil, repeats: true)
        if let timer { RunLoop.main.add(timer, forMode: .common) }
        weatherTimer = Timer.scheduledTimer(timeInterval: 20 * 60, target: self,
                                            selector: #selector(refreshWeather), userInfo: nil, repeats: true)

        if preview {
            PreviewData.install(weather: weather, music: music)
            if ProcessInfo.processInfo.arguments.contains("--weather") {
                Task { @MainActor [weak self] in
                    try? await Task.sleep(for: .milliseconds(350))
                    self?.openPreviewWeather()
                }
            }
            if ProcessInfo.processInfo.arguments.contains("--lyrics") {
                music.mode = .lyrics
                layoutMusic()
            }
        } else {
            weather.refresh()
            music.start()
        }
        updateStatus()
        logger.notice("LumiBar started")
    }

    func applicationWillTerminate(_ notification: Notification) {
        timer?.invalidate()
        weatherTimer?.invalidate()
        if let globalClicks { NSEvent.removeMonitor(globalClicks) }
        if let localClicks { NSEvent.removeMonitor(localClicks) }
        music.spectrum.stop()
        if let previewDomain { defaults.removePersistentDomain(forName: previewDomain) }
    }

    @objc private func refreshWeather() {
        if !preview { weather.refresh() }
    }

    private var statusFrame: NSRect? {
        guard let button = status.button, let window = button.window else { return nil }
        let anchor = window.convertToScreen(button.convert(button.bounds, to: nil))
        let center = NSPoint(x: anchor.midX, y: anchor.midY)
        guard anchor.width > 0, NSScreen.screens.contains(where: { screen in
            screen.frame.contains(center) && screen.frame.maxY - center.y < max(48, screen.safeAreaInsets.top + 8)
        }) else { return nil }
        return anchor
    }

    private func openPreviewWeather() {
        guard let screen = musicScreen else { return }
        let menuHeight = max(24, screen.safeAreaInsets.top)
        let fallback = NSRect(x: screen.frame.maxX - 168, y: screen.frame.maxY - menuHeight,
                              width: 56, height: menuHeight)
        weatherPinned = true
        showWeather(anchor: statusFrame ?? fallback, compact: false)
    }

    private func updateStatus() {
        guard let button = status.button else { return }
        let weather = weather.weather
        if let weather {
            let appearance = Weather.appearance(weather.code, day: weather.isDay)
            button.image = menuGlyph(weather) ?? NSImage(systemSymbolName: appearance.symbol, accessibilityDescription: appearance.name)
            button.imagePosition = .imageLeading
            let temperature = weather.temperature.formatted(.number.precision(.fractionLength(0))) + "°"
            let rain = weather.rain.map { "  💧" + $0.formatted(.number.precision(.fractionLength(0))) + "%" } ?? ""
            let title = NSMutableAttributedString(string: temperature,
                attributes: [.font: NSFont.monospacedDigitSystemFont(ofSize: 13, weight: .semibold)])
            title.append(NSAttributedString(string: rain,
                attributes: [.font: NSFont.monospacedDigitSystemFont(ofSize: 9, weight: .regular)]))
            button.attributedTitle = title
            button.setAccessibilityLabel("LumiBar 天气，\(temperature)\(rain)")
        } else {
            let image = NSImage(systemSymbolName: "cloud.sun", accessibilityDescription: nil)
            image?.isTemplate = true
            button.image = image
            button.imagePosition = .imageOnly
            button.title = ""
            button.setAccessibilityLabel("LumiBar 天气")
        }
    }

    private func menuGlyph(_ weather: Weather) -> NSImage? {
        guard let original = WeatherGlyph.image(weather.code, day: weather.isDay) else { return nil }
        let icon = NSImage(size: NSSize(width: 19, height: 19), flipped: false) { rect in
            original.draw(in: rect)
            return true
        }
        icon.isTemplate = false
        return icon
    }

    @objc private func weatherClicked(_ sender: NSStatusBarButton) {
        if NSApp.currentEvent?.type == .rightMouseUp {
            weatherPinned = false
            weatherPanel.orderOut(nil)
            let menu = NSMenu()
            menu.addItem(withTitle: "LumiBar 设置…", action: #selector(showSettings), keyEquivalent: ",").target = self
            menu.addItem(.separator())
            menu.addItem(withTitle: "退出 LumiBar", action: #selector(quit), keyEquivalent: "q").target = self
            menu.popUp(positioning: nil, at: NSPoint(x: 0, y: sender.bounds.height), in: sender)
            return
        }
        let cursor = NSEvent.mouseLocation
        let anchor = statusFrame ?? NSRect(x: cursor.x - 10, y: cursor.y - 14, width: 20, height: 28)
        weatherPinned.toggle()
        pointedAtWeather = nil
        if weatherPinned { showWeather(anchor: anchor, compact: false) }
        else { weatherPanel.orderOut(nil) }
    }

    private func showWeather(anchor: NSRect, compact: Bool) {
        guard let screen = NSScreen.screens.first(where: { $0.frame.contains(.init(x: anchor.midX, y: anchor.midY)) })
                ?? musicScreen else { return }
        let width = min(compact ? 316 : 478, screen.visibleFrame.width)
        let height = min(compact ? 205 : 696, max(180, anchor.minY - screen.visibleFrame.minY - 5))
        let x = max(screen.visibleFrame.minX, min(anchor.midX - width / 2, screen.visibleFrame.maxX - width))
        weatherPanel.contentView = NSHostingView(rootView: WeatherView(model: weather, compact: compact,
            width: width, height: height, showSettings: { [weak self] in self?.showSettings() },
            quit: { [weak self] in self?.quit() }))
        weatherPanel.ignoresMouseEvents = compact
        weatherPanel.present(NSRect(x: x, y: anchor.minY - height - 3, width: width, height: height), key: !compact)
    }

    private func outsideClick() {
        let pointer = NSEvent.mouseLocation
        if weatherPinned, weatherPanel.isVisible, !weatherPanel.frame.contains(pointer),
           statusFrame?.contains(pointer) != true {
            weatherPinned = false
            weatherPanel.orderOut(nil)
        }
        if music.mode == .lyrics, !musicPanels.contains(where: { $0.isVisible && $0.frame.contains(pointer) }) {
            music.mode = .compact
            layoutMusic()
        }
    }

    private var musicPanels: [NSPanel] { [leadingPanel, trailingPanel, capsulePanel, expandedPanel] }

    @objc private func pointerTick() {
        let pointer = NSEvent.mouseLocation
        let now = Date.now
        if weatherPinned && !weatherPanel.isVisible { weatherPinned = false }
        if !weatherPinned, let anchor = statusFrame {
            if anchor.contains(pointer) {
                if let began = pointedAtWeather {
                    if now.timeIntervalSince(began) > 0.4 && !weatherPanel.isVisible {
                        showWeather(anchor: anchor, compact: true)
                    }
                } else {
                    pointedAtWeather = now
                }
            } else if pointedAtWeather != nil {
                pointedAtWeather = nil
                weatherPanel.orderOut(nil)
            }
        }

        guard music.track != nil, !backgrounded else { return }
        let hovering = musicPanels.contains { $0.isVisible && $0.frame.insetBy(dx: -2, dy: -5).contains(pointer) }
        if hovering && !wasAtMusic { musicHoverStartsAt = now }
        if !hovering {
            musicHoverStartsAt = nil
            musicDismissedUntilExit = false
        }
        wasAtMusic = hovering
        if music.mode == .compact, hovering, !musicDismissedUntilExit,
           let began = musicHoverStartsAt, now.timeIntervalSince(began) >= 0.18 {
            music.mode = .controls
            layoutMusic()
        } else if music.mode == .controls, !hovering {
            if let began = movingAwayAt, now.timeIntervalSince(began) > 0.24 {
                music.mode = .compact
                layoutMusic()
            } else if movingAwayAt == nil {
                movingAwayAt = now
            }
        } else {
            movingAwayAt = nil
        }
        if music.mode != .compact && !expandedPanel.isVisible {
            music.mode = .compact
            musicDismissedUntilExit = true
        }
    }

    private func toggleLyrics() {
        if music.mode == .lyrics {
            music.mode = .compact
            musicDismissedUntilExit = true
        } else {
            music.mode = .lyrics
        }
        layoutMusic()
    }

    private func layoutMusic() {
        guard let screen = musicScreen, music.track != nil, !backgrounded else {
            musicPanels.forEach { $0.orderOut(nil) }
            return
        }
        let frame = screen.frame
        let leading = screen.auxiliaryTopLeftArea
        let trailing = screen.auxiliaryTopRightArea
        let hasNotch = screen.safeAreaInsets.top > 0 && leading != nil && trailing != nil
        var anchorX = frame.midX
        let top = frame.maxY - max(2, screen.safeAreaInsets.top)
        if hasNotch, let leading, let trailing {
            capsulePanel.orderOut(nil)
            anchorX = (leading.maxX + trailing.minX) / 2
            let height = min(27, leading.height - 4, trailing.height - 4)
            if height >= 19 && leading.width >= 57 && trailing.width >= 57 {
                let y = top + (screen.safeAreaInsets.top - height) / 2
                leadingPanel.present(.init(x: leading.maxX - 47, y: y, width: 44, height: height))
                trailingPanel.present(.init(x: trailing.minX + 3, y: y, width: 52, height: height))
            } else {
                leadingPanel.orderOut(nil)
                trailingPanel.orderOut(nil)
                capsulePanel.present(.init(x: anchorX - 91, y: top - 32, width: 182, height: 28))
            }
        } else {
            leadingPanel.orderOut(nil)
            trailingPanel.orderOut(nil)
            let menuBottom = min(frame.maxY - 6, screen.visibleFrame.maxY)
            capsulePanel.present(.init(x: frame.midX - 91, y: menuBottom - 34, width: 182, height: 28))
        }
        if music.mode == .compact {
            expandedPanel.orderOut(nil)
        } else {
            let width: CGFloat = music.mode == .lyrics ? 478 : 342
            let height: CGFloat = music.mode == .lyrics ? 370 : 78
            let y = hasNotch ? top + 3 : capsulePanel.frame.minY + 2
            expandedPanel.present(.init(x: anchorX - width / 2, y: y - height, width: width, height: height))
        }
    }

    @objc private func screensChanged() {
        weatherPanel.orderOut(nil)
        weatherPinned = false
        layoutMusic()
    }

    @objc private func spaceChanged() {
        weatherPanel.orderOut(nil)
        weatherPinned = false
        if !preview { music.mode = .compact }
        layoutMusic()
    }

    @objc private func sleeping() {
        backgrounded = true
        music.spectrum.stop()
        layoutMusic()
    }

    @objc private func woke() {
        backgrounded = false
        if music.spectrumEnabled { music.restartSpectrum() }
        layoutMusic()
        refreshWeather()
    }

    @objc private func showSettings() {
        weatherPinned = false
        weatherPanel.orderOut(nil)
        if settings == nil {
            let controller = NSHostingController(rootView: SettingsView(weather: weather, music: music))
            let window = NSWindow(contentViewController: controller)
            window.title = "LumiBar 设置"
            window.styleMask = [.titled, .closable]
            window.isReleasedWhenClosed = false
            window.center()
            settings = window
        }
        NSApp.activate()
        settings?.makeKeyAndOrderFront(nil)
    }

    @objc private func quit() { NSApp.terminate(nil) }
}

private struct SettingsView: View {
    @Bindable var weather: WeatherModel
    @Bindable var music: MusicModel

    var body: some View {
        Form {
            Section("音乐岛") {
                Toggle("显示音乐岛", isOn: $music.showMusic)
                Toggle("实时频谱（系统音频权限）", isOn: $music.spectrumEnabled)
                Toggle("从 LRCLIB 查找同步歌词", isOn: $music.onlineLyrics)
                Text("Apple Music 和 Spotify 使用 macOS 自动化权限。")
                    .font(.caption).foregroundStyle(.secondary)
                if let error = music.automationError {
                    Text(error).font(.caption).foregroundStyle(.secondary)
                    Button("重新检查自动化权限") { music.retryAutomation() }
                }
                if let error = music.audioError { Text(error).font(.caption).foregroundStyle(.secondary) }
            }
            Section("天气") {
                Toggle("优先使用 LumiBar 天气代理", isOn: $weather.proxyEnabled)
                if weather.proxyEnabled {
                    TextField("HTTPS 服务地址", text: $weather.proxy)
                    if WeatherAPI.proxyURL(weather.proxy) == nil { Text("请输入有效的 HTTPS 地址。").foregroundStyle(.red) }
                }
                Button("应用并刷新天气") { weather.refresh() }
                    .disabled(weather.city == nil || (weather.proxyEnabled && WeatherAPI.proxyURL(weather.proxy) == nil))
            }
        }
        .formStyle(.grouped)
        .frame(width: 455, height: 390)
    }
}
