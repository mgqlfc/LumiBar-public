import ApplicationServices
import Foundation

enum AppleMusicWebPlayback {
    static let firefoxID = "org.mozilla.firefox"

    static func reads(_ track: Track) -> Bool { track.systemSession && track.player.rawValue == firefoxID && track.pid > 0 }

    static func sameTrack(_ a: Track, _ b: Track) -> Bool {
        a.player == b.player && a.pid == b.pid && a.title == b.title && a.artist == b.artist && a.album == b.album
    }

    static func duration(for track: Track) -> Double? {
        guard reads(track), AXIsProcessTrusted() else { return nil }
        let app = AXUIElementCreateApplication(track.pid)
        AXUIElementSetMessagingTimeout(app, 0.20)
        guard let window = element(app, kAXFocusedWindowAttribute) ?? element(app, kAXMainWindowAttribute),
              let bounds = frame(window) else { return nil }
        let deadline = ContinuousClock.now + .milliseconds(850)
        let pages = walk(window, limit: 450, before: deadline, stopsAtDocument: true).filter { node in
            guard isDocument(node) else { return false }
            return [kAXURLAttribute, kAXDocumentAttribute].contains { name in
                guard let url = URL(string: string(node, name)) else { return false }
                return url.scheme == "https" && url.host?.lowercased() == "music.apple.com" &&
                    url.user == nil && url.password == nil && (url.port == nil || url.port == 443)
            }
        }
        guard pages.count == 1, let page = pages.first else { return nil }
        let content = walk(page, limit: 1_200, before: deadline)
        let progressLabels: Set<String> = ["播放进度", "播放進度", "播放進展", "playback position", "playback progress"]
        let sliders = content.filter { node in
            role(node) == kAXSliderRole && labels(node).contains { progressLabels.contains($0.lowercased()) }
        }
        guard sliders.count == 1, let slider = sliders.first, let sliderFrame = frame(slider),
              sliderFrame.maxY >= bounds.maxY - 145, bounds.intersects(sliderFrame),
              let maximum = (attribute(slider, kAXMaxValueAttribute) as? NSNumber)?.doubleValue,
              let progress = (attribute(slider, kAXValueAttribute) as? NSNumber)?.doubleValue,
              (6...86_400).contains(maximum), (0...maximum).contains(progress),
              abs(progress - track.position(at: .now)) <= 2.5 else { return nil }

        let title = LyricRecording.identity(track.title)
        let artist = LyricRecording.identity(track.artist)
        guard !title.isEmpty, !artist.isEmpty else { return nil }
        let footer = CGRect(x: bounds.minX, y: sliderFrame.minY - 76, width: bounds.width,
                            height: sliderFrame.height + 96).intersection(bounds)
        let metadataRoles: Set<String> = [kAXStaticTextRole, "AXLink", kAXGroupRole, kAXButtonRole, kAXImageRole]
        var titles: [CGRect] = []
        var artists: [CGRect] = []
        for node in content {
            guard ContinuousClock.now < deadline else { return nil }
            let type = role(node)
            guard metadataRoles.contains(type), let box = frame(node), box.width > 0, box.height > 0,
                  footer.contains(CGPoint(x: box.midX, y: box.midY)) else { continue }
            let values = labels(node) + (type == kAXStaticTextRole ? [string(node, kAXValueAttribute)] : [])
            for value in values {
                if LyricRecording.identity(value) == title { titles.append(box) }
                let pieces = value.split(separator: "·", maxSplits: 1)
                if LyricRecording.identity(pieces.first.map(String.init) ?? value) == artist { artists.append(box) }
            }
        }
        let metadataAgrees = titles.contains { title in
            artists.contains { artist in abs(title.minX - artist.minX) <= 24 && (0...37).contains(artist.minY - title.minY) }
        }
        return metadataAgrees ? maximum : nil
    }

    private static func walk(_ root: AXUIElement, limit: Int, before deadline: ContinuousClock.Instant,
                             stopsAtDocument: Bool = false) -> [AXUIElement] {
        var queue = [root]
        var cursor = 0
        var discovered = Set<AXUIElement>()
        var nodes: [AXUIElement] = []
        while cursor < queue.count, nodes.count < limit, ContinuousClock.now < deadline {
            let node = queue[cursor]
            cursor += 1
            guard discovered.insert(node).inserted else { continue }
            nodes.append(node)
            if !stopsAtDocument || !isDocument(node) {
                queue.append(contentsOf: attribute(node, kAXChildrenAttribute) as? [AXUIElement] ?? [])
            }
        }
        return nodes
    }

    private static func isDocument(_ node: AXUIElement) -> Bool { ["AXWebArea", "AXDocument"].contains(role(node)) }
    private static func role(_ node: AXUIElement) -> String { string(node, kAXRoleAttribute) }
    private static func labels(_ node: AXUIElement) -> [String] {
        [kAXTitleAttribute, kAXDescriptionAttribute, "AXLabel"].map { string(node, $0).trimmingCharacters(in: .whitespacesAndNewlines) }
    }

    private static func attribute(_ node: AXUIElement, _ name: String) -> CFTypeRef? {
        var value: CFTypeRef?
        return AXUIElementCopyAttributeValue(node, name as CFString, &value) == .success ? value : nil
    }

    private static func string(_ node: AXUIElement, _ key: String) -> String {
        let value = attribute(node, key)
        return (value as? String) ?? (value as? URL)?.absoluteString ?? ""
    }

    private static func element(_ node: AXUIElement, _ key: String) -> AXUIElement? {
        guard let value = attribute(node, key), CFGetTypeID(value) == AXUIElementGetTypeID() else { return nil }
        return (value as! AXUIElement)
    }

    private static func frame(_ node: AXUIElement) -> CGRect? {
        guard let position = attribute(node, kAXPositionAttribute), CFGetTypeID(position) == AXValueGetTypeID(),
              let size = attribute(node, kAXSizeAttribute), CFGetTypeID(size) == AXValueGetTypeID() else { return nil }
        var point = CGPoint.zero
        var dimensions = CGSize.zero
        guard AXValueGetValue(position as! AXValue, .cgPoint, &point), AXValueGetValue(size as! AXValue, .cgSize, &dimensions) else { return nil }
        return CGRect(origin: point, size: dimensions)
    }
}
