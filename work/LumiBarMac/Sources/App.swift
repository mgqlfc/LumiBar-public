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
    private var transition = 0
    private(set) var closing = false
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
        if closing {
            transition += 1
            closing = false
            NSAnimationContext.runAnimationGroup { context in
                context.duration = 0
                animator().alphaValue = 1
            }
        }
        if self.frame != frame { setFrame(frame, display: true) }
        if key { makeKeyAndOrderFront(nil) }
        else if !isVisible { orderFrontRegardless() }
    }

    func dismiss(duration: TimeInterval) {
        guard isVisible, !closing else { return }
        guard duration > 0 else { orderOut(nil); return }
        transition += 1
        let generation = transition
        closing = true
        NSAnimationContext.runAnimationGroup { context in
            context.duration = duration
            animator().alphaValue = 0
        } completionHandler: { [weak self] in
            Task { @MainActor in
                guard let self, self.transition == generation else { return }
                self.closing = false
                self.orderOut(nil)
                self.alphaValue = 1
            }
        }
    }

    override func cancelOperation(_ sender: Any?) {
        orderOut(sender)
    }
}

@MainActor
final class LumiBarController: NSObject, NSApplicationDelegate {
    private let preview = ProcessInfo.processInfo.arguments.contains("--preview")
    private let reference = ReferenceRequest.current
    private let logger = Logger(subsystem: "com.lumibar.macos", category: "lifecycle")
    private var previewDomain: String?
    private lazy var defaults: UserDefaults = {
        // The preview never reads or writes the user's preferences.
        if preview || reference != nil {
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
    private let musicStatus = NSStatusBar.system.statusItem(withLength: NSStatusItem.variableLength)
    private var weatherStatusContent: StatusContent<WeatherCapsule>?
    private var musicStatusContent: StatusContent<MusicView>?
    private let expandedPanel = SurfacePanel()
    private var settings: SurfacePanel?
    private var timer: Timer?
    private var weatherTimer: Timer?
    private var globalClicks: Any?
    private var localClicks: Any?
    private var pointedAtWeather: Date?
    private var weatherLeaveAt: Date?
    private var weatherLayoutGeneration = 0
    private var weatherHeightLimit: CGFloat = 0
    private var weatherNaturalHeight: CGFloat?
    private var previewTime: Date?
    private var weatherPinned = false
    private var movingAwayAt: Date?
    private var musicHoverStartsAt: Date?
    private var wasAtMusic = false
    private var musicDismissedUntilExit = false
    private var backgrounded = false
    private var musicScreen: NSScreen? { NSScreen.screens.first }
    private var motion: Bool { appearance.animation && !NSWorkspace.shared.accessibilityDisplayShouldReduceMotion }
    private var capsuleHeight: CGFloat { max(12, min(34, NSStatusBar.system.thickness - 4)) }

    func applicationDidFinishLaunching(_ notification: Notification) {
        if let reference {
            status.isVisible = false
            musicStatus.isVisible = false
            showReference(reference)
            return
        }
        status.button?.target = self
        status.button?.action = #selector(weatherClicked)
        status.button?.sendAction(on: [.leftMouseUp, .rightMouseUp])
        status.button?.setAccessibilityLabel("LumiBar 天气")
        musicStatus.button?.target = self
        musicStatus.button?.action = #selector(musicClicked)
        musicStatus.isVisible = false
        weather.onChange = { [weak self] in self?.updateStatus() }
        appearance.onEntryChange = { [weak self] in self?.updateStatus() }
        appearance.onThemeChange = { [weak self] in
            guard let self else { return }
            weatherPanel.appearance = appearance.windowAppearance
            expandedPanel.appearance = appearance.windowAppearance
            settings?.appearance = appearance.windowAppearance
            musicStatus.button?.appearance = appearance.windowAppearance
            updateStatus()
        }
        music.onTrackChange = { [weak self] in self?.layoutMusic() }
        expandedPanel.contentView = NSHostingView(rootView: MusicView(model: music, appearance: appearance,
            surface: .expanded) { [weak self] in self?.toggleLyrics() })

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
        timer = Timer(timeInterval: 0.04, target: self, selector: #selector(pointerTick), userInfo: nil, repeats: true)
        if let timer { RunLoop.main.add(timer, forMode: .common) }
        weatherTimer = Timer.scheduledTimer(timeInterval: 20 * 60, target: self,
                                            selector: #selector(refreshWeather), userInfo: nil, repeats: true)

        let arguments = ProcessInfo.processInfo.arguments
        if preview {
            previewTime = try? PreviewData.install(weather: weather, music: music, appearance: appearance, scene: .weather)
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

    private var statusFrame: NSRect? { entryFrame(status) }
    private var musicFrame: NSRect? { entryFrame(musicStatus) }

    private func entryFrame(_ item: NSStatusItem) -> NSRect? {
        guard item.isVisible, let button = item.button, let window = button.window else { return nil }
        let anchor = window.convertToScreen(button.convert(button.bounds, to: nil))
        let center = NSPoint(x: anchor.midX, y: anchor.midY)
        guard anchor.width > 0, NSScreen.screens.contains(where: { screen in
            screen.frame.contains(center) && screen.frame.maxY - center.y < max(48, screen.safeAreaInsets.top + 12)
        }) else { return nil }
        return anchor
    }

    private func placeInStatus<V: View>(_ content: StatusContent<V>, button: NSStatusBarButton) {
        content.sizingOptions = []
        content.frame = button.bounds
        content.autoresizingMask = [.width, .height]
        button.image = nil
        button.title = ""
        button.addSubview(content)
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
        let capsule = WeatherCapsule(model: weather, appearance: appearance, lite: appearance.weatherLite, height: capsuleHeight)
        status.length = capsule.idealWidth + 4
        button.appearance = appearance.windowAppearance
        if let content = weatherStatusContent { content.rootView = capsule }
        else {
            let content = StatusContent(rootView: capsule)
            placeInStatus(content, button: button)
            weatherStatusContent = content
        }
        if let forecast = weather.weather {
            let condition = forecast.conditionText ?? Weather.appearance(forecast.code, day: forecast.isDay).name
            let rain = forecast.rain.map { "，降水概率 " + $0.formatted(.number.precision(.fractionLength(0))) + "%" } ?? ""
            button.setAccessibilityLabel("LumiBar 天气，\(condition)，\(weather.temperature(forecast.temperature))\(rain)")
        } else {
            button.setAccessibilityLabel("LumiBar 天气")
        }
    }

    @objc private func musicClicked() { toggleLyrics() }

    @objc private func weatherClicked(_ sender: NSStatusBarButton) {
        if NSApp.currentEvent?.type == .rightMouseUp {
            weatherPinned = false
            weatherPanel.dismiss(duration: motion ? 0.18 : 0)
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
        else { weatherPanel.dismiss(duration: motion ? 0.18 : 0) }
    }

    private func showWeather(anchor: NSRect, compact: Bool) {
        guard let screen = NSScreen.screens.first(where: { $0.frame.contains(.init(x: anchor.midX, y: anchor.midY)) })
                ?? musicScreen else { return }
        let width = min(compact ? 296 : 512, screen.visibleFrame.width)
        let maxHeight = max(180, anchor.minY - screen.visibleFrame.minY + 12)
        let initial = compact ? 192 : weatherNaturalHeight ?? (weather.weather == nil ? 275 : 742)
        let height = min(CGFloat(initial), maxHeight)
        weatherHeightLimit = maxHeight
        weatherLayoutGeneration += 1
        let generation = weatherLayoutGeneration
        let x = max(screen.visibleFrame.minX, min(anchor.midX - width / 2, screen.visibleFrame.maxX - width))
        weatherPanel.appearance = appearance.windowAppearance
        let view = NSHostingView(rootView: WeatherView(model: weather, appearance: appearance, compact: compact,
            width: width, height: nil, referenceDate: previewTime, measured: { [weak self] value in
                Task { @MainActor in self?.resizeWeather(to: value, generation: generation) }
            }, showSettings: { [weak self] in self?.showSettings() },
            quit: { [weak self] in self?.quit() }))
        view.sizingOptions = []
        weatherPanel.contentView = view
        weatherPanel.ignoresMouseEvents = false
        weatherPanel.present(NSRect(x: x, y: anchor.minY + 12 - height, width: width, height: height), key: !compact)
    }

    private func resizeWeather(to desired: CGFloat, generation: Int) {
        guard generation == weatherLayoutGeneration, desired.isFinite, weatherPanel.isVisible else { return }
        weatherNaturalHeight = max(174, ceil(desired))
        let old = weatherPanel.frame
        let height = min(weatherHeightLimit, weatherNaturalHeight ?? old.height)
        guard abs(old.height - height) > 1 else { return }
        let frame = NSRect(x: old.minX, y: old.maxY - height, width: old.width, height: height)
        NSAnimationContext.runAnimationGroup { context in
            context.duration = motion ? (height > old.height ? 0.28 : 0.22) : 0
            context.timingFunction = CAMediaTimingFunction(name: .easeInEaseOut)
            weatherPanel.animator().setFrame(frame, display: true)
        }
    }

    private func outsideClick() {
        let pointer = NSEvent.mouseLocation
        if weatherPinned, weatherPanel.isVisible, !weatherPanel.frame.contains(pointer),
           statusFrame?.contains(pointer) != true {
            weatherPinned = false
            weatherPanel.dismiss(duration: motion ? 0.18 : 0)
        }
        if music.mode == .lyrics, musicFrame?.contains(pointer) != true, !expandedPanel.frame.contains(pointer) {
            music.mode = .compact
            layoutMusic()
        }
    }

    @objc private func pointerTick() {
        let pointer = NSEvent.mouseLocation
        let now = Date.now
        if weatherPinned && !weatherPanel.isVisible { weatherPinned = false }
        if !weatherPinned, let anchor = statusFrame {
            if anchor.contains(pointer) {
                weatherLeaveAt = nil
                if weatherPanel.closing { weatherPanel.present(weatherPanel.frame) }
                if let began = pointedAtWeather {
                    if now.timeIntervalSince(began) >= 0.32 && !weatherPanel.isVisible {
                        showWeather(anchor: anchor, compact: true)
                    }
                } else {
                    pointedAtWeather = now
                }
            } else if weatherPanel.isVisible && weatherPanel.frame.contains(pointer) {
                if weatherPanel.closing { weatherPanel.present(weatherPanel.frame) }
                pointedAtWeather = nil
                weatherLeaveAt = nil
            } else {
                pointedAtWeather = nil
                if weatherPanel.isVisible {
                    if let left = weatherLeaveAt, now.timeIntervalSince(left) >= 0.35 {
                        weatherPanel.dismiss(duration: motion ? 0.12 : 0)
                    } else if weatherLeaveAt == nil {
                        weatherLeaveAt = now
                    }
                }
            }
        }

        guard music.track != nil, !backgrounded else { return }
        let hovering = musicFrame?.contains(pointer) == true
            || (expandedPanel.isVisible && expandedPanel.frame.insetBy(dx: -2, dy: -4).contains(pointer))
        if hovering && !wasAtMusic { musicHoverStartsAt = now }
        if !hovering {
            musicHoverStartsAt = nil
            musicDismissedUntilExit = false
        }
        wasAtMusic = hovering
        if music.mode == .compact, hovering, !musicDismissedUntilExit,
           let began = musicHoverStartsAt, now.timeIntervalSince(began) >= 0.32 {
            music.mode = .controls
            layoutMusic()
        } else if music.mode == .controls, !hovering {
            if let began = movingAwayAt, now.timeIntervalSince(began) > 0.35 {
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
        guard let track = music.track, !backgrounded else {
            musicStatus.isVisible = false
            expandedPanel.dismiss(duration: motion ? 0.18 : 0)
            return
        }
        let entry = MusicView(model: music, appearance: appearance, surface: .entry(capsuleHeight)) { }
        musicStatus.length = 63
        musicStatus.isVisible = true
        guard let button = musicStatus.button else { return }
        button.appearance = appearance.windowAppearance
        if let content = musicStatusContent { content.rootView = entry }
        else {
            let content = StatusContent(rootView: entry)
            placeInStatus(content, button: button)
            musicStatusContent = content
        }
        button.setAccessibilityLabel("LumiBar 音乐，\(track.title)，\(track.artist)")
        guard music.mode != .compact else {
            expandedPanel.dismiss(duration: motion ? 0.18 : 0)
            return
        }
        let anchor = musicFrame
        let screen = anchor.flatMap { rect in NSScreen.screens.first { $0.frame.contains(.init(x: rect.midX, y: rect.midY)) } } ?? musicScreen
        guard let screen else { return }
        let lyrics = music.mode == .lyrics
        let width = min(lyrics ? 452 : 342, screen.visibleFrame.width)
        let menuBottom = min(screen.frame.maxY - screen.safeAreaInsets.top, screen.visibleFrame.maxY)
        let top = (anchor?.minY ?? menuBottom) + (lyrics ? 11 : -3)
        let height = lyrics ? min(682, max(220, top - screen.visibleFrame.minY - 5)) : 78
        let center = lyrics ? screen.frame.midX : anchor?.midX ?? screen.frame.midX
        let x = max(screen.visibleFrame.minX, min(center - width / 2, screen.visibleFrame.maxX - width))
        expandedPanel.appearance = appearance.windowAppearance
        expandedPanel.present(.init(x: x, y: top - height, width: width, height: height))
    }

    @objc private func screensChanged() {
        weatherPanel.orderOut(nil)
        weatherPinned = false
        updateStatus()
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
        weatherPanel.dismiss(duration: motion ? 0.18 : 0)
        if settings == nil {
            let window = SurfacePanel()
            window.level = .floating
            window.appearance = appearance.windowAppearance
            window.isMovableByWindowBackground = true
            window.title = "LumiBar 设置"
            window.contentView = NSHostingView(rootView: SettingsView(appearance: appearance, weather: weather, music: music,
                close: { [weak self] in self?.closeSettings() }))
            window.setContentSize(NSSize(width: 592, height: 682))
            window.center()
            settings = window
        }
        NSApp.activate()
        if let settings { settings.present(settings.frame, key: true) }
    }

    private func closeSettings() { settings?.dismiss(duration: motion ? 0.18 : 0) }

    private func showReference(_ request: ReferenceRequest) {
        do {
            previewTime = try PreviewData.install(weather: weather, music: music, appearance: appearance, scene: request.scene)
        } catch {
            logger.error("Could not open visual fixture: \(error.localizedDescription)")
            NSApp.terminate(nil)
            return
        }
        appearance.theme = request.dark ? "Dark" : "Light"
        music.mode = .lyrics
        let size = request.scene.size
        let backdrop = AccentPalette.color(request.dark ? 0x171922 : 0xF3F5FA)
        let content: AnyView
        switch request.scene {
        case .music:
            content = AnyView(MusicView(model: music, appearance: appearance, surface: .expanded) { })
        case .settings:
            content = AnyView(SettingsView(appearance: appearance, weather: weather, music: music) { NSApp.terminate(nil) })
        case .capsule, .lite, .wide:
            content = AnyView(WeatherCapsule(model: weather, appearance: appearance, lite: request.scene == .lite, height: 34))
        case .weather, .hover, .alerts:
            content = AnyView(WeatherView(model: weather, appearance: appearance, compact: request.scene == .hover,
                width: size.width, height: size.height, referenceDate: previewTime, showSettings: {}, quit: {}))
        }
        let panel = SurfacePanel()
        panel.title = "LumiBar Reference \(request.scene.rawValue)"
        panel.level = .floating
        panel.appearance = appearance.windowAppearance
        panel.contentView = NSHostingView(rootView: content.frame(width: size.width, height: size.height).background(backdrop)
            .environment(\.colorScheme, request.dark ? .dark : .light))
        panel.setContentSize(size)
        panel.center()
        settings = panel
        NSApp.activate()
        panel.present(panel.frame, key: true)
    }

    @objc private func quit() { NSApp.terminate(nil) }
}

