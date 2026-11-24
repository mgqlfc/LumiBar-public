import AppKit
import QuartzCore

@MainActor
final class AttachedLyricsTools: NSView, NSMenuDelegate {
    private struct Tool {
        let item: AttachedLyricsHeader.Item
        let name: String
        let symbol: String
        let identifier: String
        let x: CGFloat
        let width: CGFloat

        func frame(in bounds: CGRect) -> CGRect {
            CGRect(x: bounds.minX + AttachedLyricsTools.sideInset + x, y: bounds.midY - 13,
                   width: width, height: 26).intersection(bounds)
        }
    }

    private static let tools = [
        Tool(item: .options, name: "歌词选项", symbol: "ellipsis", identifier: "options", x: 0, width: 20),
        Tool(item: .volumeDown, name: "降低音量一格", symbol: "speaker.minus", identifier: "volumeDown", x: 23, width: 22),
        Tool(item: .volumeUp, name: "提高音量一格", symbol: "speaker.plus", identifier: "volumeUp", x: 48, width: 22)
    ]
    nonisolated static let sideInset: CGFloat = 6
    static let width: CGFloat = 70 + 2 * sideInset

    static func target(at point: CGPoint, in frame: CGRect) -> (item: AttachedLyricsHeader.Item, frame: CGRect)? {
        for tool in tools {
            let hit = tool.frame(in: frame)
            if hit.contains(point) { return (tool.item, hit) }
        }
        return nil
    }

    private let music: MusicModel
    private let prepareMenu: () -> Void
    private let openLyricLibrary: () -> Void
    private var buttons: [ToolButton] = []
    private var currentHover: AttachedLyricsHeader.Item?
    private var currentCover: NSColor?
    private var volumeAvailable = false
    private var volumeCheckedAt: CFTimeInterval = -.infinity
    private weak var offsetCard: OffsetMenuCard?
    private weak var optionsMenu: NSMenu?
    private var opensLibraryAfterMenu = false
    private var animatesFeedback = false
    var isShowingOptions: Bool { optionsMenu != nil }

    init(music: MusicModel, prepareMenu: @escaping () -> Void, openLyricLibrary: @escaping () -> Void) {
        self.music = music
        self.prepareMenu = prepareMenu
        self.openLyricLibrary = openLyricLibrary
        super.init(frame: .zero)
        for (index, tool) in Self.tools.enumerated() {
            let button = ToolButton(symbol: tool.symbol, name: tool.name, identifier: tool.identifier,
                                    pointSize: tool.item == .options ? 10 : 13, weight: tool.item == .options ? .medium : .semibold)
            button.tag = index
            button.target = self
            button.action = #selector(activateTool(_:))
            addSubview(button)
            buttons.append(button)
        }
    }

    required init?(coder: NSCoder) { nil }

    override func viewDidChangeEffectiveAppearance() {
        super.viewDidChangeEffectiveAppearance()
        needsDisplay = true
    }

    override func draw(_ dirtyRect: NSRect) {
        super.draw(dirtyRect)
        let capsule = bounds.insetBy(dx: 0, dy: 2)
        guard capsule.width > 0, capsule.height > 0 else { return }
        let radius = min(capsule.height, capsule.width) / 2
        let dark = effectiveAppearance.bestMatch(from: [.aqua, .darkAqua]) == .darkAqua
        let path = NSBezierPath(roundedRect: capsule, xRadius: radius, yRadius: radius)
        let footprint = Self.tools.reduce(CGRect.null) { $0.union($1.frame(in: bounds)) }
        if !footprint.isEmpty {
            // Keep the corners of the native targets hittable where they extend beyond the visible capsule.
            NSGraphicsContext.saveGraphicsState()
            let outside = NSBezierPath(rect: bounds)
            outside.append(path)
            outside.windingRule = .evenOdd
            outside.addClip()
            let coverage = dark ? NSColor.black.withAlphaComponent(0.02)
                : NSColor(srgbRed: 0.62, green: 0.66, blue: 0.73, alpha: 0.02)
            coverage.setFill()
            footprint.fill()
            NSGraphicsContext.restoreGraphicsState()
        }
        let tint = dark ? NSColor(srgbRed: 0.64, green: 0.64, blue: 0.64, alpha: 0.032)
            : NSColor(srgbRed: 0.35, green: 0.37, blue: 0.41, alpha: 0.069)
        tint.setFill()
        path.fill()
    }

    override func layout() {
        super.layout()
        for (tool, button) in zip(Self.tools, buttons) {
            let frame = tool.frame(in: bounds)
            button.isHidden = frame.isEmpty
            button.frame = frame.isEmpty ? .zero : frame
        }
    }

    func update(header: AttachedLyricsHeader, cover: NSColor?, animated: Bool) {
        let now = CACurrentMediaTime()
        if now - volumeCheckedAt >= 0.5 {
            volumeAvailable = SystemOutputVolume.isAdjustable
            volumeCheckedAt = now
        }
        animatesFeedback = animated
        offsetCard?.refresh()
        let tintChanged = currentHover != header.hovered || currentCover != cover
        for (tool, button) in zip(Self.tools, buttons) {
            if tintChanged || button.contentTintColor == nil {
                button.contentTintColor = header.tint(for: tool.item, cover: cover)
            }
            if tool.item.adjustsVolume, button.isEnabled != volumeAvailable {
                button.isEnabled = volumeAvailable
                button.setAccessibilityHelp(volumeAvailable ? tool.name : "当前输出设备的音量由设备控制")
            }
            button.updateFeedback(header.backgroundOpacity(for: tool.item), animated: animated)
        }
        currentHover = header.hovered
        currentCover = cover
    }

    @objc private func activateTool(_ sender: NSButton) {
        switch Self.tools[sender.tag].item {
        case .options: showOptions(from: sender)
        case .volumeDown: SystemOutputVolume.step(up: false)
        case .volumeUp: SystemOutputVolume.step(up: true)
        default: break
        }
    }

    private func showOptions(from button: NSButton) {
        prepareMenu()
        let menu = NSMenu()
        menu.autoenablesItems = false
        menu.font = .menuFont(ofSize: 12)
        let card = OffsetMenuCard(music: music, animated: animatesFeedback)
        offsetCard = card
        let timing = NSMenuItem()
        timing.view = card
        menu.addItem(timing)
        let sources = NSMenu()
        sources.autoenablesItems = false
        sources.font = menu.font
        sources.delegate = self
        menuNeedsUpdate(sources)
        let sourceMenu = add("切换歌词来源", to: menu, action: nil, enabled: music.track != nil)
        sourceMenu.submenu = sources
        add("打开歌词管理…", to: menu, action: #selector(showLyricLibrary))
        optionsMenu = menu
        let menuY = button.isFlipped ? button.bounds.maxY + 3 : button.bounds.minY - 3
        menu.popUp(positioning: nil, at: CGPoint(x: button.bounds.minX, y: menuY), in: button)
        optionsMenu = nil
        if opensLibraryAfterMenu {
            opensLibraryAfterMenu = false
            openLyricLibrary()
        }
    }

    func menuNeedsUpdate(_ menu: NSMenu) {
        menu.removeAllItems()
        for source in music.lyricSourceChoices {
            let item = add(source.title, to: menu, action: #selector(selectSource(_:)))
            item.representedObject = source.rawValue
            item.state = source == music.selectedLyricSource ? .on : .off
        }
        if let message = music.lyricSourceMessage {
            menu.addItem(withTitle: message, action: nil, keyEquivalent: "").isEnabled = false
        }
        if !menu.items.isEmpty { menu.addItem(.separator()) }
        add("导入本地歌词…", to: menu, action: #selector(importLyrics), enabled: music.track != nil).keyEquivalent = "i"
    }

    @discardableResult
    private func add(_ title: String, to menu: NSMenu, action: Selector?, enabled: Bool = true) -> NSMenuItem {
        let item = menu.addItem(withTitle: title, action: action, keyEquivalent: "")
        item.target = self
        item.isEnabled = enabled
        return item
    }

    @objc private func importLyrics() {
        optionsMenu?.cancelTracking()
        music.importLyrics()
    }

    @objc private func showLyricLibrary() {
        if isShowingOptions { opensLibraryAfterMenu = true }
        else { openLyricLibrary() }
    }

    @objc private func selectSource(_ sender: NSMenuItem) {
        guard let identifier = sender.representedObject as? String,
              let source = MusicModel.LyricSource(rawValue: identifier) else { return }
        music.chooseLyricSource(source)
    }

    private final class OffsetMenuCard: NSView {
        private let music: MusicModel
        private let readout = NSTextField(labelWithString: "")
        private var buttons: [ToolButton] = []

        init(music: MusicModel, animated: Bool) {
            self.music = music
            super.init(frame: CGRect(x: 0, y: 0, width: 218, height: 48))
            autoresizingMask = [.width]
            readout.font = .monospacedDigitSystemFont(ofSize: 11, weight: .medium)
            readout.alignment = .center
            readout.textColor = .secondaryLabelColor
            readout.setAccessibilityIdentifier("LumiBar.attached.offset.value")
            addSubview(readout)
            for (index, item) in [("minus", "歌词延后 0.2 秒", "delay"),
                                  ("arrow.counterclockwise", "重置本曲歌词时移", "reset"),
                                  ("plus", "歌词提前 0.2 秒", "advance")].enumerated() {
                let button = ToolButton(symbol: item.0, name: item.1, identifier: "offset." + item.2)
                button.menuFeedback = animated
                button.toolTip = item.1
                button.tag = index
                button.target = self
                button.action = #selector(adjust(_:))
                addSubview(button)
                buttons.append(button)
            }
            refresh()
        }

        required init?(coder: NSCoder) { nil }

        override func layout() {
            super.layout()
            readout.frame = CGRect(x: 8, y: 26, width: bounds.width - 16, height: 16)
            let width = min(46, (bounds.width - 24) / 3)
            for (index, button) in buttons.enumerated() {
                button.frame = CGRect(x: bounds.midX + (CGFloat(index) - 1.5) * width,
                                      y: 2, width: width, height: 24)
            }
        }

        override func draw(_ dirtyRect: NSRect) {
            let card = NSBezierPath(roundedRect: bounds.insetBy(dx: 7, dy: 2), xRadius: 8, yRadius: 8)
            NSColor.labelColor.withAlphaComponent(0.065).setFill()
            card.fill()
        }

        func refresh() {
            let offset = music.offset
            let amount = offset.formatted(.number.precision(.fractionLength(0...1)).sign(strategy: .always(includingZero: false)))
            let caption = "本曲时移 · 目前 \(amount) 秒"
            if readout.stringValue != caption { readout.stringValue = caption }
            let hasTrack = music.track != nil
            buttons[0].isEnabled = hasTrack && offset > -60
            buttons[1].isEnabled = hasTrack && offset != 0
            buttons[2].isEnabled = hasTrack && offset < 60
        }

        @objc private func adjust(_ sender: NSButton) {
            music.adjustOffset(sender.tag == 0 ? -0.2 : sender.tag == 1 ? -music.offset : 0.2)
            refresh()
        }
    }

    private final class ToolButton: NSButton {
        private let feedback = CALayer()
        private var tracking: NSTrackingArea?
        private var hovered = false
        var menuFeedback: Bool?

        init(symbol: String, name: String, identifier: String, pointSize: CGFloat = 13, weight: NSFont.Weight = .semibold) {
            super.init(frame: .zero)
            image = NSImage(systemSymbolName: symbol, accessibilityDescription: nil)?
                .withSymbolConfiguration(.init(pointSize: pointSize, weight: weight))
            setAccessibilityLabel(name)
            setAccessibilityIdentifier("LumiBar.attached." + identifier)
            imagePosition = .imageOnly
            isBordered = false
            focusRingType = .none
            clipsToBounds = true
        }

        required init?(coder: NSCoder) { nil }

        override func acceptsFirstMouse(for event: NSEvent?) -> Bool { true }

        override func updateTrackingAreas() {
            super.updateTrackingAreas()
            guard menuFeedback != nil else { return }
            if let tracking { removeTrackingArea(tracking) }
            let area = NSTrackingArea(rect: .zero, options: [.mouseEnteredAndExited, .activeAlways, .inVisibleRect],
                                      owner: self, userInfo: nil)
            addTrackingArea(area)
            tracking = area
        }

        override func mouseEntered(with event: NSEvent) {
            hovered = true
            refreshMenuFeedback()
        }

        override func mouseExited(with event: NSEvent) {
            hovered = false
            refreshMenuFeedback()
        }

        override func highlight(_ flag: Bool) {
            super.highlight(flag)
            refreshMenuFeedback(pressed: flag)
        }

        private func refreshMenuFeedback(pressed: Bool = false) {
            guard let menuFeedback, let layer else { return }
            let opacity = isEnabled ? (pressed ? 1.0 : hovered ? 0.86 : 0) : 0
            contentTintColor = opacity > 0 ? .selectedMenuItemTextColor : nil
            var color = NSColor.clear.cgColor
            effectiveAppearance.performAsCurrentDrawingAppearance {
                color = NSColor.selectedContentBackgroundColor.withAlphaComponent(opacity).cgColor
            }
            guard layer.backgroundColor != color else { return }
            let previous = layer.presentation()?.backgroundColor ?? layer.backgroundColor
            CATransaction.begin()
            CATransaction.setDisableActions(true)
            layer.backgroundColor = color
            CATransaction.commit()
            if menuFeedback, let previous {
                let animation = CABasicAnimation(keyPath: "backgroundColor")
                animation.fromValue = previous
                animation.duration = 0.13
                animation.timingFunction = CAMediaTimingFunction(name: .easeOut)
                layer.add(animation, forKey: "menuHighlight")
            }
        }

        override func layout() {
            super.layout()
            if menuFeedback != nil {
                wantsLayer = true
                layer?.cornerRadius = 5
                if layer?.backgroundColor == nil { refreshMenuFeedback() }
                return
            }
            if feedback.superlayer == nil {
                wantsLayer = true
                feedback.cornerRadius = 5
                feedback.opacity = 0
                layer?.addSublayer(feedback)
            }
            CATransaction.begin()
            CATransaction.setDisableActions(true)
            feedback.frame = bounds.insetBy(dx: 0, dy: 3)
            effectiveAppearance.performAsCurrentDrawingAppearance {
                feedback.backgroundColor = NSColor.labelColor.cgColor
            }
            CATransaction.commit()
        }

        override func viewDidChangeEffectiveAppearance() {
            super.viewDidChangeEffectiveAppearance()
            needsLayout = true
            needsDisplay = true
        }

        func updateFeedback(_ opacity: Double, animated: Bool) {
            guard feedback.opacity != Float(opacity) else { return }
            let previous = feedback.presentation()?.opacity ?? feedback.opacity
            CATransaction.begin()
            CATransaction.setDisableActions(true)
            feedback.opacity = Float(opacity)
            CATransaction.commit()
            if animated {
                let animation = CABasicAnimation(keyPath: "opacity")
                animation.fromValue = previous
                animation.duration = 0.13
                animation.timingFunction = CAMediaTimingFunction(name: .easeOut)
                feedback.add(animation, forKey: "opacity")
            } else {
                feedback.removeAnimation(forKey: "opacity")
            }
        }
    }
}
