import AppKit
import CoreText
import QuartzCore
import SwiftUI

@MainActor
final class NotchLyricsPanel {
    private let music: MusicModel
    private let appearance: AppearanceModel
    private let openLyricLibrary: () -> Void
    private let panel = NotchLyricsWindow()
    private let triggerPanel = NotchLyricsWindow(interactive: true)
    private let trigger = NotchLyricsTrigger()
    private let separateBackdrop = NotchLyricsWindow()
    private let expandedPanel = NotchLyricsWindow(interactive: true, keyable: true)
    private let leadingWing = NotchLyricsWindow(interactive: true, keyable: true)
    private let trailingWing = NotchLyricsWindow()
    private let hintPanel = NotchLyricsWindow()
    private let hintView = NotchLyricsHintView()
    private let header = AttachedLyricsHeader()
    private lazy var tools = AttachedLyricsTools(music: music, prepareMenu: { [weak self] in
        self?.hideHint()
        self?.hoverBeganAt = .now
    }, openLyricLibrary: { [weak self] in
        self?.collapse()
        self?.openLyricLibrary()
    })
    private let expansion = AttachedNotchExpansion()
    private let view = NotchLyricsView()
    private let glass = NSVisualEffectView()
    private let glassGeometry = NotchGlassGeometry()
    private lazy var systemGlass = host(NotchSystemGlass(geometry: glassGeometry, expansion: expansion))
    private let content = NSView()
    private let compactClip = NSView()
    private var expandedTint: NSView?
    private var closingTint: NSView?
    private let mask = CAShapeLayer()
    private var lastBorder: NotchLyricsBorder?
    private var presentation = NotchTextLayout()
    private var fontChoice: FontChoice?
    private var font = NSFont.systemFont(ofSize: 11.5, weight: .bold)
    private var chineseFont = NSFont.systemFont(ofSize: 11.5, weight: .bold)
    private var rowHeight: CGFloat = 0
    private var textRowHeight: CGFloat = 0
    private var lastStyle: NotchLyricsStyle?
    private var dimmedForPointer = false
    private var compactOutline: CompactNotchOutline?
    private var compactOpenness: CGFloat = 0
    private var compactTransition: CompactTransition?
    private var compactDisplayLink: CADisplayLink?
    private var restoresCompactImmediately = false
    private var hoverBeganAt: Date?
    var isTrackingMenu = false {
        didSet { if isTrackingMenu { hideHint() } }
    }
    private var isExpanded = false
    private var isActive = false
    private var missingTrackSince: Date?
    private var pauseBegan: (trackKey: String, instant: ContinuousClock.Instant)?
    private var shellGeometry: ShellGeometry?
    private var transition: Task<Void, Never>?
    private var collapseToken: UUID?

    private struct FontChoice: Equatable {
        let size: CGFloat
        let typography: LyricTypography
    }

    private struct ShellGeometry: Equatable {
        let collapsedWidth: CGFloat
        let collapsedHeight: CGFloat
        let squareTop: Bool
        let roundsTop: Bool
        let glassInsets: NotchGlassInsets

        var usesNativeGlass: Bool { glassInsets.outset > 0 }
    }

    private struct CompactTransition {
        let from: CGFloat
        let target: CGFloat
        let startedAt: CFTimeInterval

        func openness(at time: CFTimeInterval) -> CGFloat {
            let duration = max(0.07, (target == 1 ? 0.22 : 0.20) * abs(target - from))
            let progress = min(1, max(0, (time - startedAt) / duration))
            let eased = target == 1 ? 1 - pow(1 - progress, 2.35) : progress * progress * (3 - 2 * progress)
            return progress == 1 ? target : from + (target - from) * eased
        }
    }

    private var expandedControls: [NotchLyricsWindow] { [expandedPanel, leadingWing, trailingWing] }
    private var expandedBackdrop: NotchLyricsWindow { shellGeometry?.usesNativeGlass == true ? panel : separateBackdrop }
    private var expandedBackdropVisible: Bool { shellGeometry != nil && expandedBackdrop.isVisible }
    private var animatesExpansion: Bool {
        appearance.animation && !NSWorkspace.shared.accessibilityDisplayShouldReduceMotion
    }

    init(music: MusicModel, appearance: AppearanceModel, openLyricLibrary: @escaping () -> Void) {
        self.music = music
        self.appearance = appearance
        self.openLyricLibrary = openLyricLibrary
        content.wantsLayer = true
        compactClip.wantsLayer = true
        compactClip.layer?.mask = mask
        compactClip.autoresizingMask = [.width, .height]
        systemGlass.autoresizingMask = [.width, .height]
        systemGlass.isHidden = true
        glass.blendingMode = .behindWindow
        glass.material = .hudWindow
        glass.state = .active
        glass.autoresizingMask = [.width, .height]
        view.autoresizingMask = [.width, .height]
        content.addSubview(systemGlass)
        compactClip.addSubview(glass)
        compactClip.addSubview(view)
        content.addSubview(compactClip)
        panel.contentView = content
        panel.title = "Lumi 歌词速览"

        trigger.isBordered = false
        trigger.focusRingType = .none
        trigger.target = self
        trigger.action = #selector(toggle)
        trigger.setAccessibilityIdentifier("LumiBar.notchLyrics.toggle")
        trigger.autoresizingMask = [.width, .height]
        triggerPanel.hasShadow = false
        triggerPanel.level = NSWindow.Level(rawValue: NSWindow.Level.statusBar.rawValue + 1)
        triggerPanel.contentView = trigger

        separateBackdrop.title = "Lumi 歌词玻璃背景"
        expandedPanel.title = "Lumi 歌词详情"
        leadingWing.title = "Lumi 歌词工具"
        trailingWing.title = "Lumi 歌词来源"
        for window in [panel, separateBackdrop, expandedPanel, leadingWing, trailingWing] {
            window.hasShadow = false
        }
        hintPanel.title = "Lumi 歌词提示"
        hintPanel.level = NSWindow.Level(rawValue: NSWindow.Level.statusBar.rawValue + 1)
        hintPanel.contentView = hintView
        expandedPanel.onCancel = { [weak self] in self?.collapse() }
        leadingWing.onCancel = { [weak self] in self?.collapse() }
    }

    var canExpand: Bool {
        isActive && music.notchLyrics.enabled && !hidesForPause && (isExpanded || music.track != nil)
    }
    var expanded: Bool { isExpanded }

    private var hidesForPause: Bool {
        guard music.notchLyrics.hidesWhenPaused, !(isExpanded && music.awaitingTrackNavigation),
              let track = music.track, !track.playing,
              let pauseBegan, pauseBegan.trackKey == track.key else { return false }
        return pauseBegan.instant.duration(to: .now) >= .seconds(5)
    }

    @objc func toggle() {
        guard canExpand else { return }
        if isExpanded {
            collapse()
        } else {
            transition?.cancel()
            transition = nil
            collapseToken = nil
            let reopening = expandedBackdropVisible
            isExpanded = true
            expandedPanel.ignoresMouseEvents = false
            leadingWing.ignoresMouseEvents = false
            if !reopening {
                let close: () -> Void = { [weak self] in self?.collapse() }
                expandedPanel.contentView = host(MusicView(model: music, appearance: appearance, surface: .attachedLyrics, toggle: close))
                leadingWing.contentView = tools
                trailingWing.contentView = host(AttachedLyricsSources(music: music, appearance: appearance, header: header))
            }
            refresh(active: isActive)
            if reopening { openExpanded() }
        }
    }

    func refresh(active: Bool) {
        let snapshot = music.notchLyrics
        let screen = NSScreen.screens.first(where: { $0.safeAreaInsets.top > 0 }) ?? NSScreen.main
        isActive = active && screen.map { !snapshot.hidesInFullScreen || !DisplaySpaces.isFullScreen(on: $0) } == true
        // Players can briefly report a paused or empty session while moving between tracks.
        if snapshot.hidesWhenPaused, let track = music.track, !track.playing {
            if pauseBegan?.trackKey != track.key { pauseBegan = (track.key, .now) }
        } else {
            pauseBegan = nil
        }
        let previousBody = glassGeometry.insets.bodyFrame(in: panel.frame)
        let animatesHide = lastStyle != nil && isActive && animatesExpansion && screen?.frame.contains(previousBody) == true
        guard isActive, snapshot.enabled, !hidesForPause, let screen else {
            restoresCompactImmediately = false
            hide(animateCompact: animatesHide)
            return
        }
        if music.track != nil { missingTrackSince = nil }
        else if !isExpanded {
            let since = missingTrackSince ?? .now
            missingTrackSince = since
            if panel.isVisible, lastStyle != nil, compactTransition?.target != 0,
               Date.now.timeIntervalSince(since) < 0.55 { return }
            restoresCompactImmediately = false
            hide(animateCompact: animatesHide)
            return
        }
        let optionsAppearance: NSAppearance? = switch snapshot.theme {
        case "Light": NSAppearance(named: .aqua)
        case "Dark": NSAppearance(named: .darkAqua)
        default: nil
        }
        let topInset = screen.safeAreaInsets.top
        guard screen.safeAreaInsets.top > 0,
              let left = screen.auxiliaryTopLeftArea, let right = screen.auxiliaryTopRightArea else {
            if !isExpanded && !expandedBackdropVisible { hideCompact() }
            triggerPanel.orderOut(nil)
            if isExpanded {
                showExpanded(on: screen, center: screen.visibleFrame.midX, reservedWidth: 0,
                             collapsedWidth: 0, collapsedHeight: 0, triggerFrame: nil, appearance: optionsAppearance)
                if !snapshot.usesSystemGlassBorder { hideCompact() }
            }
            return
        }
        let cameraCenter = (left.maxX + right.minX) / 2
        // The macOS 27.3 playback capsule is drawn two points left of the camera center.
        let center = if #available(macOS 27.3, *) { cameraCenter - 2 } else { cameraCenter }
        let cameraWidth = right.minX - left.maxX
        let capsuleWidth = max(cameraWidth, 140)
        let glassInsets = NotchGlassInsets(system: snapshot.usesSystemGlassBorder)
        let width = min(screen.frame.width - 2 * glassInsets.outset, max(capsuleWidth + 52, snapshot.width))
        let choice = FontChoice(size: snapshot.fontSize,
                                typography: LyricTypography(options: snapshot, appearance: appearance))
        let fontChanged = fontChoice != choice
        if fontChanged {
            (font, chineseFont) = choice.typography.fonts(size: choice.size)
            fontChoice = choice
            (rowHeight, textRowHeight) = NotchTextLayout.rowHeights(for: font, chineseFont: chineseFont)
            presentation = NotchTextLayout()
        }
        let layout = snapshot.compactLayout(cameraHeight: topInset, rowHeight: rowHeight,
                                            textRowHeight: textRowHeight, fontPointSize: font.pointSize)
        let height = layout.height
        if music.minimumCompactCameraOffset != layout.minimumOffset {
            music.minimumCompactCameraOffset = layout.minimumOffset
        }
        let triggerFrame = NSRect(x: center - capsuleWidth / 2 - 24, y: screen.frame.maxY - topInset * 0.56 - 11,
                                  width: 22, height: 22)
        if isExpanded {
            showExpanded(on: screen, center: center, reservedWidth: max(cameraWidth + 22, 162),
                         collapsedWidth: width, collapsedHeight: height, triggerFrame: triggerFrame,
                         appearance: optionsAppearance)
            if !snapshot.usesSystemGlassBorder { hideCompact() }
            return
        }
        if expandedBackdropVisible {
            updateHeader(triggerFrame: triggerFrame, screen: screen, appearance: optionsAppearance)
            return
        }
        guard let track = music.track else { return }

        let now = Date.now
        let offset = music.playbackCalibration + music.offset
        let position = track.lyricPosition(at: now, offset: offset)
        let effectiveAppearance = optionsAppearance ?? NSApp.effectiveAppearance
        let dark = effectiveAppearance.bestMatch(from: [.aqua, .darkAqua]) == .darkAqua
        let foreground = (snapshot.useCoverColors ? music.colors.first : nil) ?? .accentColor
        let alternate = snapshot.useCoverColors ? (music.colors.dropFirst().first ?? foreground) : foreground
        let style = NotchLyricsStyle(options: snapshot, dark: dark, primary: foreground, secondary: alternate,
                                    density: appearance.glassDensity)
        if style != lastStyle {
            panel.appearance = optionsAppearance
            panel.presentsNativeGlass = snapshot.usesSystemGlassBorder
            glass.material = dark ? .hudWindow : .popover
            glass.isHidden = snapshot.usesSystemGlassBorder
            systemGlass.isHidden = !snapshot.usesSystemGlassBorder
            if snapshot.usesSystemGlassBorder { glassGeometry.compactOpenness = compactOpenness }
            lastStyle = style
        }

        let rows = Self.lyrics(music.lyrics, track: track, position: position, offset: offset,
                               options: snapshot, acceptsManualText: music.lyricsAreLocal)
        let textWidth = max(1, width - 16)
        let firstLineScale = 1 + snapshot.firstLineExtraSize / font.pointSize
        let lines = presentation.compactRows(rows.items, font: font, chineseFont: chineseFont,
                                             width: textWidth, firstLineScale: firstLineScale,
                                             position: position,
                                             split: snapshot.splitLongLines, karaoke: snapshot.karaoke)
        let frame = NSRect(x: center - width / 2, y: screen.frame.maxY - height, width: width, height: height)
        let windowFrame = glassInsets.windowFrame(around: frame)
        let resized = compactClip.bounds.size != frame.size
        if closingTint == nil, panel.frame != windowFrame { panel.setFrame(windowFrame, display: false) }
        let clipFrame = frame.offsetBy(dx: -panel.frame.minX, dy: -panel.frame.minY)
        let outline = CompactNotchOutline(cameraSize: CGSize(width: cameraWidth, height: topInset),
                                         roundsTop: snapshot.roundsTopCorners)
        let borderStyle = NotchLyricsBorder(music: music, dark: dark)
        if resized || compactClip.frame != clipFrame || compactOutline != outline || glassGeometry.insets != glassInsets {
            compactOutline = outline
            glassGeometry.compactOutline = outline
            glassGeometry.insets = glassInsets
            CATransaction.begin()
            CATransaction.setDisableActions(true)
            systemGlass.frame = content.bounds
            compactClip.frame = clipFrame
            glass.frame = compactClip.bounds
            view.frame = compactClip.bounds
            mask.frame = compactClip.bounds
            CATransaction.commit()
            applyCompactAppearance(compactOpenness)
            if resized { view.needsDisplay = true }
        }
        if borderStyle != lastBorder {
            view.updateBorder(borderStyle, path: mask.path)
            lastBorder = borderStyle
        }
        let reduceMotion = NSWorkspace.shared.accessibilityDisplayShouldReduceMotion
        let motion = snapshot.glow && appearance.animation && track.playing && !reduceMotion
        view.update(style: style, lines: lines, rowHeight: rowHeight,
                    padding: layout.bottomPadding, textCeiling: layout.textCeiling,
                    spacing: snapshot.lineSpacing, firstLineScale: firstLineScale,
                    countdown: rows.countdown, phase: motion ? music.lyricGlowTime(expanded: false) : nil,
                    animatesChanges: panel.isVisible && compactOpenness == 1 && animatesExpansion && !resized && !fontChanged,
                    label: ([rows.countdown?.caption].compactMap { $0 } + lines.filter { $0.kind != .countdown }.map(\.text))
                        .joined(separator: "\n"))
        let pointer = NSEvent.mouseLocation
        let localPointer = CGPoint(x: pointer.x - frame.minX, y: pointer.y - frame.minY)
        let shouldDim = snapshot.fadesOnHover && (mask.path?.contains(localPointer) == true)
        if shouldDim != dimmedForPointer || !panel.isVisible {
            dimmedForPointer = shouldDim
            let alpha: CGFloat = shouldDim ? 0.36 : 1
            if reduceMotion || !panel.isVisible {
                panel.alphaValue = alpha
            } else {
                NSAnimationContext.runAnimationGroup { context in
                    context.duration = 0.26
                    context.timingFunction = CAMediaTimingFunction(name: .easeInEaseOut)
                    panel.animator().alphaValue = alpha
                }
            }
        }
        setCompactVisible(true, animated: animatesExpansion && !restoresCompactImmediately)
        restoresCompactImmediately = false
        updateHeader(triggerFrame: compactOpenness == 1 ? triggerFrame : nil, screen: screen, appearance: optionsAppearance)
    }

    private func updateHeader(triggerFrame: NSRect?, screen: NSScreen, appearance: NSAppearance?,
                              leading: NSRect = .zero, trailing: NSRect = .zero) {
        let hit = headerHit(at: NSEvent.mouseLocation, trigger: triggerFrame, leading: leading, trailing: trailing)
        if header.hovered != hit?.item {
            header.hovered = hit?.item
            hoverBeganAt = hit == nil ? nil : .now
            hideHint()
        }
        if header.pressed != nil, NSEvent.pressedMouseButtons & 1 == 0 { header.pressed = nil }
        if isExpanded, !isTrackingMenu, let hit, hit.item != .trigger,
           header.pressed == nil || (hit.item.adjustsVolume && header.pressed == hit.item), let hoverBeganAt,
           Date.now.timeIntervalSince(hoverBeganAt) >= 0.34 {
            showHint(for: hit.item, anchor: hit.frame, frame: screen.visibleFrame, appearance: appearance)
        } else {
            hideHint()
        }
        let cover = AttachedLyricsHeader.coverColor(music: music, appearance: self.appearance)
        if isExpanded { tools.update(header: header, cover: cover, animated: animatesExpansion) }
        guard let triggerFrame else {
            if triggerPanel.isVisible { triggerPanel.orderOut(nil) }
            return
        }
        triggerPanel.appearance = appearance
        if triggerPanel.frame != triggerFrame { triggerPanel.setFrame(triggerFrame, display: true) }
        trigger.update(expanded: isExpanded, header: header, cover: cover)
        if !triggerPanel.isVisible { triggerPanel.orderFrontRegardless() }
    }

    func headerMouseChanged(isDown: Bool, at point: NSPoint) {
        guard isDown, !isTrackingMenu else {
            header.pressed = nil
            return
        }
        let hit = headerHit(at: point, trigger: triggerPanel.isVisible ? triggerPanel.frame : nil,
                            leading: isExpanded && leadingWing.isVisible ? leadingWing.frame : .zero,
                            trailing: isExpanded && trailingWing.isVisible ? trailingWing.frame : .zero)
        header.pressed = hit?.item
        if let hit, hit.item.adjustsVolume {
            if header.hovered != hit.item { hideHint() }
            header.hovered = hit.item
            hoverBeganAt = .distantPast
        } else if hit != nil {
            hoverBeganAt = .now
            hideHint()
        }
    }

    private func headerHit(at pointer: NSPoint, trigger: NSRect?, leading: NSRect, trailing: NSRect)
        -> (item: AttachedLyricsHeader.Item, frame: NSRect)? {
        if let trigger, trigger.contains(pointer) { return (.trigger, trigger) }
        if let hit = AttachedLyricsTools.target(at: pointer, in: leading) {
            return hit
        }
        if trailing.contains(pointer), music.track != nil {
            for item in [AttachedLyricsHeader.Item.player, .source] {
                guard let measured = header.sourceBounds[item] else { continue }
                let frame = NSRect(x: trailing.minX + measured.minX, y: trailing.maxY - measured.maxY,
                                   width: measured.width, height: measured.height).intersection(trailing)
                if frame.contains(pointer) { return (item, frame) }
            }
        }
        return nil
    }

    private func showHint(for item: AttachedLyricsHeader.Item, anchor: NSRect, frame: NSRect, appearance: NSAppearance?) {
        let label = switch item {
        case .trigger: ""
        case .options: "歌词选项"
        case .volumeDown: "降低音量一格"
        case .volumeUp: "提高音量一格"
        case .player: "音乐来源 · \(music.track?.player.name ?? "无")"
        case .source: AttachedLyricsHeader.lyricSourceHint(for: music)
        }
        hintPanel.appearance = appearance
        let size = hintView.setLabel(label)
        let x = min(frame.maxX - size.width - 4, max(frame.minX + 4, anchor.midX - size.width / 2))
        let y = max(frame.minY + 4, anchor.minY - size.height - 7)
        let target = NSRect(origin: NSPoint(x: x, y: y), size: size)
        if hintPanel.frame != target { hintPanel.setFrame(target, display: true) }
        if !hintPanel.isVisible { hintPanel.orderFrontRegardless() }
    }

    private func hideHint() {
        if hintPanel.isVisible { hintPanel.orderOut(nil) }
    }

    func hide() {
        if panel.isVisible, (compactTransition?.target ?? compactOpenness) > 0 {
            restoresCompactImmediately = true
        }
        hide(animateCompact: false)
    }

    private func hide(animateCompact: Bool) {
        let shrinks = animateCompact && !isExpanded && !expandedBackdropVisible
        isExpanded = false
        header.hovered = nil
        header.pressed = nil
        hoverBeganAt = nil
        hideHint()
        removeExpandedWindows()
        hideCompact(animated: shrinks)
        if triggerPanel.isVisible { triggerPanel.orderOut(nil) }
    }

    private func hideCompact(animated: Bool = false) {
        view.cancelFade()
        setCompactVisible(false, animated: animated)
    }

    private func setCompactVisible(_ visible: Bool, animated: Bool) {
        let target: CGFloat = visible ? 1 : 0
        if animated, compactTransition?.target == target { return }
        guard animated, compactOutline != nil, compactOpenness != target else {
            finishCompactTransition(at: target)
            return
        }
        compactTransition = CompactTransition(from: compactOpenness, target: target, startedAt: CACurrentMediaTime())
        if visible, !panel.isVisible { panel.orderFrontRegardless() }
        if compactDisplayLink == nil {
            let link = content.displayLink(target: self, selector: #selector(advanceCompactTransition(_:)))
            link.add(to: .main, forMode: .common)
            compactDisplayLink = link
        }
    }

    @objc private func advanceCompactTransition(_ link: CADisplayLink) {
        guard let transition = compactTransition else { return }
        let amount = transition.openness(at: link.targetTimestamp)
        if amount == transition.target || !animatesExpansion {
            finishCompactTransition(at: transition.target)
        } else {
            applyCompactAppearance(amount)
        }
    }

    private func finishCompactTransition(at target: CGFloat) {
        compactTransition = nil
        compactDisplayLink?.invalidate()
        compactDisplayLink = nil
        if compactOpenness != target { applyCompactAppearance(target) }
        if target == 0 {
            if panel.isVisible { panel.orderOut(nil) }
        } else if !panel.isVisible {
            panel.orderFrontRegardless()
        }
    }

    private func applyCompactAppearance(_ amount: CGFloat) {
        compactOpenness = amount
        guard let outline = compactOutline else { return }
        let path = outline.path(in: compactClip.bounds, openness: amount)
        let glassOpacity = min(1, amount / 0.34)
        let lyricsOpacity = min(1, max(0, (amount - 0.72) / 0.25))
        CATransaction.begin()
        CATransaction.setDisableActions(true)
        mask.path = path
        if !systemGlass.isHidden { glassGeometry.compactOpenness = amount }
        content.layer?.opacity = Float(glassOpacity * glassOpacity * (3 - 2 * glassOpacity))
        view.moveBorder(to: path)
        view.setLyricsOpacity(lyricsOpacity * lyricsOpacity * (3 - 2 * lyricsOpacity))
        CATransaction.commit()
    }

    func dismissIfOutside(_ point: NSPoint) {
        guard isExpanded, isTrackingMenu == false, !tools.isShowingOptions,
              let geometry = shellGeometry, expandedBackdrop.isVisible,
              !(triggerPanel.isVisible && triggerPanel.frame.contains(point)),
              !(leadingWing.isVisible && leadingWing.frame.contains(point)) else { return }
        let frame = expandedBackdrop.frame
        let local = CGPoint(x: point.x - frame.minX, y: frame.maxY - point.y)
        let outline = AttachedNotchOutline(openness: 1, collapsedWidth: geometry.collapsedWidth,
                                          collapsedHeight: geometry.collapsedHeight, squareTop: geometry.squareTop,
                                          roundsTop: geometry.roundsTop, glassInsets: geometry.glassInsets)
        if !outline.path(in: CGRect(origin: .zero, size: frame.size)).contains(local) { collapse() }
    }

    private func collapse() {
        guard isExpanded else { return }
        isExpanded = false
        header.pressed = nil
        hideHint()
        expandedPanel.ignoresMouseEvents = true
        leadingWing.ignoresMouseEvents = true
        transition?.cancel()
        refresh(active: isActive)
        guard animatesExpansion, expandedBackdropVisible else {
            finishCollapse(blendingTint: false)
            return
        }
        fadeControls(to: 0, duration: 0.12)
        let token = UUID()
        collapseToken = token
        transition = Task { @MainActor [weak self] in
            do { try await Task.sleep(for: .milliseconds(85)) } catch { return }
            guard let self, !self.isExpanded else { return }
            withAnimation(.timingCurve(0.22, 0.76, 0.33, 1, duration: 0.31), completionCriteria: .removed) {
                self.expansion.openness = 0
            } completion: { [weak self] in
                guard let self, self.collapseToken == token, !self.isExpanded else { return }
                self.finishCollapse(blendingTint: true)
            }
        }
    }

    private func finishCollapse(blendingTint: Bool) {
        removeExpandedWindows(blendingTint: blendingTint && animatesExpansion && music.track != nil)
        restoresCompactImmediately = true
        refresh(active: isActive)
        guard let closingTint else { return }
        NSAnimationContext.runAnimationGroup { context in
            context.duration = 0.18
            context.timingFunction = CAMediaTimingFunction(name: .easeInEaseOut)
            compactClip.animator().alphaValue = 1
            closingTint.animator().alphaValue = 0
        } completionHandler: { [weak self, weak closingTint] in
            Task { @MainActor in
                guard let self, let closingTint, self.closingTint === closingTint else { return }
                self.clearClosingTint()
                self.refresh(active: self.isActive)
            }
        }
    }

    private func clearClosingTint() {
        if let closingTint {
            closingTint.removeFromSuperview()
            self.closingTint = nil
            glassGeometry.expandedOutline = nil
            compactClip.alphaValue = 1
        }
    }

    private func removeExpandedWindows(blendingTint: Bool = false) {
        transition?.cancel()
        transition = nil
        collapseToken = nil
        clearClosingTint()
        for window in expandedControls where window.contentView != nil {
            window.orderOut(nil)
            window.contentView = nil
        }
        if let shellGeometry {
            detachExpandedBackdrop(shellGeometry, blendingTint: blendingTint)
        }
        shellGeometry = nil
        header.sourceBounds.removeAll()
        expansion.openness = 0
    }

    private func detachExpandedBackdrop(_ geometry: ShellGeometry, blendingTint: Bool = false) {
        if geometry.usesNativeGlass {
            panel.title = "Lumi 歌词速览"
            if blendingTint, let expandedTint {
                // Preserve the rendered shape and its backing size until the
                // tint has faded. Resizing here re-samples the glass mid-fade.
                expandedTint.autoresizingMask = []
                closingTint = expandedTint
                compactClip.alphaValue = 0
            } else {
                expandedTint?.removeFromSuperview()
                glassGeometry.expandedOutline = nil
                compactClip.alphaValue = 1
            }
            expandedTint = nil
            compactClip.isHidden = false
            lastStyle = nil
        } else {
            separateBackdrop.orderOut(nil)
            separateBackdrop.contentView = nil
        }
    }

    private func host<Content: View>(_ rootView: Content) -> NSView {
        let view = NotchLyricsHostingView(rootView: rootView)
        view.sizingOptions = []
        return view
    }

    private func showExpanded(on screen: NSScreen, center: CGFloat, reservedWidth: CGFloat,
                              collapsedWidth: CGFloat, collapsedHeight: CGFloat, triggerFrame: NSRect?,
                              appearance: NSAppearance?) {
        let safeFrame = screen.visibleFrame
        let top = reservedWidth > 0 ? screen.frame.maxY : safeFrame.maxY
        let cameraHeight = reservedWidth > 0 ? screen.safeAreaInsets.top : 0
        let leftEnd = min(center - reservedWidth / 2 - 4, triggerFrame.map { $0.minX - 2 } ?? center)
        let leadingWidth = AttachedLyricsTools.width
        let leadingMargin = 8 - AttachedLyricsTools.sideInset
        let widthForLeadingTools = 2 * (center - leftEnd + leadingMargin + leadingWidth)
        let nativeGlass = music.notchLyrics.usesSystemGlassBorder
        let glassInsets = NotchGlassInsets(system: nativeGlass)
        let horizontalMargin = max(16, 2 * glassInsets.outset)
        let bottom = max(safeFrame.minY + 8, screen.frame.minY + glassInsets.outset)
        let width = min(max(music.notchLyrics.expandedWidth, collapsedWidth, widthForLeadingTools), safeFrame.width - horizontalMargin)
        let height = min(music.notchLyrics.expandedHeight, top - bottom)
        guard width > 0, height > cameraHeight + 90 else { return }
        let x = min(safeFrame.maxX - width - glassInsets.outset,
                    max(safeFrame.minX + glassInsets.outset, center - width / 2))
        let frame = NSRect(x: x, y: top - height, width: width, height: height)
        let body = NSRect(x: x, y: frame.minY, width: width, height: height - cameraHeight)
        let headerInset: CGFloat = 22
        let leftStart = max(frame.minX + leadingMargin,
                            min(frame.minX + headerInset - AttachedLyricsTools.sideInset, leftEnd - leadingWidth))
        let rightStart = center + reservedWidth / 2 + 4
        let toolsHeight: CGFloat = 28
        let toolsY = triggerFrame.map { $0.midY - toolsHeight / 2 } ?? body.maxY - toolsHeight - 14
        let left = NSRect(x: leftStart, y: toolsY, width: min(leadingWidth, max(0, leftEnd - leftStart)), height: toolsHeight)
        let right = NSRect(x: rightStart, y: toolsY,
                           width: max(0, frame.maxX - headerInset - rightStart), height: toolsHeight)
        updateHeader(triggerFrame: triggerFrame, screen: screen, appearance: appearance,
                     leading: left, trailing: right)
        let geometry = ShellGeometry(collapsedWidth: collapsedWidth,
                                     collapsedHeight: max(cameraHeight + 22, collapsedHeight),
                                     squareTop: cameraHeight > 0, roundsTop: music.notchLyrics.roundsTopCorners,
                                     glassInsets: glassInsets)
        let opening = !expandedBackdropVisible
        let geometryChanged = shellGeometry != geometry
        if geometryChanged {
            if let old = shellGeometry, old.usesNativeGlass != nativeGlass {
                detachExpandedBackdrop(old)
            }
            shellGeometry = geometry
            expandedBackdrop.presentsNativeGlass = nativeGlass
            // The controls sit above the glass. They still accept clicks and may
            // request keyboard input, while the backdrop supplies the native optics.
            expandedPanel.becomesKeyOnlyIfNeeded = nativeGlass
            leadingWing.becomesKeyOnlyIfNeeded = nativeGlass
            let outline = AttachedNotchOutline(openness: 0, collapsedWidth: geometry.collapsedWidth,
                                               collapsedHeight: geometry.collapsedHeight, squareTop: geometry.squareTop,
                                               roundsTop: geometry.roundsTop, glassInsets: glassInsets)
            let shell = host(AttachedNotchShell(expansion: expansion, music: music, outline: outline) {
                AttachedLyricsBackdrop(music: music, appearance: self.appearance)
            })
            if nativeGlass {
                // The same glass view renders both outlines. The lyrics and tint
                // can change without replacing the WindowServer glass surface.
                compactTransition = nil
                compactDisplayLink?.invalidate()
                compactDisplayLink = nil
                view.cancelFade()
                clearClosingTint()
                compactClip.isHidden = true
                systemGlass.isHidden = false
                CATransaction.begin()
                CATransaction.setDisableActions(true)
                content.layer?.opacity = 1
                CATransaction.commit()
                glassGeometry.expandedOutline = outline
                expandedTint?.removeFromSuperview()
                expandedTint = shell
                shell.frame = content.bounds
                shell.autoresizingMask = [.width, .height]
                content.addSubview(shell, positioned: .below, relativeTo: compactClip)
                panel.title = "Lumi 歌词玻璃背景"
                dimmedForPointer = false
                panel.alphaValue = 1
            } else {
                separateBackdrop.contentView = shell
            }
        }
        let windows: [(NotchLyricsWindow, NSRect)] = [(expandedBackdrop, geometry.glassInsets.windowFrame(around: frame)), (expandedPanel, body),
                                                     (leadingWing, left), (trailingWing, right)]
        for (window, windowFrame) in windows {
            window.appearance = appearance
            if window.frame != windowFrame { window.setFrame(windowFrame, display: window !== panel) }
            if !window.isVisible {
                window.alphaValue = window === expandedBackdrop || !animatesExpansion ? 1 : 0
                window.orderFrontRegardless()
            }
        }
        if nativeGlass && (opening || geometryChanged) {
            for window in expandedControls where window.isVisible {
                window.order(.above, relativeTo: expandedBackdrop.windowNumber)
            }
        }
        if opening { openExpanded() }
    }

    private func openExpanded() {
        guard animatesExpansion else {
            expansion.openness = 1
            for window in expandedControls { window.alphaValue = 1 }
            return
        }
        transition = Task { @MainActor [weak self] in
            do { try await Task.sleep(for: .milliseconds(36)) } catch { return }
            guard let self, self.isExpanded else { return }
            withAnimation(.spring(response: 0.47, dampingFraction: 0.75)) { self.expansion.openness = 1 }
            do { try await Task.sleep(for: .milliseconds(225)) } catch { return }
            guard self.isExpanded else { return }
            self.fadeControls(to: 1, duration: 0.22)
            self.transition = nil
        }
    }

    private func fadeControls(to opacity: CGFloat, duration: TimeInterval) {
        NSAnimationContext.runAnimationGroup { context in
            context.duration = duration
            context.timingFunction = CAMediaTimingFunction(name: .easeInEaseOut)
            for window in expandedControls { window.animator().alphaValue = opacity }
        }
    }

    private static func lyrics(_ lyrics: [Lyric], track: Track, position: Double, offset: Double,
                               options: NotchLyricsOptions,
                               acceptsManualText: Bool) -> (items: [NotchTextRow], countdown: LyricCountdown?) {
        if options.showCountdown,
           let countdown = LyricCountdown.current(in: lyrics, track: track, position: position, offset: offset,
                                                   acceptsManualText: acceptsManualText) {
            let next = lyrics[countdown.nextIndex]
            let preview = NotchTextRow(text: next.text, words: next.words, kind: .following)
            let dots = NotchTextRow(text: "•••", kind: .countdown, canSplit: false)
            return ([dots, preview], countdown)
        }
        if let first = Lyric.firstVocal(in: lyrics, for: track, acceptsManualText: acceptsManualText) {
            let firstVocal = first.time
            if position >= firstVocal {
                let usesVocalTiming = options.showCountdown && lyrics[first.index].isVocalLine
                let current = lyrics[first.index...].lastIndex {
                    usesVocalTiming ? $0.vocalStartTime <= position && $0.isVocalLine : $0.time <= position
                } ?? first.index
                let line = lyrics[current]
                let upcoming = lyrics.dropFirst(current + 1)
                let nextCue = usesVocalTiming ? upcoming.first(where: \.isVocalLine) : upcoming.first
                let lineStart = usesVocalTiming ? line.vocalStartTime : line.time
                let lineEnd = nextCue.map { usesVocalTiming ? $0.vocalStartTime : $0.time } ?? max(track.duration, lineStart + 1)
                var rows = [NotchTextRow(text: line.text, words: line.words, kind: .current, start: lineStart, end: lineEnd)]
                if options.showTranslation, let translation = line.translation, !translation.isEmpty {
                    rows.append(NotchTextRow(text: translation, kind: .translation, start: lineStart, end: lineEnd))
                } else if let nextCue {
                    rows.append(NotchTextRow(text: nextCue.text, kind: .following, canSplit: false))
                }
                return (rows, nil)
            }
        }
        guard options.showIntroMetadata else { return ([], nil) }
        let title = NotchTextRow(text: track.title, canSplit: false)
        let artist = NotchTextRow(text: track.artist, kind: .secondary, canSplit: false)
        return (track.artist.isEmpty ? [title] : [title, artist], nil)
    }
}

private final class NotchLyricsWindow: NSPanel {
    private let keyable: Bool
    var presentsNativeGlass = false
    var onCancel: (() -> Void)?

    init(interactive: Bool = false, keyable: Bool = false) {
        self.keyable = keyable
        super.init(contentRect: .zero, styleMask: [.borderless, .nonactivatingPanel], backing: .buffered, defer: false)
        isOpaque = false
        backgroundColor = .clear
        isReleasedWhenClosed = false
        hidesOnDeactivate = false
        ignoresMouseEvents = !interactive
        isMovable = false
        hasShadow = true
        level = .statusBar
        collectionBehavior = [.moveToActiveSpace, .stationary, .fullScreenAuxiliary, .ignoresCycle]
        animationBehavior = .none
    }

    // Passive glass needs key eligibility for full specular highlights. Its
    // nonactivating window ignores mouse events and is ordered without requesting key.
    override var canBecomeKey: Bool { keyable || presentsNativeGlass }
    override var canBecomeMain: Bool { false }
    override func constrainFrameRect(_ frameRect: NSRect, to screen: NSScreen?) -> NSRect { frameRect }
    override func cancelOperation(_ sender: Any?) { onCancel?() }
}

@MainActor
private final class NotchLyricsTrigger: NSButton {
    private var expanded: Bool?
    private var headerHighlighted: Bool?
    private var coverTint: NSColor?
    private var backgroundOpacity = 0.0

    override func acceptsFirstMouse(for event: NSEvent?) -> Bool { true }

    override func draw(_ dirtyRect: NSRect) {
        if backgroundOpacity > 0 {
            NSColor.labelColor.withAlphaComponent(backgroundOpacity).setFill()
            NSBezierPath(roundedRect: bounds.insetBy(dx: 2, dy: 2), xRadius: 5, yRadius: 5).fill()
        }
        super.draw(dirtyRect)
    }

    func update(expanded: Bool, header: AttachedLyricsHeader, cover: NSColor?) {
        let label = expanded ? "收起 Lumi 歌词" : "展开 Lumi 歌词"
        if self.expanded != expanded {
            self.expanded = expanded
            image = NSImage(systemSymbolName: expanded ? "chevron.up" : "chevron.down", accessibilityDescription: label)?
                .withSymbolConfiguration(.init(pointSize: 9, weight: .semibold))
            setAccessibilityLabel(label)
        }
        let hovered = header.hovered == .trigger
        if headerHighlighted != hovered || coverTint != cover {
            headerHighlighted = hovered
            coverTint = cover
            contentTintColor = header.tint(for: .trigger, cover: cover)
        }
        let opacity = header.backgroundOpacity(for: .trigger)
        if backgroundOpacity != opacity {
            backgroundOpacity = opacity
            needsDisplay = true
        }
    }
}

private final class NotchLyricsHintView: NSVisualEffectView {
    private let label = NSTextField(wrappingLabelWithString: "")
    private var hintSize = CGSize.zero

    override init(frame: NSRect) {
        super.init(frame: frame)
        material = .popover
        blendingMode = .behindWindow
        state = .active
        wantsLayer = true
        layer?.cornerRadius = 6
        layer?.masksToBounds = true
        label.font = .systemFont(ofSize: 11, weight: .medium)
        label.lineBreakMode = .byClipping
        label.setAccessibilityElement(false)
        addSubview(label)
    }

    required init?(coder: NSCoder) { nil }

    func setLabel(_ text: String) -> CGSize {
        if label.stringValue != text {
            label.stringValue = text
            label.sizeToFit()
            label.setFrameOrigin(CGPoint(x: 7, y: 4))
            hintSize = CGSize(width: ceil(label.frame.width) + 14, height: ceil(label.frame.height) + 8)
        }
        return hintSize
    }
}

@MainActor
private final class NotchLyricsHostingView<Content: View>: NSHostingView<Content> {
    override func acceptsFirstMouse(for event: NSEvent?) -> Bool { true }
}

@MainActor @Observable
private final class AttachedNotchExpansion {
    var openness: CGFloat = 0
}

@MainActor @Observable
private final class NotchGlassGeometry {
    var compactOutline: CompactNotchOutline?
    var compactOpenness: CGFloat = 0
    var expandedOutline: AttachedNotchOutline?
    var insets = NotchGlassInsets(system: false)
}

private struct NotchSystemGlassShape: Shape {
    let compact: CompactNotchOutline?
    let expanded: AttachedNotchOutline?
    let insets: NotchGlassInsets
    var openness: CGFloat

    var animatableData: CGFloat {
        get { openness }
        set { openness = newValue }
    }

    func path(in bounds: CGRect) -> Path {
        if let expanded {
            return expanded.atOpenness(openness).path(in: bounds)
        }
        if let compact {
            return CompactNotchGlassShape(outline: compact, openness: openness, glassInsets: insets).path(in: bounds)
        }
        return Path()
    }
}

private struct NotchSystemGlass: View {
    let geometry: NotchGlassGeometry
    let expansion: AttachedNotchExpansion
    @State private var appIsActive = NSApp.isActive
    @Environment(\.accessibilityReduceTransparency) private var reduceTransparency
    @Environment(\.colorScheme) private var scheme

    var body: some View {
        let outline = geometry.expandedOutline
        let shape = NotchSystemGlassShape(compact: geometry.compactOutline, expanded: outline, insets: geometry.insets,
                                          openness: outline == nil ? geometry.compactOpenness : expansion.openness)
        Color.clear
            .glassEffect(.regular, in: shape)
            .overlay {
                if !appIsActive && !reduceTransparency {
                    NotchInactiveHighlight(shape: shape, dark: scheme == .dark)
                }
            }
            .onReceive(NotificationCenter.default.publisher(for: NSApplication.didBecomeActiveNotification)) { _ in
                appIsActive = true
            }
            .onReceive(NotificationCenter.default.publisher(for: NSApplication.didResignActiveNotification)) { _ in
                appIsActive = false
            }
            .allowsHitTesting(false)
            .accessibilityHidden(true)
    }
}

private struct NotchInactiveHighlight: View, Animatable {
    nonisolated var shape: NotchSystemGlassShape
    let dark: Bool
    @Environment(\.displayScale) private var scale

    nonisolated var animatableData: CGFloat {
        get { shape.openness }
        set { shape.openness = newValue }
    }

    var body: some View {
        GeometryReader { proxy in
            let bounds = CGRect(origin: .zero, size: proxy.size)
            let path = shape.path(in: bounds)
            let contour = path.boundingRect
            if contour.width > 0, contour.height > 0, bounds.width > 0, bounds.height > 0 {
                // Native glass has a narrow bright rim that fades into the surface
                // and loses intensity around the sides and corners.
                let lightExpansion = dark || shape.expanded == nil ? 0 : min(1, max(0, shape.openness))
                let crest = dark ? 0.25 : 0.76 + 0.07 * lightExpansion
                let side = Color.white.opacity(dark ? 0.00 : 0.025)
                let reflection = LinearGradient(stops: [
                    .init(color: .white.opacity(crest), location: 0),
                    .init(color: side, location: 0.27),
                    .init(color: side, location: 0.73),
                    .init(color: .white.opacity(crest), location: 1)
                ], startPoint: UnitPoint(x: 0.5, y: contour.minY / bounds.height),
                   endPoint: UnitPoint(x: 0.5, y: contour.maxY / bounds.height))
                let feather = min(0.25, 22 / contour.width)
                let edge = Color.black.opacity(dark ? 0.10 : 0.70)
                let shoulder = Color.black.opacity(dark ? 0.42 : 0.86)
                let cornerFalloff = LinearGradient(stops: [
                    .init(color: edge, location: 0),
                    .init(color: shoulder, location: feather * 0.45),
                    .init(color: .black, location: feather),
                    .init(color: .black, location: 1 - feather),
                    .init(color: shoulder, location: 1 - feather * 0.45),
                    .init(color: edge, location: 1)
                ], startPoint: UnitPoint(x: contour.minX / bounds.width, y: 0.5),
                   endPoint: UnitPoint(x: contour.maxX / bounds.width, y: 0.5))
                ZStack {
                    if lightExpansion > 0 {
                        path.stroke(.white.opacity(0.32 * lightExpansion), lineWidth: 34)
                            .blur(radius: 5.5)
                    }
                    path.stroke(reflection, lineWidth: dark ? 5 : 9)
                        .blur(radius: dark ? 1.35 : 2.0)
                        .opacity(dark ? 0.38 : 0.42)
                    path.stroke(reflection, lineWidth: (dark ? 2.8 : 4.0) / max(1, scale))
                }
                .clipShape(path)
                .mask(cornerFalloff)
            }
        }
    }
}

private struct NotchLyricsBorder: Equatable {
    let color: Color
    let opacity: Double

    @MainActor init(music: MusicModel, dark: Bool) {
        let options = music.notchLyrics
        let cover = options.tintsBorderFromCover && music.artwork != nil ? music.colors.first : nil
        color = cover ?? (dark ? .white : .black)
        opacity = options.usesSystemGlassBorder ? 0 : options.borderIntensity
    }
}

private struct AttachedNotchShell<Content: View>: View {
    let expansion: AttachedNotchExpansion
    let music: MusicModel
    let outline: AttachedNotchOutline
    @ViewBuilder var content: Content
    @Environment(\.colorScheme) private var scheme

    var body: some View {
        let shape = outline.atOpenness(expansion.openness)
        let tint = content.frame(maxWidth: .infinity, maxHeight: .infinity)
            .padding(.horizontal, outline.glassInsets.outset)
            .padding(.bottom, outline.glassInsets.outset)
            .clipShape(shape)
        Group {
            if outline.glassInsets.outset > 0 {
                tint
            } else {
                let border = NotchLyricsBorder(music: music, dark: scheme == .dark)
                tint.glassEffect(.regular, in: shape).overlay {
                    if border.opacity > 0 {
                        shape.stroke(border.color.opacity(border.opacity), lineWidth: 2).clipShape(shape)
                    }
                }
            }
        }
        .accessibilityHidden(true)
    }
}

private struct NotchLyricsStyle: Equatable {
    let base: NSColor
    let halo: NSColor
    let companionHalo: NSColor
    let primary: NSColor
    let secondary: NSColor
    let current: NSColor
    let contextual: NSColor
    let unsung: NSColor
    let shadows: CGFloat
    let hdr: Bool

    @MainActor
    init(options: NotchLyricsOptions, dark: Bool, primary: Color, secondary: Color, density: Double) {
        let tint = Self.vividColor(primary) ?? .white
        let alternate = Self.vividColor(secondary) ?? tint
        let ink: NSColor = dark ? .white : NSColor(srgbRed: 0.07, green: 0.11, blue: 0.16, alpha: 1)
        let baseAlpha = options.usesSystemGlassBorder ? 0.26 + density * 0.18 : (dark ? 0.68 : 0.72) + density * 0.18
        base = dark ? NSColor(srgbRed: 0.07, green: 0.11, blue: 0.15, alpha: baseAlpha)
                    : NSColor(srgbRed: 0.93, green: 0.95, blue: 0.99, alpha: baseAlpha)
        halo = tint.withAlphaComponent(options.glow ? (dark ? 0.57 : 0.44) : 0)
        companionHalo = alternate.withAlphaComponent(options.glow ? (dark ? 0.46 : 0.34) : 0)
        let automatic = tint.blended(withFraction: dark ? 0.42 : 0.73, of: ink) ?? ink
        let companion = alternate.blended(withFraction: dark ? 0.52 : 0.68, of: ink) ?? ink
        self.primary = (options.useCoverColors ? nil : Self.color(options.lyricColor)) ?? automatic
        self.secondary = companion.withAlphaComponent(0.83)
        if options.useCoverColors {
            current = tint.blended(withFraction: dark ? 0.35 : 0.67, of: ink) ?? automatic
        } else {
            current = self.primary
        }
        contextual = current.withAlphaComponent(options.translationOpacity)
        unsung = current.withAlphaComponent(0.58)
        shadows = options.textShadow(dark: dark)
        hdr = dark && options.usesHDRLyricHighlights
    }

    private static func vividColor(_ color: Color) -> NSColor? {
        guard let rgb = NSColor(color).usingColorSpace(.sRGB) else { return nil }
        return NSColor(hue: rgb.hueComponent, saturation: min(1, rgb.saturationComponent * 1.33),
                       brightness: rgb.brightnessComponent, alpha: 1).usingColorSpace(.sRGB) ?? rgb
    }

    private static func color(_ hex: String) -> NSColor? {
        let digits = hex.hasPrefix("#") ? String(hex.dropFirst()) : hex
        guard digits.count == 6, let value = UInt32(digits, radix: 16) else { return nil }
        return NSColor(srgbRed: CGFloat((value >> 16) & 255) / 255, green: CGFloat((value >> 8) & 255) / 255,
                       blue: CGFloat(value & 255) / 255, alpha: 1)
    }
}

private struct NotchTextRow {
    enum Kind: Equatable { case primary, secondary, current, translation, following, countdown }

    var text: String
    var words: [Lyric.Word] = []
    var kind = Kind.primary
    var canSplit = true
    var start: Double = 0
    var end: Double = 0
}

struct ExpandedLyricSegments {
    private var layout = NotchTextLayout()

    // Stable ranges include zero-width slots for the words ahead of the playhead.
    mutating func fragment(text: String, words: [Lyric.Word] = [], start: Double, end: Double, position: Double,
                           font: NSFont, width: CGFloat, split: Bool, karaoke: Bool)
        -> (text: String, sungFraction: Double?, sungRanges: [ClosedRange<Double>]?,
            previousFragment: String?, nextFragment: String?) {
        let row = NotchTextRow(text: text, words: words, kind: .current, start: start, end: end)
        let visible = layout.shape(row, font: font, width: width, position: position, split: split, karaoke: karaoke,
                                   keepsWordSlots: true)
        let fragmentWidth = visible.shaping.width
        let sungRanges = visible.activeMarks.map { marks -> [ClosedRange<Double>] in
            guard fragmentWidth > 0 else { return [] }
            return marks.map { Double($0.lowerBound / fragmentWidth)...Double($0.upperBound / fragmentWidth) }
        }
        let sungFraction = sungRanges?.reduce(0) { max($0, $1.upperBound) }
        let source = visible.source as NSString
        let previous = visible.previousFragment.map { source.substring(with: $0.range) }
        let next = visible.nextFragment.map { source.substring(with: $0.range) }
        return (visible.text, sungFraction, sungRanges, previous, next)
    }
}

private struct NotchTextLayout {
    struct Shaped {
        let line: CTLine
        let width: CGFloat
        let height: CGFloat
        let descent: CGFloat
        let inkBounds: CGRect
        let range: NSRange
    }

    struct VisibleRow {
        let shaping: Shaped
        let source: String
        let kind: NotchTextRow.Kind
        var activeMarks: [ClosedRange<CGFloat>]? = nil
        var previousFragment: Shaped? = nil
        var nextFragment: Shaped? = nil

        var text: String { (source as NSString).substring(with: shaping.range) }

        func showsSameText(as other: Self) -> Bool {
            kind == other.kind && source == other.source && shaping.range == other.shaping.range
        }
    }

    private struct Key: Hashable {
        let text: String
        let font: NSFont
        let chineseFont: NSFont?
        let width: CGFloat
        let split: Bool
        let normalizesSpacing: Bool

        static func == (lhs: Key, rhs: Key) -> Bool {
            // Core Text uses the original UTF-16 indices even for canonically equivalent text.
            lhs.font == rhs.font && lhs.chineseFont == rhs.chineseFont && lhs.width == rhs.width &&
                lhs.split == rhs.split && lhs.normalizesSpacing == rhs.normalizesSpacing &&
                lhs.text.utf8.elementsEqual(rhs.text.utf8)
        }
    }

    private struct CachedLine {
        let display: LyricDisplayText
        let shapes: [Shaped]
    }

    private var cache: [Key: CachedLine] = [:]

    static func rowHeights(for font: NSFont, chineseFont: NSFont) -> (row: CGFloat, text: CGFloat) {
        func height(of text: String) -> CGFloat {
            let sample = LyricTypography.attributedText(text, westernFont: font, chineseFont: chineseFont)
            let line = CTLineCreateWithAttributedString(sample)
            var ascent: CGFloat = 0
            var descent: CGFloat = 0
            CTLineGetTypographicBounds(line, &ascent, &descent, nil)
            return max(10, ceil(max(font.ascender - font.descender, ascent + descent)))
        }
        // Keep the canvas sized for common fallback glyphs while using normal text to set the slider's range.
        return (height(of: "Ag 声音 かな 한글 🙂"), height(of: "Ag 声音 かな 한글"))
    }

    mutating func compactRows(_ items: [NotchTextRow], font: NSFont, chineseFont: NSFont, width: CGFloat,
                              firstLineScale: CGFloat, position: Double, split: Bool, karaoke: Bool) -> [VisibleRow] {
        let previewsNextLine = items.first?.kind == .countdown
        let rows = items.enumerated().map { index, item in
            // Keep the preview's first fragment when it moves into the larger singing row.
            let usesSingingWidth = index == 0 || (previewsNextLine && item.kind == .following)
            let lineWidth = usesSingingWidth ? width / firstLineScale : width
            return shape(item, font: font, chineseFont: chineseFont, width: lineWidth, position: position,
                         split: split && item.canSplit, karaoke: karaoke)
        }
        guard let current = rows.first, current.kind == .current, let following = current.nextFragment,
              !rows.contains(where: { $0.kind == .translation }) else { return rows }
        return [current, VisibleRow(shaping: following, source: current.source, kind: .following)]
    }

    mutating func shape(_ item: NotchTextRow, font: NSFont, chineseFont: NSFont? = nil, width: CGFloat, position: Double,
                        split: Bool, karaoke: Bool, keepsWordSlots: Bool = false) -> VisibleRow {
        let raw = item.words.isEmpty ? item.text.components(separatedBy: .newlines).joined(separator: " ") : item.words.map(\.text).joined()
        let normalizesSpacing = item.kind == .current || item.kind == .following || item.kind == .translation
        let key = Key(text: raw, font: font, chineseFont: chineseFont, width: width, split: split,
                      normalizesSpacing: normalizesSpacing)
        let line: CachedLine
        if let cached = cache[key] {
            line = cached
        } else {
            if cache.count >= 45 { cache.removeAll(keepingCapacity: true) }
            let display = LyricDisplayText(raw, normalizesSpacing: normalizesSpacing)
            let shapes = Self.lines(display.string, font: font, chineseFont: chineseFont, width: width, split: split)
            line = CachedLine(display: display, shapes: shapes)
            cache[key] = line
        }
        let source = line.display.string
        let shapes = line.shapes
        let words = line.display.timedWords(item.words)
        var index = 0
        if shapes.count > 1, item.kind != .following {
            if words.isEmpty {
                let duration = max(0.2, item.end - item.start)
                let progress = min(1, max(0, (position - item.start) / duration))
                let totalWidth = shapes.reduce(0) { $0 + $1.width }
                var distance = progress * totalWidth
                for (piece, shape) in shapes.enumerated() {
                    index = piece
                    distance -= shape.width
                    if distance < 0 { break }
                }
            } else {
                var wordOffset = 0
                var activeOffset = 0
                for word in words where !word.text.isEmpty {
                    let count = word.text.utf16.count
                    if word.time <= position {
                        let progress = word.duration > 0 ? min(1, max(0, (position - word.time) / word.duration)) : 0
                        activeOffset = wordOffset + min(count - 1, Int(progress * Double(count)))
                    }
                    wordOffset += count
                }
                // A timed token can include the spaces trimmed from the previous segment.
                index = shapes.lastIndex { $0.range.location <= activeOffset } ?? 0
            }
        }
        let shaped = shapes[index]
        var row = VisibleRow(shaping: shaped, source: source, kind: item.kind,
                             previousFragment: index > 0 ? shapes[index - 1] : nil,
                             nextFragment: shapes.indices.contains(index + 1) ? shapes[index + 1] : nil)
        guard !words.isEmpty, karaoke, item.kind != .following else { return row }
        var marks: [ClosedRange<CGFloat>] = []
        var offset = 0
        for word in words {
            let size = word.text.utf16.count
            let wordRange = NSRange(location: offset, length: size)
            offset += size
            let overlap = NSIntersectionRange(shaped.range, wordRange)
            guard overlap.length > 0 else { continue }
            let began = Double(overlap.location - wordRange.location) / Double(size)
            let portion = Double(overlap.length) / Double(size)
            let spoken = position < word.time ? 0
                : (word.duration > 0 ? (position - word.time) / word.duration : 1)
            let amount = min(1, max(0, (spoken - began) / portion))
            guard keepsWordSlots || amount > 0 else { continue }
            let a = min(shaped.width, max(0, CTLineGetOffsetForStringIndex(shaped.line, overlap.location, nil)))
            let b = min(shaped.width, max(0, CTLineGetOffsetForStringIndex(shaped.line, NSMaxRange(overlap), nil)))
            let sung = a + (b - a) * amount
            marks.append(min(a, sung)...max(a, sung))
        }
        row.activeMarks = marks
        return row
    }

    private static func lines(_ text: String, font: NSFont, chineseFont: NSFont?, width: CGFloat, split: Bool) -> [Shaped] {
        let marked = LyricTypography.attributedText(text, westernFont: font, chineseFont: chineseFont)
        let whole = CTLineCreateWithAttributedString(marked)
        func shaped(_ line: CTLine, range: NSRange) -> Shaped {
            var ascent: CGFloat = 0
            var descent: CGFloat = 0
            let width = CTLineGetTypographicBounds(line, &ascent, &descent, nil)
            let inkBounds = CTLineGetImageBounds(line, nil)
            return .init(line: line, width: width, height: ascent + descent, descent: descent,
                         inkBounds: inkBounds, range: range)
        }
        let fullWidth = CTLineGetTypographicBounds(whole, nil, nil, nil)
        if fullWidth <= width {
            return [shaped(whole, range: NSRange(location: 0, length: marked.length))]
        }
        if !split {
            let token = CTLineCreateWithAttributedString(LyricTypography.attributedText("…", westernFont: font, chineseFont: nil))
            let line = CTLineCreateTruncatedLine(whole, width, .end, token) ?? token
            return [shaped(line, range: NSRange(location: 0, length: marked.length))]
        }
        let typesetter = CTTypesetterCreateWithAttributedString(marked)
        if let ranges = NotchLyricBreaks.ranges(for: text, using: typesetter, width: width, fullWidth: fullWidth) {
            return ranges.map { range in
                shaped(CTTypesetterCreateLine(typesetter, CFRange(location: range.location, length: range.length)), range: range)
            }
        }
        var result: [Shaped] = []
        var start = 0
        while start < marked.length {
            let count = min(marked.length - start, max(1, CTTypesetterSuggestLineBreak(typesetter, start, width)))
            let line = CTTypesetterCreateLine(typesetter, CFRange(location: start, length: count))
            result.append(shaped(line, range: NSRange(location: start, length: count)))
            start += count
        }
        return result
    }
}

private final class NotchLyricsView: NSView {
    private struct Fade {
        let lines: [NotchTextLayout.VisibleRow]
        let countdownProgress: Double?
        let startedAt: TimeInterval
    }

    private static let fadeDuration: TimeInterval = 0.13
    private var style: NotchLyricsStyle?
    private var lines: [NotchTextLayout.VisibleRow] = []
    private var rowHeight: CGFloat = 0
    private var padding: CGFloat = 0
    private var textCeiling: CGFloat?
    private var spacing: CGFloat = 0
    private var firstLineScale: CGFloat = 1
    private var countdown: LyricCountdown?
    private var phase: TimeInterval?
    private var caption = ""
    private var fade: Fade?
    private var fadeDisplayLink: CADisplayLink?
    private var hdrInk: HDRLyricView?
    private var borderPath: CGPath?
    private var borderColor: CGColor?
    private var lyricsOpacity: CGFloat = 1

    func update(style: NotchLyricsStyle, lines: [NotchTextLayout.VisibleRow], rowHeight: CGFloat,
                padding: CGFloat, textCeiling: CGFloat?, spacing: CGFloat, firstLineScale: CGFloat, countdown: LyricCountdown?,
                phase: TimeInterval?, animatesChanges: Bool, label: String) {
        if label != caption {
            caption = label
            setAccessibilityElement(true)
            setAccessibilityLabel(label)
        }
        let sameLines = self.lines.count == lines.count && zip(self.lines, lines).allSatisfy { old, new in
            old.shaping.line === new.shaping.line && old.kind == new.kind && old.activeMarks == new.activeMarks
        }
        let sameText = self.lines.count == lines.count && zip(self.lines, lines).allSatisfy { old, new in
            old.showsSameText(as: new)
        }
        let changedTypeLayout = self.firstLineScale != firstLineScale || self.spacing != spacing || self.padding != padding
            || self.textCeiling != textCeiling
        if !animatesChanges || changedTypeLayout || self.lines.isEmpty || lines.isEmpty {
            cancelFade()
        } else if !sameText {
            if fade != nil {
                cancelFade()
            } else {
                fade = Fade(lines: self.lines, countdownProgress: self.countdown?.progress, startedAt: CACurrentMediaTime())
                let link = displayLink(target: self, selector: #selector(redrawFade(_:)))
                link.add(to: .main, forMode: .common)
                fadeDisplayLink = link
            }
        }
        guard self.style != style || !sameLines || self.rowHeight != rowHeight || self.padding != padding ||
                self.textCeiling != textCeiling || self.spacing != spacing || self.firstLineScale != firstLineScale || self.countdown != countdown ||
                self.phase != phase else { return }
        self.style = style
        self.lines = lines
        self.rowHeight = rowHeight
        self.padding = padding
        self.textCeiling = textCeiling
        self.spacing = spacing
        self.firstLineScale = firstLineScale
        self.countdown = countdown
        self.phase = phase
        if style.hdr, hdrInk == nil {
            let ink = HDRLyricView(frame: bounds)
            ink.autoresizingMask = [.width, .height]
            ink.alphaValue = lyricsOpacity
            addSubview(ink)
            hdrInk = ink
        } else if !style.hdr {
            hdrInk?.removeFromSuperview()
            hdrInk = nil
        }
        needsDisplay = true
    }

    func cancelFade() {
        guard fade != nil else { return }
        fade = nil
        fadeDisplayLink?.invalidate()
        fadeDisplayLink = nil
        needsDisplay = true
    }

    func setLyricsOpacity(_ opacity: CGFloat) {
        guard lyricsOpacity != opacity else { return }
        lyricsOpacity = opacity
        hdrInk?.alphaValue = opacity
        needsDisplay = true
    }

    func updateBorder(_ border: NotchLyricsBorder, path: CGPath?) {
        borderPath = path
        borderColor = border.opacity > 0 ? NSColor(border.color).withAlphaComponent(border.opacity).cgColor : nil
        needsDisplay = true
    }

    func moveBorder(to path: CGPath) {
        guard borderColor != nil else { return }
        borderPath = path
        needsDisplay = true
    }

    @objc private func redrawFade(_ link: CADisplayLink) {
        if let fade, link.timestamp - fade.startedAt >= Self.fadeDuration { cancelFade() }
        needsDisplay = true
    }

    override func draw(_ dirtyRect: NSRect) {
        guard let context = NSGraphicsContext.current?.cgContext, let style else { return }
        context.setFillColor(style.base.cgColor)
        context.fill(bounds)
        if style.halo.alphaComponent > 0 {
            let sway = phase.map { sin($0 * 0.94) } ?? 0
            let crossing = phase.map { cos($0 * 1.19) } ?? 0
            drawHalo(style.halo, center: .init(x: bounds.width * (0.23 + 0.27 * sway), y: 5 + 9 * crossing), in: context)
            drawHalo(style.companionHalo, center: .init(x: bounds.width * (0.79 - 0.21 * crossing), y: 12 - 9 * sway), in: context)
        }
        if let borderPath, let borderColor {
            context.setStrokeColor(borderColor)
            context.setLineWidth(2)
            context.addPath(borderPath)
            context.strokePath()
        }
        guard lyricsOpacity > 0 else { return }
        let blend: CGFloat
        if let fade {
            let progress = min(1, max(0, (CACurrentMediaTime() - fade.startedAt) / Self.fadeDuration))
            blend = CGFloat(progress * progress * (3 - 2 * progress))
            drawLyrics(fade.lines, style: style, in: context, opacity: (1 - blend) * lyricsOpacity,
                       countdownProgress: fade.countdownProgress)
        } else { blend = 1 }
        drawLyrics(lines, style: style, in: context, opacity: blend * lyricsOpacity,
                   countdownProgress: countdown?.progress)
        if let hdrInk {
            if lines.contains(where: { $0.activeMarks?.isEmpty == false })
                || fade?.lines.contains(where: { $0.activeMarks?.isEmpty == false }) == true {
                hdrInk.render(size: bounds.size, color: style.current) { cg, ink in
                    if let fade { drawLyrics(fade.lines, style: style, in: cg, opacity: 1 - blend, hdrInk: ink) }
                    drawLyrics(lines, style: style, in: cg, opacity: blend, hdrInk: ink)
                }
            } else { hdrInk.clear() }
        }
    }

    private func drawLyrics(_ lines: [NotchTextLayout.VisibleRow], style: NotchLyricsStyle,
                            in context: CGContext, opacity: CGFloat, hdrInk: CGColor? = nil, countdownProgress: Double? = nil) {
        context.saveGState()
        context.setAlpha(opacity)
        // Keep the fixed canvas centered while packing two short lines closer together.
        let stackHeight = 2 * rowHeight + spacing
        let rowStep = max(rowHeight - 5, lines.last?.shaping.height ?? rowHeight) + spacing
        let upperHeight = (lines.first?.shaping.height ?? rowHeight) * firstLineScale
        let rowInset = (stackHeight - rowStep - upperHeight) / 2
        func baseline(for row: NotchTextLayout.VisibleRow, index: Int, scale: CGFloat) -> CGFloat {
            let verticalOffset = lines.count == 1 ? (stackHeight - upperHeight) / 2
                                                  : rowInset + CGFloat(index) * rowStep
            return padding + verticalOffset + row.shaping.descent * scale
        }
        func dotsCenter(for row: NotchTextLayout.VisibleRow, index: Int, scale: CGFloat) -> CGFloat {
            baseline(for: row, index: index, scale: scale) + (row.shaping.height / 2 - row.shaping.descent) * scale
        }
        let rows = lines.reversed().enumerated()
        if let textCeiling, !lines.isEmpty {
            // Fit unusually tall fallback glyphs inside the short panel without resizing on lyric changes.
            var inkBottom = CGFloat.infinity
            var inkTop = -CGFloat.infinity
            for (index, row) in rows {
                let scale = index == lines.count - 1 ? firstLineScale : 1
                if row.kind == .countdown {
                    let center = dotsCenter(for: row, index: index, scale: scale)
                    inkBottom = min(inkBottom, center - 2)
                    inkTop = max(inkTop, center + 2)
                } else {
                    let y = baseline(for: row, index: index, scale: scale)
                    let ink = row.shaping.inkBounds
                    if !ink.isNull, !ink.isEmpty {
                        inkBottom = min(inkBottom, y + ink.minY * scale)
                        inkTop = max(inkTop, y + ink.maxY * scale)
                    }
                }
            }
            if inkBottom.isFinite, inkTop.isFinite {
                let inkHeight = inkTop - inkBottom
                let scale = min(1, max(1, textCeiling - 1) / max(1, inkHeight))
                let fittedBottom = max(1, min(inkBottom, textCeiling - inkHeight * scale))
                context.translateBy(x: bounds.midX * (1 - scale), y: fittedBottom - inkBottom * scale)
                context.scaleBy(x: scale, y: scale)
            }
        }
        for (index, row) in rows {
            let line = row.shaping.line
            let scale = index == lines.count - 1 ? firstLineScale : 1
            if row.kind == .countdown {
                if let countdownProgress, hdrInk == nil {
                    let centerY = dotsCenter(for: row, index: index, scale: scale)
                    for dot in 0..<3 {
                        let alpha = LyricCountdown.opacity(of: dot, progress: countdownProgress)
                        context.setFillColor(style.primary.withAlphaComponent(alpha).cgColor)
                        context.fillEllipse(in: CGRect(x: bounds.midX + CGFloat(dot - 1) * 8 - 2,
                                                       y: centerY - 2, width: 4, height: 4))
                    }
                }
                continue
            }
            let origin = CGPoint(x: bounds.midX - row.shaping.width * scale / 2,
                                 y: baseline(for: row, index: index, scale: scale))
            let ink = switch row.kind {
            case .primary, .countdown: style.primary
            case .secondary: style.secondary
            case .current: style.current
            case .translation: style.contextual
            case .following: style.unsung
            }
            func drawLine(in color: CGColor) {
                context.saveGState()
                context.setFillColor(color)
                context.translateBy(x: origin.x, y: origin.y)
                context.scaleBy(x: scale, y: scale)
                context.textMatrix = .identity
                context.textPosition = .zero
                CTLineDraw(line, context)
                context.restoreGState()
            }

            if hdrInk == nil {
                context.saveGState()
                if style.shadows > 0 {
                    context.setShadow(offset: CGSize(width: 0, height: -1), blur: 4 * style.shadows,
                                      color: NSColor.black.withAlphaComponent(0.48 * style.shadows).cgColor)
                }
                drawLine(in: (row.activeMarks == nil ? ink : style.unsung).cgColor)
                context.restoreGState()
            }
            if let marks = row.activeMarks, !marks.isEmpty {
                context.saveGState()
                let clips = marks.map { range in
                    CGRect(x: origin.x + range.lowerBound * scale, y: 0,
                           width: (range.upperBound - range.lowerBound) * scale, height: bounds.height)
                }
                context.clip(to: clips)
                drawLine(in: hdrInk ?? ink.cgColor)
                context.restoreGState()
            }
        }
        context.restoreGState()
    }

    private func drawHalo(_ color: NSColor, center: CGPoint, in context: CGContext) {
        let colors = [color.cgColor, color.withAlphaComponent(color.alphaComponent * 0.34).cgColor,
                      color.withAlphaComponent(0).cgColor] as CFArray
        guard let gradient = CGGradient(colorsSpace: nil, colors: colors, locations: [0, 0.36, 1]) else { return }
        context.drawRadialGradient(gradient, startCenter: center, startRadius: 0,
                                   endCenter: center, endRadius: bounds.width * 0.62, options: [])
    }
}
