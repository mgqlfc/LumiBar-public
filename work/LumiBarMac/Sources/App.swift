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

private let menuBarWindowBehavior: NSWindow.CollectionBehavior = [
    .canJoinAllSpaces, .canJoinAllApplications, .fullScreenAuxiliary, .ignoresCycle
]

private final class SurfacePanel: NSPanel {
    private var transition = 0
    private(set) var closing = false
    var onCancel: (() -> Void)?
    override var canBecomeKey: Bool { true }

    init(menuBar: Bool = false) {
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
        collectionBehavior = menuBar ? menuBarWindowBehavior : [.moveToActiveSpace, .fullScreenNone, .ignoresCycle]
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

private final class SettingsWindow: NSWindow {
    override func performKeyEquivalent(with event: NSEvent) -> Bool {
        let modifiers = event.modifierFlags.intersection([.command, .control, .option, .shift])
        if modifiers == .command, event.charactersIgnoringModifiers?.lowercased() == "w" {
            performClose(nil)
            return true
        }
        return super.performKeyEquivalent(with: event)
    }
}

private enum MenuBarCapsule { case weather, music }
private typealias CapsuleHit = (capsule: MenuBarCapsule, frame: NSRect)

private final class CapsuleInputView: NSView {
    var capsuleAt: ((NSPoint) -> CapsuleHit?)?
    var highlightForPress: ((Int, NSPoint, CapsuleHit) -> NSRect?)?
    var onRelease: ((NSEvent, NSPoint, NSPoint, MenuBarCapsule) -> Void)?
    var onPointerMove: (() -> Void)?
    var transitionImage: NSImage? {
        didSet { if oldValue !== transitionImage { needsDisplay = true } }
    }
    private var press: (button: Int, point: NSPoint, capsule: MenuBarCapsule)?
    private var pointerTrackingArea: NSTrackingArea?
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

    override func updateTrackingAreas() {
        if let pointerTrackingArea { removeTrackingArea(pointerTrackingArea) }
        let area = NSTrackingArea(rect: .zero, options: [.activeAlways, .inVisibleRect, .mouseEnteredAndExited, .mouseMoved],
                                  owner: self, userInfo: nil)
        addTrackingArea(area)
        pointerTrackingArea = area
        super.updateTrackingAreas()
    }

    override func mouseEntered(with event: NSEvent) { onPointerMove?() }
    override func mouseMoved(with event: NSEvent) { onPointerMove?() }
    override func mouseExited(with event: NSEvent) { onPointerMove?() }

    override func draw(_ dirtyRect: NSRect) {
        super.draw(dirtyRect)
        transitionImage?.draw(in: bounds)
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
        guard let hit = capsuleAt?(point),
              let highlight = highlightForPress?(event.buttonNumber, point, hit) else { return }
        press = (event.buttonNumber, point, hit.capsule)
        shade(highlight)
    }

    private func drag(_ event: NSEvent) {
        guard let press, press.button == event.buttonNumber, let window else { return }
        let point = window.convertPoint(toScreen: event.locationInWindow)
        let highlight = capsuleAt?(point).flatMap { hit in
            hit.capsule == press.capsule ? highlightForPress?(press.button, point, hit) : nil
        }
        shade(highlight)
        onPointerMove?()
    }

    private func end(_ event: NSEvent) {
        guard let press, press.button == event.buttonNumber, let window else { return }
        self.press = nil
        shade(nil, fade: true)
        onRelease?(event, press.point, window.convertPoint(toScreen: event.locationInWindow), press.capsule)
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
            shade.path = local.width == local.height
                ? CGPath(ellipseIn: local, transform: nil)
                : MenuBarCapsuleShape.outline(height: local.height).path(in: local).cgPath
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
    private lazy var status = NSStatusBar.system.statusItem(withLength: NSStatusItem.variableLength)
    private let weatherPanel = SurfacePanel(menuBar: true)
    private let weatherHoverPanel: SurfacePanel = {
        let panel = SurfacePanel(menuBar: true)
        panel.ignoresMouseEvents = true
        panel.contentView = nil
        return panel
    }()
    private let capsuleGap = MenuBarCapsuleShape.entryGap
    private var capsuleInputFrame: NSRect?
    private lazy var capsuleInputView: CapsuleInputView = {
        let view = CapsuleInputView()
        view.capsuleAt = { [weak self] point in self?.capsule(at: point) }
        view.highlightForPress = { [weak self] button, point, hit in
            guard let self else { return nil }
            guard button == 0, hit.capsule == .music else { return hit.frame }
            guard musicStatusImage != nil, canControlMusic, musicControlContains(point, in: hit.frame) else { return nil }
            return MusicView.entryControlFrame(in: hit.frame)
        }
        view.onRelease = { [weak self] event, start, end, capsule in
            self?.statusPointerReleased(event, at: end, startedAt: start, pressed: capsule)
        }
        view.onPointerMove = { [weak self] in self?.updateCapsuleHover() }
        return view
    }()
    // The native item supplies layout and keyboard access. The panel handles input and keeps the Lite transition anchored.
    private lazy var capsuleInputPanel: NSPanel = {
        let panel = NSPanel(contentRect: .zero, styleMask: [.borderless, .nonactivatingPanel],
                            backing: .buffered, defer: false)
        panel.isOpaque = false
        panel.backgroundColor = NSColor.black.withAlphaComponent(0.01)
        panel.hasShadow = false
        panel.isMovable = false
        panel.hidesOnDeactivate = false
        panel.acceptsMouseMovedEvents = true
        panel.level = NSWindow.Level(rawValue: NSWindow.Level.statusBar.rawValue + 1)
        panel.collectionBehavior = menuBarWindowBehavior
        panel.contentView = capsuleInputView
        return panel
    }()
    private var minimumMenuBarWidth: CGFloat { MusicView.entryWidth * 2 + capsuleGap }
    private var showsMusicCapsule: Bool { music.showMusic && music.track != nil && !backgrounded }
    private var usesLiteWeatherCapsule: Bool { weather.liteCapsule || showsMusicCapsule }
    private var wideWeatherImage: MenuBarCapsuleImage?
    private var narrowWeatherImage: MenuBarCapsuleImage?
    private var weatherStatusImage: MenuBarCapsuleImage? {
        guard weather.showWeather else { return nil }
        return usesLiteWeatherCapsule ? narrowWeatherImage : wideWeatherImage
    }
    private var musicStatusImage: MenuBarCapsuleImage?
    private var splitProgress: CGFloat = 0
    private var splitTarget: CGFloat?
    private var splitUsesLite = false
    private var splitHasWeather = true
    private var liteSplitAnchor: NSPoint?
    private var splitTask: Task<Void, Never>?
    private var renderedSplit: MenuBarCapsuleTransition?
    private var systemAppearanceObservation: NSKeyValueObservation?
    private var lastWeatherImageAt: TimeInterval = -.infinity
    private var lastMusicImageAt = Date.distantPast
    private var lastMusicBars: [Int] = []
    private var lastMusicArt: NSImage?
    private var lastMusicScheme: ColorScheme?
    private var lastMusicPlayback: MusicView.EntryPlayback?
    private var sendingMusicCommand = false
    private let musicHoverPanel: SurfacePanel = {
        let panel = SurfacePanel(menuBar: true)
        panel.title = "LumiBar 曲目信息"
        panel.ignoresMouseEvents = true
        panel.contentView = nil
        return panel
    }()
    private lazy var notchLyricsPanel = NotchLyricsPanel(music: music, appearance: appearance) { [weak self] in
        self?.showLyricLibraryFromMenu()
    }
    private var settings: NSWindow?
    private var referenceWindow: SurfacePanel?
    private var lyricLibraryWindow: NSWindow?
    private var timer: Timer?
    private var weatherTimer: Timer?
    private var globalClicks: Any?
    private var localClicks: Any?
    private var menuTrackingDepth = 0
    private var nativeCapsulePress: (point: NSPoint, capsule: MenuBarCapsule)?
    private var weatherLayoutGeneration = 0
    private var weatherHeightLimit: CGFloat = 0
    private var weatherNaturalHeight: CGFloat?
    private weak var weatherAlertWindow: NSWindow?
    private var measuredWeatherHiddenItems: Set<WeatherDetailItem> = []
    private var weatherHoverAnchor: NSRect?
    private var previewTime: Date?
    private var weatherOpenedAt: TimeInterval = 0
    private var weatherDetailOpen: Bool { weatherPanel.isVisible && !weatherPanel.closing }
    private var weatherDismissedUntilExit = false
    private var musicDismissedUntilExit = false
    private var backgrounded = false
    private var musicScreen: NSScreen? { NSScreen.screens.first }
    private var motion: Bool { appearance.animation && !NSWorkspace.shared.accessibilityDisplayShouldReduceMotion }
    private let capsuleHeight = MenuBarCapsuleShape.entryHeight

    func applicationDidFinishLaunching(_ notification: Notification) {
        if let reference {
            showReference(reference)
            return
        }
        DisplaySpaces.refresh()
        status.button?.target = self
        status.button?.action = #selector(statusClicked)
        status.button?.sendAction(on: [.leftMouseUp, .rightMouseUp])
        status.button?.isBordered = false
        status.button?.imageScaling = .scaleNone
        (status.button?.cell as? NSButtonCell)?.highlightsBy = []
        status.button?.setAccessibilityLabel("LumiBar 天气")
        weatherPanel.onCancel = { [weak self] in self?.dismissWeather() }
        weather.onChange = { [weak self] in self?.updateStatus() }
        if !preview { retireLumiBarLiveActivities() }
        appearance.onEntryChange = { [weak self] in self?.updateStatus() }
        appearance.onThemeChange = { [weak self] in
            guard let self else { return }
            weatherPanel.appearance = appearance.windowAppearance
            weatherHoverPanel.appearance = appearance.windowAppearance
            musicHoverPanel.appearance = appearance.windowAppearance
            settings?.appearance = appearance.windowAppearance
            lyricLibraryWindow?.appearance = appearance.windowAppearance
            updateStatus()
        }
        systemAppearanceObservation = NSApp.observe(\.effectiveAppearance, options: [.new]) { [weak self] _, _ in
            Task { @MainActor in
                guard let self, self.appearance.windowAppearance == nil else { return }
                self.updateStatus()
            }
        }
        music.onTrackChange = { [weak self] in
            guard let self else { return }
            updateStatus()
            notchLyricsPanel.refresh(active: !backgrounded)
        }
        music.onDisplayChange = { [weak self] in
            guard let self else { return }
            updateStatus()
            notchLyricsPanel.refresh(active: !backgrounded)
        }
        let center = NotificationCenter.default
        center.addObserver(self, selector: #selector(screensChanged), name: NSApplication.didChangeScreenParametersNotification, object: nil)
        center.addObserver(self, selector: #selector(menuBeganTracking), name: NSMenu.didBeginTrackingNotification, object: nil)
        center.addObserver(self, selector: #selector(menuEndedTracking), name: NSMenu.didEndTrackingNotification, object: nil)
        let workspace = NSWorkspace.shared.notificationCenter
        workspace.addObserver(self, selector: #selector(spaceChanged), name: NSWorkspace.activeSpaceDidChangeNotification, object: nil)
        workspace.addObserver(self, selector: #selector(browserActivated), name: NSWorkspace.didActivateApplicationNotification, object: nil)
        workspace.addObserver(self, selector: #selector(sleeping), name: NSWorkspace.willSleepNotification, object: nil)
        workspace.addObserver(self, selector: #selector(woke), name: NSWorkspace.didWakeNotification, object: nil)
        let clickEvents: NSEvent.EventTypeMask = [.leftMouseDown, .rightMouseDown, .leftMouseUp]
        globalClicks = NSEvent.addGlobalMonitorForEvents(matching: clickEvents) { [weak self] event in
            let type = event.type
            let point = event.locationInWindow
            let timestamp = event.timestamp
            Task { @MainActor in self?.handleMouse(type, at: point, timestamp: timestamp) }
        }
        localClicks = NSEvent.addLocalMonitorForEvents(matching: clickEvents) { [weak self] event in
            let point = event.window?.convertPoint(toScreen: event.locationInWindow) ?? event.locationInWindow
            if event.type == .leftMouseDown { self?.rememberNativeCapsulePress(event, point: point) }
            self?.handleMouse(event.type, at: point, timestamp: event.timestamp, window: event.window)
            return event
        }
        timer = Timer(timeInterval: 0.04, target: self, selector: #selector(pointerTick), userInfo: nil, repeats: true)
        if let timer { RunLoop.main.add(timer, forMode: .common) }
        weatherTimer = Timer.scheduledTimer(timeInterval: 20 * 60, target: self,
                                            selector: #selector(refreshWeather), userInfo: nil, repeats: true)

        let arguments = ProcessInfo.processInfo.arguments
        if preview {
            let showsAlerts = arguments.contains("--weather-alerts")
            previewTime = try? PreviewData.install(weather: weather, music: music, appearance: appearance,
                                                  scene: showsAlerts ? .alerts : .weather)
            if showsAlerts || arguments.contains("--weather") || arguments.contains("--weather-hover") {
                Task { @MainActor [weak self] in
                    try? await Task.sleep(for: .milliseconds(350))
                    self?.openPreviewWeather(compact: arguments.contains("--weather-hover"))
                }
            }
        } else {
            weather.refresh()
            music.start()
            Task { await music.refreshAppleMusicLyricConnection(reload: true) }
        }
        updateStatus()
        notchLyricsPanel.refresh(active: !backgrounded)
        if preview, arguments.contains("--lyrics") { notchLyricsPanel.toggle() }
        if arguments.contains("--settings") || (!preview && !checkMenuBar) { showSettings() }
        if checkMenuBar {
            Task { @MainActor [weak self] in
                try? await Task.sleep(for: .milliseconds(750))
                self?.reportStatusItems()
            }
        }
        logger.notice("LumiBar started")
    }

    func applicationShouldHandleReopen(_ sender: NSApplication, hasVisibleWindows flag: Bool) -> Bool {
        guard !preview, reference == nil else { return false }
        showSettings()
        return false
    }

    func applicationWillTerminate(_ notification: Notification) {
        notchLyricsPanel.hide()
        splitTask?.cancel()
        systemAppearanceObservation?.invalidate()
        timer?.invalidate()
        weatherTimer?.invalidate()
        if let globalClicks { NSEvent.removeMonitor(globalClicks) }
        if let localClicks { NSEvent.removeMonitor(localClicks) }
        music.stop()
        music.spectrum.stop()
        if let previewDomain { defaults.removePersistentDomain(forName: previewDomain) }
    }

    @objc private func refreshWeather() {
        if !preview { weather.refresh() }
    }

    private var statusFrame: NSRect? {
        guard weather.showWeather else { return nil }
        guard let group = statusImageFrame, let image = weatherStatusImage else { return entryFrame(status) }
        if let renderedSplit {
            let frame = showsWideWeatherDuringSplit(renderedSplit) ? renderedSplit.frame : renderedSplit.weatherFrame
            return frame.offsetBy(dx: group.minX, dy: group.minY)
        }
        return NSRect(x: group.maxX - image.size.width, y: group.minY,
                      width: image.size.width, height: group.height)
    }

    private var musicFrame: NSRect? {
        guard splitTask == nil, showsMusicCapsule, let group = statusImageFrame else { return nil }
        guard let image = musicStatusImage else { return weather.showWeather ? nil : group }
        return NSRect(x: group.minX, y: group.minY, width: image.size.width, height: group.height)
    }

    private func showsWideWeatherDuringSplit(_ transition: MenuBarCapsuleTransition) -> Bool {
        !splitUsesLite && transition.progress <= 0.45
    }

    private var canControlMusic: Bool {
        showsMusicCapsule && music.track?.systemSession == true && SystemMedia.executable != nil && !sendingMusicCommand
    }

    private func musicControlContains(_ point: NSPoint, in frame: NSRect) -> Bool {
        let control = MusicView.entryControlFrame(in: frame)
        return Circle().path(in: control).contains(point)
    }

    private var statusImageFrame: NSRect? {
        if capsuleInputView.transitionImage != nil { return capsuleInputFrame }
        return nativeStatusImageFrame
    }

    private var nativeStatusImageFrame: NSRect? {
        guard let frame = entryFrame(status), let image = status.button?.image else { return nil }
        let size = image.size
        return NSRect(x: frame.midX - size.width / 2, y: frame.midY - size.height / 2,
                      width: size.width, height: size.height)
    }

    private func capsuleContains(_ pointer: NSPoint, in frame: NSRect?) -> Bool {
        guard let frame, frame.contains(pointer) else { return false }
        return MenuBarCapsuleShape.outline(height: frame.height).path(in: frame).contains(pointer)
    }

    private func capsule(at pointer: NSPoint) -> CapsuleHit? {
        if let musicFrame, capsuleContains(pointer, in: musicFrame) { return (.music, musicFrame) }
        guard let statusFrame else { return nil }
        if let renderedSplit, showsWideWeatherDuringSplit(renderedSplit) {
            let local = NSPoint(x: pointer.x - statusFrame.minX, y: pointer.y - statusFrame.minY)
            let outline = MenuBarCapsuleShape.movingOutline(leading: renderedSplit.musicFrame,
                trailing: renderedSplit.weatherFrame, progress: renderedSplit.progress)
            return outline.contains(local) ? (.weather, statusFrame) : nil
        }
        if capsuleContains(pointer, in: statusFrame) { return (.weather, statusFrame) }
        return nil
    }

    private func syncCapsuleInputPanel() {
        guard let native = nativeStatusImageFrame, let window = status.button?.window,
              window.isVisible, !backgrounded, menuBarVisible(on: native, window: window) else {
            if liteSplitAnchor != nil {
                liteSplitAnchor = nil
                displayStatusImages()
            }
            capsuleInputView.cancelPress()
            capsuleInputFrame = nil
            if capsuleInputPanel.isVisible { capsuleInputPanel.orderOut(nil) }
            return
        }
        placeCapsuleInputPanel(at: statusImageFrame ?? native)
    }

    private func menuBarVisible(on frame: NSRect, window: NSWindow) -> Bool {
        let center = NSPoint(x: frame.midX, y: frame.midY)
        guard let screen = NSScreen.screens.first(where: { $0.frame.contains(center) }),
              DisplaySpaces.isFullScreen(on: screen) else { return true }
        return window.occlusionState.contains(.visible)
    }

    private func placeCapsuleInputPanel(at frame: NSRect) {
        if capsuleInputFrame != frame {
            capsuleInputFrame = frame
            capsuleInputPanel.setFrame(frame, display: true)
        }
        let passesClicks = NSEvent.modifierFlags.contains(.command)
        if capsuleInputPanel.ignoresMouseEvents != passesClicks {
            if passesClicks { capsuleInputView.cancelPress() }
            capsuleInputPanel.ignoresMouseEvents = passesClicks
        }
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

    private var interfaceAppearance: NSAppearance { appearance.windowAppearance ?? NSApp.effectiveAppearance }

    private var statusScheme: ColorScheme {
        interfaceAppearance.bestMatch(from: [.aqua, .darkAqua]) == .darkAqua ? .dark : .light
    }

    private func statusImage<V: View>(_ view: V, button: NSStatusBarButton,
                                      reusingContent: NSImage? = nil) -> MenuBarCapsuleImage? {
        let scheme = statusScheme
        let scale = button.window?.backingScaleFactor ?? NSScreen.main?.backingScaleFactor ?? 2
        func render(_ layer: MenuBarCapsuleLayer) -> NSImage? {
            let renderer = ImageRenderer(content: view
                .environment(\.colorScheme, scheme)
                .environment(\.menuBarCapsuleLayer, layer))
            renderer.scale = scale
            let image = renderer.nsImage
            image?.isTemplate = false
            return image
        }
        guard let surface = render(.surface),
              let content = (reusingContent?.size == surface.size ? reusingContent : nil) ?? render(.content) else { return nil }
        return MenuBarCapsuleImage(surface: surface, content: content)
    }

    private func reportStatusItems() {
        let mode = statusScheme == .light ? "light" : "dark"
        logger.notice("Menu bar appearance: \(mode, privacy: .public)")
        if statusFrame == nil {
            let frame = status.button?.window?.frame.debugDescription ?? "none"
            logger.notice("Menu bar item visible=\(self.status.isVisible), window=\(frame, privacy: .public)")
        }
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
        showWeather(anchor: statusFrame ?? fallback, compact: compact, on: weatherPanel)
    }

    private func updateStatus() {
        let visible = weather.showWeather || showsMusicCapsule
        if status.isVisible != visible { status.isVisible = visible }
        if !weather.showWeather, weatherPanel.isVisible { dismissWeather() }
        guard let button = status.button else { return }
        updateSplitTarget()
        button.appearance = appearance.windowAppearance
        button.imagePosition = .imageOnly
        button.title = ""
        _ = updateWeatherStatusImages(force: true)
        _ = updateMusicStatusImage(force: true)
        displayStatusImages()
        updateStatusAccessibility()
        syncCapsuleInputPanel()
        updateCapsuleHover(reposition: true)
    }

    private func updateWeatherStatusImages(force: Bool = false) -> Bool {
        guard weather.showWeather, let button = status.button else { return false }
        let moving = weather.capsuleGlow && weather.tintScale > 0 && motion && !backgrounded && weather.weather != nil
        let now = CACurrentMediaTime()
        guard force || (moving && button.window?.isVisible == true && now - lastWeatherImageAt >= 1.0 / 18.0)
        else { return false }
        let time = moving ? now : nil
        func renderCapsule(minimumWidth: CGFloat, showsPrecipitation: Bool,
                           reusingContent: NSImage? = nil) -> MenuBarCapsuleImage? {
            let capsule = WeatherCapsule(model: weather, appearance: appearance, height: capsuleHeight,
                                         minimumWidth: minimumWidth, showsPrecipitation: showsPrecipitation, lightTime: time)
            return statusImage(capsule, button: button, reusingContent: reusingContent)
        }

        if force {
            narrowWeatherImage = renderCapsule(minimumWidth: MusicView.entryWidth, showsPrecipitation: false)
            if weather.liteCapsule {
                wideWeatherImage = nil
            } else {
                // Keep the group the same width before and after the capsules split.
                let musicAndGap = MusicView.entryWidth + capsuleGap
                let narrowWidth = narrowWeatherImage?.size.width ?? MusicView.entryWidth
                let pairedWidth = musicAndGap + narrowWidth
                wideWeatherImage = renderCapsule(minimumWidth: pairedWidth, showsPrecipitation: true)
                if let wideWidth = wideWeatherImage?.size.width, wideWidth > pairedWidth {
                    narrowWeatherImage = renderCapsule(minimumWidth: wideWidth - musicAndGap, showsPrecipitation: false)
                }
            }
        } else if usesLiteWeatherCapsule {
            narrowWeatherImage = renderCapsule(minimumWidth: narrowWeatherImage?.size.width ?? MusicView.entryWidth,
                                               showsPrecipitation: false, reusingContent: narrowWeatherImage?.content)
        } else {
            wideWeatherImage = renderCapsule(minimumWidth: wideWeatherImage?.size.width ?? minimumMenuBarWidth,
                                             showsPrecipitation: true, reusingContent: wideWeatherImage?.content)
        }
        lastWeatherImageAt = now
        return true
    }

    private func updateSplitTarget() {
        let target: CGFloat = weather.showWeather && showsMusicCapsule ? 1 : 0
        let modeChanged = splitHasWeather != weather.showWeather || splitUsesLite != weather.liteCapsule
        splitUsesLite = weather.liteCapsule
        splitHasWeather = weather.showWeather
        let canAnimate = weather.showWeather && motion && !backgrounded && !modeChanged
        guard splitTarget != target || (splitTask != nil && !canAnimate) else { return }
        let needsAnimation = splitTarget != nil && canAnimate && abs(splitProgress - target) > 0.001
        splitTarget = target
        splitTask?.cancel()
        guard needsAnimation else {
            splitTask = nil
            splitProgress = target
            liteSplitAnchor = nil
            return
        }
        if splitUsesLite, liteSplitAnchor == nil, let frame = nativeStatusImageFrame,
           let window = status.button?.window, window.isVisible, menuBarVisible(on: frame, window: window) {
            liteSplitAnchor = NSPoint(x: frame.maxX, y: frame.midY)
        }
        let from = splitProgress
        let duration = max(0.16, 0.46 * abs(target - from))
        let start = CACurrentMediaTime()
        splitTask = Task { @MainActor [weak self] in
            while !Task.isCancelled {
                guard let self else { return }
                let elapsed = min(1, (CACurrentMediaTime() - start) / duration)
                let eased = elapsed * elapsed * (3 - 2 * elapsed)
                splitProgress = from + (target - from) * eased
                if elapsed >= 1 {
                    if liteSplitAnchor != nil {
                        displayStatusImages()
                        let deadline = CACurrentMediaTime() + 0.18
                        while !Task.isCancelled, liteSplitAnchor != nil, CACurrentMediaTime() < deadline,
                              !liteTransitionMatchesNativeFrame {
                            try? await Task.sleep(for: .milliseconds(16))
                        }
                        guard !Task.isCancelled else { return }
                    }
                    splitTask = nil
                    liteSplitAnchor = nil
                    if !showsMusicCapsule { musicStatusImage = nil }
                    displayStatusImages()
                    syncCapsuleInputPanel()
                    return
                }
                displayStatusImages()
                try? await Task.sleep(for: .milliseconds(16))
            }
        }
    }

    private var liteTransitionMatchesNativeFrame: Bool {
        guard let frame = capsuleInputFrame, let native = nativeStatusImageFrame else { return false }
        return abs(frame.maxX - native.maxX) <= 0.5 && abs(frame.midY - native.midY) <= 0.5
            && abs(frame.width - native.width) <= 0.5
    }

    private func presentStatusImage(_ image: NSImage, reservingWidth: CGFloat? = nil) {
        guard let button = status.button else { return }
        if let anchor = liteSplitAnchor, let reservingWidth {
            // Keep the visual position while macOS moves the native slot to accommodate the new width.
            let size = image.size
            let placeholderSize = NSSize(width: reservingWidth, height: size.height)
            if status.length != reservingWidth { status.length = reservingWidth }
            if capsuleInputView.transitionImage == nil || button.image?.size != placeholderSize {
                button.image = NSImage(size: placeholderSize, flipped: false) { _ in true }
            }
            capsuleInputView.transitionImage = image
            placeCapsuleInputPanel(at: NSRect(x: anchor.x - size.width, y: anchor.y - size.height / 2,
                                              width: size.width, height: size.height))
        } else {
            if status.length != image.size.width { status.length = image.size.width }
            button.image = image
            capsuleInputView.transitionImage = nil
        }
    }

    private func displayStatusImages() {
        guard let button = status.button else { return }
        let scheme = statusScheme
        if weather.showWeather, splitTask != nil, let narrowWeatherImage, let musicStatusImage,
           splitUsesLite || wideWeatherImage != nil {
            let wideImage = wideWeatherImage
            let progress = splitProgress
            let opening = max(0, (progress - 0.76) / 0.24)
            let gap = capsuleGap * opening * opening * (3 - 2 * opening)
            let growth = min(1, progress / 0.76)
            let originalMusicWidth = musicStatusImage.size.width
            let musicWidth = splitUsesLite
                ? (originalMusicWidth * growth * (2 - growth)).rounded()
                : originalMusicWidth + (capsuleGap - gap) / 2
            let musicHeight = min(capsuleHeight, musicWidth)
            let musicFrame = NSRect(x: 0, y: (capsuleHeight - musicHeight) / 2, width: musicWidth, height: musicHeight)
            let weatherX = musicFrame.maxX + gap
            let size = wideImage?.size ?? CGSize(width: weatherX + narrowWeatherImage.size.width, height: capsuleHeight)
            let frame = NSRect(origin: .zero, size: size)
            let weatherFrame = NSRect(x: weatherX, y: 0, width: size.width - weatherX, height: size.height)
            let transition = MenuBarCapsuleTransition(frame: frame, musicFrame: musicFrame, weatherFrame: weatherFrame,
                                                      progress: progress)
            renderedSplit = transition
            let image = NSImage(size: size, flipped: false) { _ in
                guard let context = NSGraphicsContext.current?.cgContext else { return false }
                transition.draw(wide: wideImage, music: musicStatusImage, weather: narrowWeatherImage,
                                scheme: scheme, in: context)
                return true
            }
            let reservedWidth = progress > 0 || splitTarget == 1
                ? narrowWeatherImage.size.width + originalMusicWidth + capsuleGap
                : size.width
            presentStatusImage(image, reservingWidth: splitUsesLite ? reservedWidth : nil)
            return
        }
        renderedSplit = nil
        let musicImage = showsMusicCapsule ? musicStatusImage : nil
        let weatherImage = weatherStatusImage
        let gap = musicImage != nil && weatherImage != nil ? capsuleGap : 0
        let weatherX = (musicImage?.size.width ?? 0) + gap
        let size = CGSize(width: weatherX + (weatherImage?.size.width ?? 0), height: capsuleHeight)
        if size.width > 0 {
            let image = NSImage(size: size, flipped: false) { _ in
                guard let context = NSGraphicsContext.current?.cgContext else { return false }
                let capsule = MenuBarCapsuleShape.outline(height: size.height)
                func draw(_ image: MenuBarCapsuleImage, x: CGFloat) {
                    let frame = NSRect(origin: NSPoint(x: x, y: 0), size: image.size)
                    MenuBarCapsuleChrome.draw(outline: capsule.path(in: frame), scheme: scheme, in: context) {
                        image.surface.draw(in: frame)
                        image.content.draw(in: frame)
                    }
                }
                if let musicImage { draw(musicImage, x: 0) }
                if let weatherImage { draw(weatherImage, x: weatherX) }
                return true
            }
            presentStatusImage(image)
        } else {
            capsuleInputView.transitionImage = nil
            if status.isVisible {
                status.length = NSStatusItem.squareLength
                let symbol = weather.showWeather ? "cloud.sun" : "music.note"
                let image = NSImage(systemSymbolName: symbol, accessibilityDescription: nil)
                image?.isTemplate = true
                button.image = image
            } else {
                button.image = nil
            }
        }
    }

    private func updateStatusAccessibility() {
        guard let button = status.button else { return }
        var label = "LumiBar"
        if weather.showWeather {
            label += " 天气"
            if let forecast = weather.weather {
                let condition = forecast.conditionText ?? Weather.appearance(forecast.code, day: forecast.isDay).name
                label += "，\(condition)，\(weather.temperature(forecast.temperature))"
                if !usesLiteWeatherCapsule, let rain = forecast.rain {
                    label += "，降水概率 " + rain.formatted(.number.precision(.fractionLength(0))) + "%"
                }
            }
        }
        if showsMusicCapsule, let track = music.track {
            label += weather.showWeather ? "，音乐，" : " 音乐，"
            label += "\(track.title)，\(track.artist)"
            if !track.playing { label += "，已暂停" }
            let actionName = track.playing ? "暂停音乐" : "播放音乐"
            button.setAccessibilityCustomActions(track.systemSession && SystemMedia.executable != nil ? [
                NSAccessibilityCustomAction(name: actionName, target: self, selector: #selector(toggleMusicPlayback))
            ] : [])
        } else {
            button.setAccessibilityCustomActions([])
        }
        button.setAccessibilityLabel(label)
    }

    @objc private func toggleMusicPlayback() -> Bool {
        guard canControlMusic, let track = music.track else { return false }
        sendingMusicCommand = true
        Task { @MainActor [weak self] in
            guard let self else { return }
            let accepted = await music.control(.playPause, trackKey: track.key)
            sendingMusicCommand = false
            if !accepted, music.track?.key == track.key {
                logger.notice("Menu bar play or pause command was not accepted")
            }
        }
        return true
    }

    private func showStatusMenu(_ sender: NSStatusBarButton) {
        dismissWeather()
        dismissMusic()
        let menu = NSMenu()
        menu.appearance = interfaceAppearance
        menu.addItem(withTitle: "LumiBar 设置…", action: #selector(showSettings), keyEquivalent: ",").target = self
        menu.addItem(withTitle: "打开歌词管理…", action: #selector(showLyricLibraryFromMenu), keyEquivalent: "").target = self
        let lyricsVisibility = music.notchLyrics.enabled ? "隐藏 Lumi 歌词" : "显示 Lumi 歌词"
        menu.addItem(withTitle: lyricsVisibility, action: #selector(toggleLyricsVisibility), keyEquivalent: "").target = self
        if notchLyricsPanel.canExpand {
            let label = notchLyricsPanel.expanded ? "收起 Lumi 歌词" : "展开 Lumi 歌词"
            menu.addItem(withTitle: label, action: #selector(toggleNotchLyrics), keyEquivalent: "").target = self
        }
        menu.addItem(.separator())
        let hidePaused = menu.addItem(withTitle: NotchLyricsOptions.hideWhenPausedLabel,
                                      action: #selector(togglePausedLyricsVisibility), keyEquivalent: "")
        hidePaused.target = self
        hidePaused.state = music.notchLyrics.hidesWhenPaused ? .on : .off
        let hideInFullScreen = menu.addItem(withTitle: NotchLyricsOptions.hideInFullScreenLabel,
                                            action: #selector(toggleFullScreenLyricsVisibility), keyEquivalent: "")
        hideInFullScreen.target = self
        hideInFullScreen.state = music.notchLyrics.hidesInFullScreen ? .on : .off
        menu.addItem(.separator())
        menu.addItem(withTitle: "退出 LumiBar", action: #selector(quit), keyEquivalent: "q").target = self
        menu.popUp(positioning: nil, at: NSPoint(x: 0, y: sender.bounds.height), in: sender)
    }

    @objc private func toggleLyricsVisibility() {
        music.notchLyrics.enabled.toggle()
    }

    @objc private func togglePausedLyricsVisibility() {
        music.notchLyrics.hidesWhenPaused.toggle()
        notchLyricsPanel.refresh(active: !backgrounded)
    }

    @objc private func toggleFullScreenLyricsVisibility() {
        music.notchLyrics.hidesInFullScreen.toggle()
        notchLyricsPanel.refresh(active: !backgrounded)
    }

    @objc private func toggleNotchLyrics() { notchLyricsPanel.toggle() }

    private func rememberNativeCapsulePress(_ event: NSEvent, point: NSPoint) {
        nativeCapsulePress = nil
        guard let nativeWindow = status.button?.window, event.window === nativeWindow,
              !event.modifierFlags.contains(.command), let hit = capsule(at: point) else { return }
        if hit.capsule == .music, musicStatusImage == nil || !canControlMusic || !musicControlContains(point, in: hit.frame) {
            return
        }
        nativeCapsulePress = (point, hit.capsule)
    }

    @objc private func statusClicked(_ sender: NSStatusBarButton) {
        if let event = NSApp.currentEvent, event.type == .leftMouseUp || event.type == .rightMouseUp {
            let point = event.window?.convertPoint(toScreen: event.locationInWindow) ?? event.locationInWindow
            let press = nativeCapsulePress
            nativeCapsulePress = nil
            if event.type == .leftMouseUp {
                guard let press else { return }
                statusPointerReleased(event, at: point, startedAt: press.point, pressed: press.capsule)
            } else {
                statusPointerReleased(event, at: point)
            }
        } else if weather.showWeather {
            toggleWeather(at: NSEvent.mouseLocation, timestamp: ProcessInfo.processInfo.systemUptime)
        } else {
            _ = toggleMusicPlayback()
        }
    }

    private func statusPointerReleased(_ event: NSEvent, at pointer: NSPoint, startedAt start: NSPoint? = nil,
                                       pressed: MenuBarCapsule? = nil) {
        guard let hit = capsule(at: pointer), pressed == nil || pressed == hit.capsule else { return }
        if event.type == .rightMouseUp, let button = status.button {
            showStatusMenu(button)
        } else if hit.capsule == .music {
            if let start, musicStatusImage != nil, !event.modifierFlags.contains(.command),
               musicControlContains(start, in: hit.frame), musicControlContains(pointer, in: hit.frame) {
                _ = toggleMusicPlayback()
            }
        } else {
            toggleWeather(at: pointer, timestamp: event.timestamp)
        }
    }

    private func toggleWeather(at cursor: NSPoint, timestamp: TimeInterval) {
        guard weather.showWeather else { return }
        if weatherDetailOpen {
            dismissWeather()
            return
        }
        hideHover(weatherHoverPanel)
        dismissMusic()
        let anchor = statusFrame ?? NSRect(x: cursor.x - 10, y: cursor.y - capsuleHeight / 2, width: 20, height: capsuleHeight)
        weatherOpenedAt = timestamp
        showWeather(anchor: anchor, compact: false, on: weatherPanel)
        if checkMenuBar { logger.notice("Menu bar weather detail opened") }
    }

    private func dismissWeather() {
        if checkMenuBar && weatherDetailOpen { logger.notice("Menu bar weather detail closed") }
        weatherDismissedUntilExit = true
        hideHover(weatherHoverPanel)
        weatherPanel.dismiss(duration: 0)
        weatherPanel.contentView = nil
        weatherAlertWindow = nil
    }

    private func showWeather(anchor: NSRect, compact: Bool, on panel: SurfacePanel) {
        guard let screen = NSScreen.screens.first(where: { $0.frame.contains(.init(x: anchor.midX, y: anchor.midY)) })
                ?? musicScreen else { return }
        let width = min(compact ? WeatherView.hoverSize.width : 512, screen.visibleFrame.width)
        let maxHeight = max(180, anchor.minY - screen.visibleFrame.minY)
        let initial = compact ? WeatherView.hoverSize.height : weatherNaturalHeight ?? (weather.weather == nil ? 275 : 742)
        let height = min(CGFloat(initial), maxHeight)
        if !compact {
            weatherHeightLimit = maxHeight
            weatherLayoutGeneration += 1
        }
        let generation = weatherLayoutGeneration
        let x = max(screen.visibleFrame.minX, min(anchor.midX - width / 2, screen.visibleFrame.maxX - width))
        panel.appearance = appearance.windowAppearance
        panel.title = compact ? "LumiBar 天气速览" : "LumiBar 天气详情"
        if panel === weatherPanel || panel.contentView == nil {
            let view = NSHostingView(rootView: WeatherView(model: weather, appearance: appearance, compact: compact,
                width: width, height: nil, referenceDate: previewTime, measured: { [weak self] value in
                    if !compact { Task { @MainActor in self?.resizeWeather(to: value, generation: generation) } }
                }, alertWindowChanged: { [weak self] in self?.weatherAlertWindow = $0 },
                showSettings: { [weak self] in self?.showSettings() },
                quit: { [weak self] in self?.quit() }))
            view.sizingOptions = []
            panel.contentView = view
        }
        panel.present(NSRect(x: x, y: anchor.minY - height, width: width, height: height), key: !compact)
    }

    private func resizeWeather(to desired: CGFloat, generation: Int) {
        guard generation == weatherLayoutGeneration, desired.isFinite, weatherPanel.isVisible, !weatherPanel.closing else { return }
        let animate = motion && measuredWeatherHiddenItems == weather.hiddenDetailItems
        measuredWeatherHiddenItems = weather.hiddenDetailItems
        weatherNaturalHeight = max(96, ceil(desired))
        let old = weatherPanel.frame
        let height = min(weatherHeightLimit, weatherNaturalHeight ?? old.height)
        guard abs(old.height - height) > 1 else { return }
        let frame = NSRect(x: old.minX, y: old.maxY - height, width: old.width, height: height)
        NSAnimationContext.runAnimationGroup { context in
            context.duration = animate ? (height > old.height ? 0.28 : 0.22) : 0
            context.timingFunction = CAMediaTimingFunction(name: .easeInEaseOut)
            weatherPanel.animator().setFrame(frame, display: true)
        }
    }

    private func handleMouse(_ type: NSEvent.EventType, at pointer: NSPoint, timestamp: TimeInterval, window: NSWindow? = nil) {
        if type != .rightMouseDown {
            notchLyricsPanel.headerMouseChanged(isDown: type == .leftMouseDown, at: pointer)
        }
        guard type != .leftMouseUp else { return }
        if menuTrackingDepth == 0 { notchLyricsPanel.dismissIfOutside(pointer) }
        if weatherDetailOpen, timestamp > weatherOpenedAt, !weatherPanel.frame.contains(pointer), capsule(at: pointer) == nil,
           window == nil || window !== weatherAlertWindow {
            dismissWeather()
        }
    }

    @objc private func menuBeganTracking() {
        menuTrackingDepth += 1
        notchLyricsPanel.isTrackingMenu = true
        updateCapsuleHover()
    }

    @objc private func menuEndedTracking() {
        menuTrackingDepth = max(0, menuTrackingDepth - 1)
        notchLyricsPanel.isTrackingMenu = menuTrackingDepth > 0
    }

    @objc private func pointerTick() {
        syncCapsuleInputPanel()
        notchLyricsPanel.refresh(active: !backgrounded)
        updateCapsuleHover()
        guard splitTask == nil else { return }
        let weatherImageChanged = updateWeatherStatusImages()
        let musicImageChanged = updateMusicStatusImage()
        if weatherImageChanged || musicImageChanged { displayStatusImages() }
    }

    private func updateCapsuleHover(reposition: Bool = false) {
        guard capsuleInputFrame != nil, !backgrounded, menuTrackingDepth == 0,
              !NSEvent.modifierFlags.contains(.command) else {
            hideHover(weatherHoverPanel)
            hideHover(musicHoverPanel)
            return
        }
        let hit = capsule(at: NSEvent.mouseLocation)
        if hit?.capsule != .weather { weatherDismissedUntilExit = false }
        if hit?.capsule != .music { musicDismissedUntilExit = false }

        if let hit, hit.capsule == .weather, !weatherDetailOpen, !weatherDismissedUntilExit {
            if reposition || hit.frame != weatherHoverAnchor || !weatherHoverPanel.isVisible {
                showWeather(anchor: hit.frame, compact: true, on: weatherHoverPanel)
                weatherHoverAnchor = hit.frame
            }
        } else {
            hideHover(weatherHoverPanel)
        }
        if let hit, hit.capsule == .music, !musicDismissedUntilExit {
            if reposition || !musicHoverPanel.isVisible { layoutMusic(anchor: hit.frame) }
        } else {
            hideHover(musicHoverPanel)
        }
    }

    private func hideHover(_ panel: SurfacePanel) {
        if panel.isVisible { panel.orderOut(nil) }
        if panel.contentView != nil { panel.contentView = nil }
    }

    private func dismissMusic() {
        musicDismissedUntilExit = true
        hideHover(musicHoverPanel)
    }

    private func updateMusicStatusImage(force: Bool = false) -> Bool {
        guard showsMusicCapsule, let button = status.button, let track = music.track else {
            guard force || musicStatusImage != nil else { return false }
            if splitTask == nil { musicStatusImage = nil }
            return true
        }
        let now = Date.now
        let movingTint = music.capsuleGlow && track.playing && appearance.coverTint && motion && music.artwork != nil
        let pointer = NSEvent.mouseLocation
        let hovered = menuTrackingDepth == 0 && !NSEvent.modifierFlags.contains(.command)
            && capsuleContains(pointer, in: musicFrame)
        let playback: MusicView.EntryPlayback?
        if !track.playing || hovered {
            let highlighted = hovered && musicFrame.map { musicControlContains(pointer, in: $0) } == true
            playback = .init(playing: track.playing, highlighted: highlighted,
                             enabled: canControlMusic)
        } else {
            playback = nil
        }
        let refreshInterval = movingTint ? 1.0 / 30.0 : 0.09
        guard force || playback != lastMusicPlayback || now.timeIntervalSince(lastMusicImageAt) > refreshInterval else { return false }
        lastMusicImageAt = now
        let levels = track.playing ? (music.previewSpectrum ?? (music.spectrumEnabled
            && !NSWorkspace.shared.accessibilityDisplayShouldReduceMotion ? music.spectrum.levels : [])) : []
        let bands = MusicView.entryBands(from: levels, response: music.capsuleSpectrumResponse)
        let scheme = statusScheme
        let contentChanged = force || bands != lastMusicBars || music.artwork !== lastMusicArt || scheme != lastMusicScheme
            || playback != lastMusicPlayback
        guard contentChanged || movingTint || musicStatusImage == nil else { return false }
        lastMusicBars = bands
        lastMusicArt = music.artwork
        lastMusicScheme = scheme
        lastMusicPlayback = playback
        let entry = MusicView(model: music, appearance: appearance,
                              surface: .entry(height: capsuleHeight, bands: bands, playback: playback,
                                              lightTime: movingTint ? CACurrentMediaTime() : nil)) { }
        musicStatusImage = statusImage(entry, button: button, reusingContent: contentChanged ? nil : musicStatusImage?.content)
        return true
    }

    private func layoutMusic(anchor: NSRect) {
        guard let track = music.track,
              let screen = NSScreen.screens.first(where: { $0.frame.contains(.init(x: anchor.midX, y: anchor.midY)) })
                ?? musicScreen else { return }
        let width = min(MusicView.hoverWidth(for: track, appearance: appearance), screen.visibleFrame.width)
        let top = anchor.minY - FlyoutChrome.inset
        let height = MusicView.hoverHeight
        let x = max(screen.visibleFrame.minX, min(anchor.midX - width / 2, screen.visibleFrame.maxX - width))
        if musicHoverPanel.contentView == nil {
            let view = NSHostingView(rootView: MusicView(model: music, appearance: appearance, surface: .hover) {})
            view.sizingOptions = []
            musicHoverPanel.contentView = view
        }
        musicHoverPanel.appearance = appearance.windowAppearance
        musicHoverPanel.present(.init(x: x, y: top - height, width: width, height: height))
    }

    @objc private func screensChanged() {
        DisplaySpaces.refresh()
        liteSplitAnchor = nil
        notchLyricsPanel.refresh(active: !backgrounded)
        dismissWeather()
        updateStatus()
    }

    @objc private func browserActivated(_ notification: Notification) {
        guard let app = notification.userInfo?[NSWorkspace.applicationUserInfoKey] as? NSRunningApplication,
              app.bundleIdentifier == AppleMusicWebPlayback.firefoxID else { return }
        music.checkAppleMusicWebDuration(force: true)
    }

    @objc private func spaceChanged() {
        DisplaySpaces.refresh()
        notchLyricsPanel.hide()
        nativeCapsulePress = nil
        capsuleInputView.cancelPress()
        capsuleInputFrame = nil
        if capsuleInputPanel.isVisible { capsuleInputPanel.orderOut(nil) }
        if liteSplitAnchor != nil {
            liteSplitAnchor = nil
            displayStatusImages()
        }
        dismissWeather()
        dismissMusic()
        weatherDismissedUntilExit = false
        musicDismissedUntilExit = false
    }

    @objc private func sleeping() {
        backgrounded = true
        notchLyricsPanel.hide()
        syncCapsuleInputPanel()
        music.spectrum.stop()
        updateStatus()
    }

    @objc private func woke() {
        DisplaySpaces.refresh()
        backgrounded = false
        if music.spectrumEnabled { music.restartSpectrum() }
        updateStatus()
        notchLyricsPanel.refresh(active: true)
        refreshWeather()
    }

    @objc private func showSettings() {
        dismissWeather()
        dismissMusic()
        if settings == nil {
            let window = SettingsWindow(contentRect: NSRect(origin: .zero, size: SettingsView.windowSize),
                                        styleMask: [.titled, .closable, .miniaturizable, .resizable, .fullSizeContentView],
                                        backing: .buffered, defer: false)
            window.title = "LumiBar 设置"
            window.contentMinSize = NSSize(width: 730, height: 530)
            window.collectionBehavior = [.moveToActiveSpace]
            let content = SettingsView(appearance: appearance, weather: weather, music: music,
                                       openLyricLibrary: { [weak self] in self?.showLyricLibrary() })
            installGlassContent(content, in: window)
            window.center()
            settings = window
        }
        NSApp.activate()
        if settings?.isMiniaturized == true { settings?.deminiaturize(nil) }
        settings?.makeKeyAndOrderFront(nil)
    }

    @objc private func showLyricLibraryFromMenu() {
        settings?.orderOut(nil)
        showLyricLibrary()
    }

    private func showLyricLibrary() {
        if lyricLibraryWindow == nil {
            let window = NSWindow(contentRect: NSRect(x: 0, y: 0, width: 960, height: 702),
                                  styleMask: [.titled, .closable, .miniaturizable, .resizable, .fullSizeContentView],
                                  backing: .buffered, defer: false)
            window.title = "LumiBar 歌词管理"
            window.identifier = LyricLibraryView.windowIdentifier
            window.contentMinSize = NSSize(width: 770, height: 522)
            installGlassContent(LyricLibraryView(music: music), in: window)
            if preview || !window.setFrameUsingName("LumiBar Lyric Library") { window.center() }
            if !preview { window.setFrameAutosaveName("LumiBar Lyric Library") }
            lyricLibraryWindow = window
        }
        NSApp.activate()
        lyricLibraryWindow?.makeKeyAndOrderFront(nil)
    }

    private func installGlassContent<Content: View>(_ content: Content, in window: NSWindow) {
        window.titlebarAppearsTransparent = true
        window.titlebarSeparatorStyle = .none
        window.isOpaque = false
        window.backgroundColor = .clear
        window.isReleasedWhenClosed = false
        window.appearance = appearance.windowAppearance

        let backdrop = NSVisualEffectView()
        backdrop.material = .sidebar
        backdrop.blendingMode = .behindWindow
        backdrop.state = .active
        let hosting = NSHostingView(rootView: content.defaultAppStorage(defaults))
        hosting.translatesAutoresizingMaskIntoConstraints = false
        backdrop.addSubview(hosting)
        window.contentView = backdrop
        NSLayoutConstraint.activate([
            hosting.leadingAnchor.constraint(equalTo: backdrop.leadingAnchor),
            hosting.trailingAnchor.constraint(equalTo: backdrop.trailingAnchor),
            hosting.topAnchor.constraint(equalTo: backdrop.topAnchor),
            hosting.bottomAnchor.constraint(equalTo: backdrop.bottomAnchor),
        ])
    }

    private func showReference(_ request: ReferenceRequest) {
        do {
            previewTime = try PreviewData.install(weather: weather, music: music, appearance: appearance, scene: request.scene)
        } catch {
            logger.error("Could not open visual fixture: \(error.localizedDescription)")
            NSApp.terminate(nil)
            return
        }
        appearance.theme = request.dark ? "Dark" : "Light"
        let size = request.scene.size
        let backdrop = Color.rgb(request.dark ? 0x171922 : 0xF3F5FA)
        let content: AnyView
        switch request.scene {
        case .music:
            content = AnyView(MusicView(model: music, appearance: appearance, surface: .referenceLyrics) { NSApp.terminate(nil) })
        case .settings:
            showSettings()
            return
        case .capsule, .lite, .wide:
            let narrow = request.scene == .lite
            content = AnyView(WeatherCapsule(model: weather, appearance: appearance, height: 34,
                minimumWidth: narrow ? MusicView.entryWidth : minimumMenuBarWidth, showsPrecipitation: !narrow))
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
        referenceWindow = panel
        NSApp.activate()
        panel.present(panel.frame, key: true)
    }

    @objc private func quit() { NSApp.terminate(nil) }
}

