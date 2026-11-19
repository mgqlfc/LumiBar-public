import AppKit
import CoreText
import OSLog
import SwiftUI

// Command Line Tools ships the state wrapper without the SwiftUI macro plug-in.
typealias ViewState<Value> = SwiftUI.State<Value>

@MainActor
func lumiFont(_ size: CGFloat, weight: Font.Weight = .regular) -> Font {
    if NSFont(name: "Geist-Regular", size: size) != nil { return .custom("Geist", size: size).weight(weight) }
    return .system(size: size, weight: weight)
}

@main
enum LumiBarApp {
    @MainActor
    static func main() {
        if let geist = Bundle.main.url(forResource: "Geist-VariableFont_wght", withExtension: "ttf") {
            CTFontManagerRegisterFontsForURL(geist as CFURL, .process, nil)
        }
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
    private lazy var appearance = AppearanceModel(defaults: defaults)
    private lazy var weather = WeatherModel(defaults: defaults)
    private lazy var music = MusicModel(defaults: defaults)
    private let status = NSStatusBar.system.statusItem(withLength: NSStatusItem.variableLength)
    private let weatherPanel = SurfacePanel()
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
        appearance.onThemeChange = { [weak self] in
            guard let self else { return }
            weatherPanel.appearance = appearance.windowAppearance
            expandedPanel.appearance = appearance.windowAppearance
            settings?.appearance = appearance.windowAppearance
        }
        music.onTrackChange = { [weak self] in self?.layoutMusic() }

        for (panel, surface) in [(capsulePanel, MusicView.Surface.capsule), (expandedPanel, .expanded)] {
            panel.contentView = NSHostingView(rootView: MusicView(model: music, appearance: appearance, surface: surface) { [weak self] in
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

        let arguments = ProcessInfo.processInfo.arguments
        if preview {
            PreviewData.install(weather: weather, music: music)
            if arguments.contains("--weather") || arguments.contains("--weather-hover") {
                Task { @MainActor [weak self] in
                    try? await Task.sleep(for: .milliseconds(350))
                    self?.openPreviewWeather(compact: arguments.contains("--weather-hover"))
                }
            }
            if arguments.contains("--lyrics") {
                music.mode = .lyrics
                layoutMusic()
            }
        } else {
            weather.refresh()
            music.start()
        }
        updateStatus()
        if arguments.contains("--settings") { showSettings() }
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

    private func openPreviewWeather(compact: Bool) {
        guard let screen = musicScreen else { return }
        let menuHeight = max(24, screen.safeAreaInsets.top)
        let fallback = NSRect(x: screen.frame.maxX - 168, y: screen.frame.maxY - menuHeight,
                              width: 56, height: menuHeight)
        weatherPinned = true
        showWeather(anchor: statusFrame ?? fallback, compact: compact)
    }

    private func updateStatus() {
        guard let button = status.button else { return }
        let weather = weather.weather
        if let weather {
            let appearance = Weather.appearance(weather.code, day: weather.isDay)
            button.image = menuGlyph(weather) ?? NSImage(systemSymbolName: appearance.symbol, accessibilityDescription: appearance.name)
            button.imagePosition = .imageLeading
            let temperature = self.weather.temperature(weather.temperature)
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
        let width = min(compact ? 314 : 496, screen.visibleFrame.width)
        let height = min(compact ? 177 : 696, max(172, anchor.minY - screen.visibleFrame.minY - 5))
        weatherPanel.appearance = appearance.windowAppearance
        let x = max(screen.visibleFrame.minX, min(anchor.midX - width / 2, screen.visibleFrame.maxX - width))
        weatherPanel.contentView = NSHostingView(rootView: WeatherView(model: weather, appearance: appearance, compact: compact,
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

    private var musicPanels: [NSPanel] { [capsulePanel, expandedPanel] }

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
        let anchorX = if let left = screen.auxiliaryTopLeftArea,
                         let right = screen.auxiliaryTopRightArea, screen.safeAreaInsets.top > 0 {
            (left.maxX + right.minX) / 2
        } else {
            frame.midX
        }
        let menuHeight = max(NSStatusBar.system.thickness, screen.safeAreaInsets.top)
        let menuBottom = min(frame.maxY - menuHeight, screen.visibleFrame.maxY)
        // The menu bar itself belongs to WindowServer. Mount the capsule
        // immediately under its lower edge, including on a notched display.
        let capsuleTop = menuBottom - 8
        let capsuleWidth: CGFloat = 238
        let capsuleHeight: CGFloat = 34
        capsulePanel.present(.init(x: anchorX - capsuleWidth / 2, y: capsuleTop - capsuleHeight,
                                   width: capsuleWidth, height: capsuleHeight))
        if music.mode == .compact {
            expandedPanel.orderOut(nil)
        } else {
            let width: CGFloat = music.mode == .lyrics ? 432 : 342
            let y = capsulePanel.frame.minY + 2
            let height = music.mode == .lyrics ? min(658, max(300, y - screen.visibleFrame.minY - 5)) : 78
            expandedPanel.appearance = appearance.windowAppearance
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
            let controller = NSHostingController(rootView: SettingsView(appearance: appearance, weather: weather, music: music))
            let window = NSWindow(contentViewController: controller)
            window.title = "LumiBar"
            window.appearance = appearance.windowAppearance
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

