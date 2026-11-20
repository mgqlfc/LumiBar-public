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
    var onCancel: (() -> Void)?
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
        if let onCancel { onCancel() }
        else { orderOut(sender) }
    }
}

private final class CapsuleInputView: NSView {
    var capsuleAt: ((NSPoint) -> NSRect?)?
    var onRelease: ((NSEvent.EventType, NSPoint, NSPoint) -> Void)?
    private var press: (button: Int, point: NSPoint, capsule: NSRect)?
    private lazy var pressShade: CAShapeLayer = {
        let shade = CAShapeLayer()
        shade.fillColor = NSColor.black.cgColor
        shade.opacity = 0
        wantsLayer = true
        layer?.addSublayer(shade)
        return shade
    }()

    override func viewDidMoveToWindow() {
        super.viewDidMoveToWindow()
        if window != nil { _ = pressShade }
    }

    override func acceptsFirstMouse(for event: NSEvent?) -> Bool { true }
    override func mouseDown(with event: NSEvent) { begin(event) }
    override func rightMouseDown(with event: NSEvent) { begin(event) }
    override func mouseDragged(with event: NSEvent) { drag(event) }
    override func rightMouseDragged(with event: NSEvent) { drag(event) }
    override func mouseUp(with event: NSEvent) { end(event) }
    override func rightMouseUp(with event: NSEvent) { end(event) }

    private func begin(_ event: NSEvent) {
        guard let window else { return }
        let point = window.convertPoint(toScreen: event.locationInWindow)
        guard let capsule = capsuleAt?(point) else { return }
        press = (event.buttonNumber, point, capsule)
        shade(capsule)
    }

    private func drag(_ event: NSEvent) {
        guard let press, press.button == event.buttonNumber, let window else { return }
        let point = window.convertPoint(toScreen: event.locationInWindow)
        shade(capsuleAt?(point) == press.capsule ? press.capsule : nil)
    }

    private func end(_ event: NSEvent) {
        guard let press, press.button == event.buttonNumber, let window else { return }
        self.press = nil
        shade(nil, fade: true)
        onRelease?(event.type, press.point, window.convertPoint(toScreen: event.locationInWindow))
    }

    func cancelPress() {
        guard press != nil else { return }
        press = nil
        shade(nil)
    }

    private func shade(_ capsule: NSRect?, fade: Bool = false) {
        let shade = pressShade
        let startOpacity = shade.opacity
        CATransaction.begin()
        CATransaction.setDisableActions(true)
        shade.removeAnimation(forKey: "opacity")
        if let capsule, let window {
            let local = convert(window.convertFromScreen(capsule), from: nil)
            shade.path = MenuBarCapsuleShape.outline(height: local.height).path(in: local).cgPath
            shade.opacity = 0.18
        } else {
            shade.opacity = 0
        }
        CATransaction.commit()
        if fade, startOpacity > 0, !NSWorkspace.shared.accessibilityDisplayShouldReduceMotion {
            let fadeOut = CABasicAnimation(keyPath: "opacity")
            fadeOut.fromValue = startOpacity
            fadeOut.toValue = 0
            fadeOut.duration = 0.16
            fadeOut.timingFunction = CAMediaTimingFunction(name: .easeOut)
            shade.add(fadeOut, forKey: "opacity")
        }
    }
}

@MainActor
final class LumiBarController: NSObject, NSApplicationDelegate {
    private let preview = ProcessInfo.processInfo.arguments.contains("--preview")
    private let checkMenuBar = ProcessInfo.processInfo.arguments.contains("--check-menu-bar")
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
    private let capsuleGap: CGFloat = 4
    private var capsuleInputFrame: NSRect?
    // The native item supplies layout and keyboard access. This panel receives pointer input without its press backdrop.
    private lazy var capsuleInputPanel: NSPanel = {
        let panel = NSPanel(contentRect: .zero, styleMask: [.borderless, .nonactivatingPanel],
                            backing: .buffered, defer: false)
        panel.isOpaque = false
        panel.backgroundColor = NSColor.black.withAlphaComponent(0.01)
        panel.hasShadow = false
        panel.isMovable = false
        panel.hidesOnDeactivate = false
        panel.level = NSWindow.Level(rawValue: NSWindow.Level.statusBar.rawValue + 1)
        panel.collectionBehavior = [.moveToActiveSpace, .fullScreenAuxiliary, .ignoresCycle]
        let view = CapsuleInputView()
        view.capsuleAt = { [weak self] point in self?.capsuleFrame(at: point) }
        view.onRelease = { [weak self] type, start, end in
            self?.statusPointerReleased(type, at: end, startedAt: start)
        }
        panel.contentView = view
        return panel
    }()
    private var menuBarWidth: CGFloat { MusicView.entryWidth * 2 + capsuleGap }
    private var showsMusicCapsule: Bool { music.track?.playing == true && !backgrounded }
    private var weatherStatusImage: NSImage?
    private var musicStatusImage: NSImage?
    private var lastMusicImageAt = Date.distantPast
    private var lastMusicBars: [Int] = []
    private var lastMusicArt: NSImage?
    private var lastMusicScheme: ColorScheme?
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
    private var weatherDismissedUntilExit = false
    private var movingAwayAt: Date?
    private var musicHoverStartsAt: Date?
    private var wasAtMusic = false
    private var musicDismissedUntilExit = false
    private var backgrounded = false
    private var musicScreen: NSScreen? { NSScreen.screens.first }
    private var motion: Bool { appearance.animation && !NSWorkspace.shared.accessibilityDisplayShouldReduceMotion }
    private let capsuleHeight: CGFloat = 26

    func applicationDidFinishLaunching(_ notification: Notification) {
        if let reference {
            status.isVisible = false
            showReference(reference)
            return
        }
        status.button?.target = self
        status.button?.action = #selector(statusClicked)
        status.button?.sendAction(on: [.leftMouseUp, .rightMouseUp])
        status.button?.isBordered = false
        status.button?.imageScaling = .scaleNone
        (status.button?.cell as? NSButtonCell)?.highlightsBy = []
        status.button?.setAccessibilityLabel("LumiBar 天气")
        weatherPanel.onCancel = { [weak self] in self?.dismissWeather() }
        expandedPanel.onCancel = { [weak self] in self?.dismissMusic() }
        weather.onChange = { [weak self] in self?.updateStatus() }
        appearance.onEntryChange = { [weak self] in self?.updateStatus() }
        appearance.onThemeChange = { [weak self] in
            guard let self else { return }
            weatherPanel.appearance = appearance.windowAppearance
            expandedPanel.appearance = appearance.windowAppearance
            settings?.appearance = appearance.windowAppearance
            updateStatus()
        }
        music.onTrackChange = { [weak self] in
            guard let self else { return }
            updateStatus()
            layoutMusic()
        }
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
        if checkMenuBar {
            Task { @MainActor [weak self] in
                try? await Task.sleep(for: .milliseconds(750))
                self?.reportStatusItems()
            }
        }
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
        guard let group = statusImageFrame, let image = weatherStatusImage else { return entryFrame(status) }
        return NSRect(x: group.maxX - image.size.width, y: group.minY,
                      width: image.size.width, height: group.height)
    }

    private var musicFrame: NSRect? {
        guard let image = musicStatusImage, let group = statusImageFrame else { return nil }
        return NSRect(x: group.minX, y: group.minY, width: image.size.width, height: group.height)
    }

    private var statusImageFrame: NSRect? {
        guard let frame = entryFrame(status), let image = status.button?.image else { return nil }
        let size = image.size
        return NSRect(x: frame.midX - size.width / 2, y: frame.midY - size.height / 2,
                      width: size.width, height: size.height)
    }

    private func capsuleContains(_ pointer: NSPoint, in frame: NSRect?) -> Bool {
        guard let frame, frame.contains(pointer) else { return false }
        return MenuBarCapsuleShape.outline(height: frame.height).path(in: frame).contains(pointer)
    }

    private func capsuleFrame(at pointer: NSPoint) -> NSRect? {
        if let musicFrame, capsuleContains(pointer, in: musicFrame) { return musicFrame }
        if let statusFrame, capsuleContains(pointer, in: statusFrame) { return statusFrame }
        return nil
    }

    private func syncCapsuleInputPanel() {
        let input = capsuleInputPanel.contentView as? CapsuleInputView
        guard let frame = statusImageFrame, status.button?.window?.isVisible == true, !backgrounded else {
            input?.cancelPress()
            capsuleInputFrame = nil
            if capsuleInputPanel.isVisible { capsuleInputPanel.orderOut(nil) }
            return
        }
        if capsuleInputFrame != frame {
            input?.cancelPress()
            capsuleInputFrame = frame
            capsuleInputPanel.setFrame(frame, display: true)
        }
        let reordering = NSEvent.modifierFlags.contains(.command)
        if capsuleInputPanel.ignoresMouseEvents != reordering { capsuleInputPanel.ignoresMouseEvents = reordering }
        if !capsuleInputPanel.isVisible { capsuleInputPanel.orderFrontRegardless() }
    }

    private func entryFrame(_ item: NSStatusItem) -> NSRect? {
        guard item.isVisible, let button = item.button, let window = button.window else { return nil }
        let anchor = window.convertToScreen(button.convert(button.bounds, to: nil))
        let center = NSPoint(x: anchor.midX, y: anchor.midY)
        guard anchor.width > 0, NSScreen.screens.contains(where: { screen in
            screen.frame.contains(center) && screen.frame.maxY - center.y < max(48, screen.safeAreaInsets.top + 12)
        }) else { return nil }
        return anchor
    }

    private func buttonScheme(_ button: NSStatusBarButton) -> ColorScheme {
        button.effectiveAppearance.bestMatch(from: [.aqua, .darkAqua]) == .darkAqua ? .dark : .light
    }

    private func statusImage<V: View>(_ view: V, button: NSStatusBarButton, size: CGSize) -> NSImage? {
        let renderer = ImageRenderer(content: view.frame(width: size.width, height: size.height)
            .environment(\.colorScheme, buttonScheme(button)))
        renderer.scale = button.window?.backingScaleFactor ?? NSScreen.main?.backingScaleFactor ?? 2
        let image = renderer.nsImage
        image?.isTemplate = false
        return image
    }

    private func reportStatusItems() {
        let top = NSScreen.screens.first?.frame.maxY ?? 0
        for (name, frame, image) in [("weather", statusFrame, weatherStatusImage), ("music", musicFrame, musicStatusImage)] {
            guard let frame, let image else {
                logger.notice("Menu bar \(name, privacy: .public): no active item")
                continue
            }
            logger.notice("Menu bar \(name, privacy: .public): rect \(Int(frame.minX)),\(Int(top - frame.maxY)),\(Int(frame.width)),\(Int(frame.height)) image \(Int(image.size.width))×\(Int(image.size.height))")
        }
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
        let paired = showsMusicCapsule
        let weatherWidth = paired ? MusicView.entryWidth : menuBarWidth
        let capsule = WeatherCapsule(model: weather, appearance: appearance, height: capsuleHeight,
                                     width: weatherWidth, showsPrecipitation: !paired)
        button.appearance = appearance.windowAppearance
        button.imagePosition = .imageOnly
        button.title = ""
        weatherStatusImage = statusImage(capsule, button: button, size: CGSize(width: weatherWidth, height: capsuleHeight))
        updateMusicStatusImage(force: true)
        updateStatusAccessibility()
    }

    private func displayStatusImages() {
        guard let button = status.button else { return }
        if let weatherStatusImage {
            let musicImage = musicStatusImage
            let weatherX = musicImage.map { $0.size.width + capsuleGap } ?? 0
            let size = CGSize(width: weatherX + weatherStatusImage.size.width, height: capsuleHeight)
            if status.length != size.width { status.length = size.width }
            button.image = NSImage(size: size, flipped: false) { _ in
                musicImage?.draw(in: NSRect(origin: .zero, size: musicImage?.size ?? .zero))
                weatherStatusImage.draw(in: NSRect(x: weatherX, y: 0, width: weatherStatusImage.size.width, height: size.height))
                return true
            }
        } else {
            status.length = NSStatusItem.squareLength
            let image = NSImage(systemSymbolName: "cloud.sun", accessibilityDescription: nil)
            image?.isTemplate = true
            button.image = image
        }
    }

    private func updateStatusAccessibility() {
        guard let button = status.button else { return }
        var label = "LumiBar 天气"
        if let forecast = weather.weather {
            let condition = forecast.conditionText ?? Weather.appearance(forecast.code, day: forecast.isDay).name
            label += "，\(condition)，\(weather.temperature(forecast.temperature))"
            if musicStatusImage == nil, let rain = forecast.rain {
                label += "，降水概率 " + rain.formatted(.number.precision(.fractionLength(0))) + "%"
            }
        }
        if musicStatusImage != nil, let track = music.track {
            label += "，音乐，\(track.title)，\(track.artist)"
            button.setAccessibilityCustomActions([
                NSAccessibilityCustomAction(name: "开关音乐详情", target: self, selector: #selector(toggleAccessibleMusic))
            ])
        } else {
            button.setAccessibilityCustomActions([])
        }
        button.setAccessibilityLabel(label)
    }

    @objc private func toggleAccessibleMusic() -> Bool {
        guard music.track != nil else { return false }
        toggleLyrics()
        return true
    }

    private func showStatusMenu(_ sender: NSStatusBarButton) {
        dismissWeather()
        dismissMusic()
        let menu = NSMenu()
        menu.addItem(withTitle: "LumiBar 设置…", action: #selector(showSettings), keyEquivalent: ",").target = self
        menu.addItem(.separator())
        menu.addItem(withTitle: "退出 LumiBar", action: #selector(quit), keyEquivalent: "q").target = self
        menu.popUp(positioning: nil, at: NSPoint(x: 0, y: sender.bounds.height), in: sender)
    }

    @objc private func statusClicked(_ sender: NSStatusBarButton) {
        if let type = NSApp.currentEvent?.type, type == .leftMouseUp || type == .rightMouseUp {
            statusPointerReleased(type, at: NSEvent.mouseLocation)
        } else {
            toggleWeather(at: NSEvent.mouseLocation)
        }
    }

    private func statusPointerReleased(_ type: NSEvent.EventType, at pointer: NSPoint, startedAt start: NSPoint? = nil) {
        let origin = start ?? pointer
        let musicAnchor = musicFrame
        let weatherAnchor = statusFrame
        let overMusic = capsuleContains(pointer, in: musicAnchor) && capsuleContains(origin, in: musicAnchor)
        let overWeather = capsuleContains(pointer, in: weatherAnchor) && capsuleContains(origin, in: weatherAnchor)
        guard overMusic || overWeather else { return }
        if type == .rightMouseUp, let button = status.button {
            showStatusMenu(button)
        } else if overMusic {
            toggleLyrics()
        } else {
            toggleWeather(at: pointer)
        }
    }

    private func toggleWeather(at cursor: NSPoint) {
        if weatherPinned && weatherPanel.isVisible {
            dismissWeather()
            return
        }
        dismissMusic()
        let anchor = statusFrame ?? NSRect(x: cursor.x - 10, y: cursor.y - capsuleHeight / 2, width: 20, height: capsuleHeight)
        weatherPinned = true
        weatherDismissedUntilExit = false
        pointedAtWeather = nil
        weatherLeaveAt = nil
        showWeather(anchor: anchor, compact: false)
        if checkMenuBar { logger.notice("Menu bar weather detail opened") }
    }

    private func dismissWeather() {
        if checkMenuBar && weatherPinned { logger.notice("Menu bar weather detail closed") }
        weatherPinned = false
        weatherDismissedUntilExit = true
        pointedAtWeather = nil
        weatherLeaveAt = nil
        weatherPanel.dismiss(duration: motion ? 0.18 : 0)
    }

    private func showWeather(anchor: NSRect, compact: Bool) {
        guard let screen = NSScreen.screens.first(where: { $0.frame.contains(.init(x: anchor.midX, y: anchor.midY)) })
                ?? musicScreen else { return }
        let width = min(compact ? 296 : 512, screen.visibleFrame.width)
        let maxHeight = max(180, anchor.minY - screen.visibleFrame.minY)
        let initial = compact ? 192 : weatherNaturalHeight ?? (weather.weather == nil ? 275 : 742)
        let height = min(CGFloat(initial), maxHeight)
        weatherHeightLimit = maxHeight
        weatherLayoutGeneration += 1
        let generation = weatherLayoutGeneration
        let x = max(screen.visibleFrame.minX, min(anchor.midX - width / 2, screen.visibleFrame.maxX - width))
        weatherPanel.appearance = appearance.windowAppearance
        weatherPanel.title = compact ? "LumiBar 天气速览" : "LumiBar 天气详情"
        let view = NSHostingView(rootView: WeatherView(model: weather, appearance: appearance, compact: compact,
            width: width, height: nil, referenceDate: previewTime, measured: { [weak self] value in
                Task { @MainActor in self?.resizeWeather(to: value, generation: generation) }
            }, showSettings: { [weak self] in self?.showSettings() },
            quit: { [weak self] in self?.quit() }))
        view.sizingOptions = []
        weatherPanel.contentView = view
        weatherPanel.ignoresMouseEvents = false
        // The transparent edge of the panel still receives mouse events.
        weatherPanel.present(NSRect(x: x, y: anchor.minY - height, width: width, height: height), key: !compact)
    }

    private func resizeWeather(to desired: CGFloat, generation: Int) {
        guard generation == weatherLayoutGeneration, desired.isFinite, weatherPanel.isVisible, !weatherPanel.closing else { return }
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
        guard !capsuleContains(pointer, in: statusFrame), !capsuleContains(pointer, in: musicFrame) else { return }
        if weatherPinned, weatherPanel.isVisible, !weatherPanel.frame.contains(pointer),
           !weatherPanel.closing {
            dismissWeather()
        }
        if music.mode == .lyrics, !expandedPanel.frame.contains(pointer) {
            dismissMusic()
        }
    }

    @objc private func pointerTick() {
        syncCapsuleInputPanel()
        let pointer = NSEvent.mouseLocation
        let now = Date.now
        if weatherPinned && !weatherPanel.isVisible { weatherPinned = false }
        let anchor = statusFrame
        let overWeather = capsuleContains(pointer, in: anchor)
        if weatherDismissedUntilExit, !overWeather,
           !weatherPanel.isVisible || !weatherPanel.frame.contains(pointer) {
            weatherDismissedUntilExit = false
        }
        if music.mode == .compact, !weatherPinned, !weatherDismissedUntilExit, let anchor {
            if overWeather {
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
        updateMusicStatusImage()
        let hovering = capsuleContains(pointer, in: musicFrame)
            || (expandedPanel.isVisible && expandedPanel.frame.insetBy(dx: -2, dy: -4).contains(pointer))
        if hovering && !wasAtMusic { musicHoverStartsAt = now }
        if !hovering {
            musicHoverStartsAt = nil
            musicDismissedUntilExit = false
        }
        wasAtMusic = hovering
        if !weatherPinned, music.mode == .compact, hovering, !musicDismissedUntilExit,
           let began = musicHoverStartsAt, now.timeIntervalSince(began) >= 0.32 {
            dismissWeather()
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
            dismissMusic()
        }
    }

    private func toggleLyrics() {
        if music.mode == .lyrics {
            dismissMusic()
        } else {
            dismissWeather()
            music.mode = .lyrics
            layoutMusic()
            if checkMenuBar { logger.notice("Menu bar music detail opened") }
        }
    }

    private func dismissMusic() {
        if checkMenuBar && music.mode == .lyrics { logger.notice("Menu bar music detail closed") }
        music.mode = .compact
        musicDismissedUntilExit = true
        layoutMusic()
    }

    private func updateMusicStatusImage(force: Bool = false) {
        guard showsMusicCapsule, let button = status.button else {
            if force || musicStatusImage != nil {
                musicStatusImage = nil
                displayStatusImages()
            }
            return
        }
        let now = Date.now
        guard force || now.timeIntervalSince(lastMusicImageAt) > 0.09 else { return }
        lastMusicImageAt = now
        let levels = music.previewSpectrum ?? (music.spectrumEnabled
            && !NSWorkspace.shared.accessibilityDisplayShouldReduceMotion ? music.spectrum.levels : [])
        let bandCount = 5
        let bands = (0..<bandCount).map { index -> Int in
            guard !levels.isEmpty else { return 0 }
            let sample = levels[index * levels.count / bandCount]
            return Int((min(1, max(0, sample)) * 11).rounded())
        }
        let scheme = buttonScheme(button)
        guard force || bands != lastMusicBars || music.artwork !== lastMusicArt || scheme != lastMusicScheme
            || musicStatusImage == nil else { return }
        lastMusicBars = bands
        lastMusicArt = music.artwork
        lastMusicScheme = scheme
        let entry = MusicView(model: music, appearance: appearance, surface: .entry(capsuleHeight, bands)) { }
        musicStatusImage = statusImage(entry, button: button, size: CGSize(width: MusicView.entryWidth, height: capsuleHeight))
        displayStatusImages()
    }

    private func layoutMusic() {
        guard music.track != nil, !backgrounded else {
            music.mode = .compact
            expandedPanel.dismiss(duration: motion ? 0.18 : 0)
            return
        }
        guard music.mode != .compact else {
            expandedPanel.dismiss(duration: motion ? 0.18 : 0)
            return
        }
        let anchor = musicFrame ?? statusFrame
        let screen = anchor.flatMap { rect in NSScreen.screens.first { $0.frame.contains(.init(x: rect.midX, y: rect.midY)) } } ?? musicScreen
        guard let screen else { return }
        let lyrics = music.mode == .lyrics
        let width = min(lyrics ? 452 : 342, screen.visibleFrame.width)
        let menuBottom = min(screen.frame.maxY - screen.safeAreaInsets.top, screen.visibleFrame.maxY)
        let top = (anchor?.minY ?? menuBottom) - (lyrics ? 0 : 3)
        let height = lyrics ? min(682, max(220, top - screen.visibleFrame.minY - 5)) : 78
        let center = anchor?.midX ?? screen.frame.midX
        let x = max(screen.visibleFrame.minX, min(center - width / 2, screen.visibleFrame.maxX - width))
        expandedPanel.appearance = appearance.windowAppearance
        expandedPanel.title = lyrics ? "LumiBar 音乐详情" : "LumiBar 播放控制"
        expandedPanel.present(.init(x: x, y: top - height, width: width, height: height))
    }

    @objc private func screensChanged() {
        dismissWeather()
        updateStatus()
        layoutMusic()
    }

    @objc private func spaceChanged() {
        dismissWeather()
        if !preview { dismissMusic() }
        else { layoutMusic() }
    }

    @objc private func sleeping() {
        backgrounded = true
        syncCapsuleInputPanel()
        music.spectrum.stop()
        updateStatus()
        layoutMusic()
    }

    @objc private func woke() {
        backgrounded = false
        if music.spectrumEnabled { music.restartSpectrum() }
        updateStatus()
        layoutMusic()
        refreshWeather()
    }

    @objc private func showSettings() {
        dismissWeather()
        dismissMusic()
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
            let narrow = request.scene == .lite
            content = AnyView(WeatherCapsule(model: weather, appearance: appearance, height: 34,
                width: narrow ? MusicView.entryWidth : menuBarWidth, showsPrecipitation: !narrow))
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

