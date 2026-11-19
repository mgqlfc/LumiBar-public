import AppKit
import CryptoKit
import Foundation
import SwiftUI

enum Player: String, CaseIterable, Sendable {
    case netease = "com.netease.163music"
    case music = "com.apple.Music"
    case spotify = "com.spotify.client"

    var name: String {
        switch self {
        case .netease: "网易云音乐"
        case .music: "Apple Music"
        case .spotify: "Spotify"
        }
    }
}

struct Track: Equatable, Sendable {
    let player: Player
    let title: String
    let artist: String
    let album: String
    let duration: Double
    var position: Double
    let playing: Bool
    let artworkURL: URL?
    let pid: pid_t
    let observedAt: Date
    var catalogID: String? = nil
    var canSeek = true
    var canControl = true
    var estimatedPosition = false
    var key: String {
        if player == .netease, let catalogID { return player.rawValue + "|" + catalogID }
        return [player.rawValue, title, artist, album, String(Int(duration))].joined(separator: "|")
    }

    func position(at now: Date) -> Double {
        min(duration, max(0, position + (playing ? now.timeIntervalSince(observedAt) : 0)))
    }
}

enum PlaybackCommand: Sendable {
    case toggle, previous, next, seek(Double)

    var script: String {
        switch self {
        case .toggle: "playpause"
        case .previous: "previous track"
        case .next: "next track"
        case .seek(let seconds): "set player position to \(max(0, seconds))"
        }
    }
}

enum PlayerBridge {
    static func read(_ player: Player, pid: pid_t) async -> Result<Track?, ScriptError> {
        let artwork = player == .spotify ? "artwork url" : "\"\""
        let duration = player == .spotify ? "(duration / 1000)" : "duration"
        let script = """
        with timeout of 4 seconds
            tell application id "\(player.rawValue)"
                if not running then return ""
                set stateText to player state as text
                if stateText is "stopped" then return ""
                set positionText to player position as text
                tell current track
                    set coverURL to ""
                    try
                        set coverURL to \(artwork)
                    end try
                    set fields to {stateText, name as text, artist as text, album as text, \(duration) as text, positionText, coverURL}
                end tell
                set AppleScript's text item delimiters to ASCII character 31
                return fields as text
            end tell
        end timeout
        """
        switch await run(script) {
        case .failure(let error): return .failure(error)
        case .success(let text):
            let parts = text.split(separator: "\u{001F}", omittingEmptySubsequences: false).map(String.init)
            guard parts.count == 7, !parts[1].isEmpty else { return .success(nil) }
            let duration = min(86400, max(0, number(parts[4])))
            let artworkURL = URL(string: parts[6]).flatMap { $0.scheme == "https" ? $0 : nil }
            return .success(Track(player: player, title: parts[1], artist: parts[2], album: parts[3],
                                  duration: duration, position: number(parts[5]), playing: parts[0] == "playing",
                                  artworkURL: artworkURL, pid: pid, observedAt: .now))
        }
    }

    static func control(_ command: PlaybackCommand, player: Player) async -> Result<String, ScriptError> {
        await run("""
        with timeout of 4 seconds
            tell application id "\(player.rawValue)"
                if running then \(command.script)
            end tell
        end timeout
        """)
    }

    static func artwork(for track: Track) async -> Data? {
        if let url = track.artworkURL {
            guard let (data, response) = try? await URLSession.shared.data(for: URLRequest(url: url, timeoutInterval: 8)),
                  let http = response as? HTTPURLResponse, (200..<300).contains(http.statusCode),
                  data.count <= 8_000_000 else { return nil }
            return data
        }
        guard track.player == .music,
              let folder = try? FileManager.default.url(for: .itemReplacementDirectory, in: .userDomainMask,
                    appropriateFor: FileManager.default.temporaryDirectory, create: true) else { return nil }
        defer { try? FileManager.default.removeItem(at: folder) }
        let file = folder.appending(path: "artwork")
        let path = file.path.replacingOccurrences(of: "\\", with: "\\\\").replacingOccurrences(of: "\"", with: "\\\"")
        let result = await run("""
        with timeout of 4 seconds
            tell application id "\(track.player.rawValue)"
                if not running then return ""
                set imageData to raw data of artwork 1 of current track
            end tell
            set outputFile to open for access POSIX file "\(path)" with write permission
            try
                write imageData to outputFile
            on error messageText number errorNumber
                close access outputFile
                error messageText number errorNumber
            end try
            close access outputFile
        end timeout
        """)
        guard case .success = result else { return nil }
        return try? Data(contentsOf: file)
    }

    struct ScriptError: Error {
        let output: String
        var denied: Bool { output.contains("-1743") }
    }

    private static func number(_ text: String) -> Double {
        let value = Double(text.replacingOccurrences(of: ",", with: ".")) ?? 0
        return value.isFinite ? value : 0
    }

    private static func run(_ script: String) async -> Result<String, ScriptError> {
        await withCheckedContinuation { continuation in
            let process = Process()
            let output = Pipe()
            process.executableURL = URL(fileURLWithPath: "/usr/bin/osascript")
            process.arguments = ["-e", script]
            process.standardOutput = output
            process.standardError = output
            process.terminationHandler = { finished in
                let data = output.fileHandleForReading.readDataToEndOfFile()
                var text = String(decoding: data, as: UTF8.self)
                if text.last?.isNewline == true { text.removeLast() }
                let result: Result<String, ScriptError> = finished.terminationStatus == 0
                    ? .success(text) : .failure(.init(output: text))
                continuation.resume(returning: result)
            }
            do {
                try process.run()
                DispatchQueue.global().asyncAfter(deadline: .now() + 8) {
                    if process.isRunning { process.terminate() }
                }
            } catch {
                continuation.resume(returning: .failure(.init(output: error.localizedDescription)))
            }
        }
    }
}

struct Lyric: Identifiable, Sendable {
    let time: Double
    let text: String
    var id: Double { time }

    static func parse(_ input: String) -> [Lyric] {
        guard input.utf8.count <= 1_048_576 else { return [] }
        let offsetMatch = input.firstMatch(of: /\[offset:([+-]?\d+)\]/.ignoresCase())
        let offset = max(-3600, min(3600, (offsetMatch.flatMap { Double($0.1) } ?? 0) / 1000))
        let tag = /\[(\d{1,3}):(\d{2})(?:[.:](\d{1,3}))?\]/
        var lines: [Double: [String]] = [:]
        for line in input.split(whereSeparator: \.isNewline) {
            let text = line.replacing(tag, with: "").trimmingCharacters(in: .whitespaces)
            guard !text.isEmpty else { continue }
            for match in line.matches(of: tag) {
                guard let minutes = Double(match.1), let seconds = Double(match.2), seconds < 60 else { continue }
                let fraction = match.3.flatMap { Double("0." + $0) } ?? 0
                let at = (max(0, minutes * 60 + seconds + fraction - offset) * 1000).rounded() / 1000
                if !(lines[at]?.contains(text) ?? false) { lines[at, default: []].append(text) }
            }
        }
        return lines.sorted(by: { $0.key < $1.key }).map { .init(time: $0.key, text: $0.value.joined(separator: "\n")) }
    }

    struct Lookup: Sendable { let lines: [Lyric]; let provider: String }

    static func fetch(_ track: Track) async -> Lookup? {
        if track.player == .netease, let id = track.catalogID {
            let lyrics = await NeteaseBridge.syncLyrics(trackID: id)
            if !lyrics.isEmpty { return .init(lines: lyrics, provider: "网易云 ") }
            if Task.isCancelled { return nil }
        }
        var parts = URLComponents(string: "https://lrclib.net/api/get")!
        parts.queryItems = [.init(name: "track_name", value: track.title), .init(name: "artist_name", value: track.artist),
                            .init(name: "album_name", value: track.album),
                            .init(name: "duration", value: String(Int(track.duration.rounded())))]
        struct Response: Decodable { let syncedLyrics: String? }
        guard let (data, response) = try? await URLSession.shared.data(for: URLRequest(url: parts.url!, timeoutInterval: 8)),
              (response as? HTTPURLResponse)?.statusCode == 200,
              let text = (try? JSONDecoder().decode(Response.self, from: data))?.syncedLyrics else { return nil }
        return .init(lines: parse(text), provider: "LRCLIB ")
    }
}

let neutralCoverColors = [Color(red: 0.54, green: 0.60, blue: 0.85),
                          Color(red: 0.65, green: 0.58, blue: 0.87)]

private struct CoverRGB {
    let r: Double, g: Double, b: Double

    var value: Double { max(r, g, b) }
    var chroma: Double { value - min(r, g, b) }
    var hue: Double {
        guard chroma > 0 else { return 0 }
        let segment: Double
        if value == r { segment = ((g - b) / chroma + 6).truncatingRemainder(dividingBy: 6) }
        else if value == g { segment = (b - r) / chroma + 2 }
        else { segment = (r - g) / chroma + 4 }
        return segment * 60
    }
    var color: Color { Color(red: r / 255, green: g / 255, blue: b / 255) }
    var lifted: CoverRGB {
        guard value > 0, value < 176 else { return self }
        let gain = 176 / value
        return .init(r: r * gain, g: g * gain, b: b * gain)
    }

    func distance(from other: CoverRGB) -> Double {
        let red = r - other.r, green = g - other.g, blue = b - other.b
        return sqrt(red * red + green * green + blue * blue)
    }

    func hueDistance(from other: CoverRGB) -> Double {
        let distance = abs(hue - other.hue)
        return min(distance, 360 - distance)
    }
}

@MainActor
func coverColors(_ image: NSImage) -> [Color] {
    var rect = CGRect(origin: .zero, size: image.size)
    let side = 32
    guard let source = image.cgImage(forProposedRect: &rect, context: nil, hints: nil),
          let space = CGColorSpace(name: CGColorSpace.sRGB),
          let context = CGContext(data: nil, width: side, height: side, bitsPerComponent: 8, bytesPerRow: side * 4,
                                  space: space, bitmapInfo: CGImageAlphaInfo.premultipliedLast.rawValue)
    else { return neutralCoverColors }
    context.draw(source, in: CGRect(x: 0, y: 0, width: side, height: side))
    guard let pixels = context.data?.assumingMemoryBound(to: UInt8.self) else { return neutralCoverColors }

    var bins: [Int: (r: Int, g: Int, b: Int, count: Int)] = [:]
    for index in stride(from: 0, to: side * side * 4, by: 4) {
        let alpha = Double(pixels[index + 3]) / 255
        guard alpha >= 180.0 / 255 else { continue }
        let r = min(255, Int((Double(pixels[index]) / alpha).rounded()))
        let g = min(255, Int((Double(pixels[index + 1]) / alpha).rounded()))
        let b = min(255, Int((Double(pixels[index + 2]) / alpha).rounded()))
        let high = max(r, g, b), low = min(r, g, b)
        guard high >= 40, high - low >= 24, Double(high - low) / Double(high) >= 0.18 else { continue }
        let key = ((r >> 5) << 6) | ((g >> 5) << 3) | (b >> 5)
        var sample = bins[key, default: (0, 0, 0, 0)]
        sample.r += r
        sample.g += g
        sample.b += b
        sample.count += 1
        bins[key] = sample
    }

    let samples = bins.keys.sorted().compactMap { key -> (color: CoverRGB, count: Double)? in
        guard let bin = bins[key] else { return nil }
        let count = Double(bin.count)
        return (CoverRGB(r: Double(bin.r) / count, g: Double(bin.g) / count, b: Double(bin.b) / count), count)
    }
    let bright = samples.filter { $0.color.value >= 160 }
    let candidates = bright.isEmpty ? samples : bright
    func prominence(_ color: CoverRGB, count: Double) -> Double {
        sqrt(count) * (0.25 + color.chroma / color.value) * (0.35 + 0.65 * color.value / 255)
    }
    guard let best = candidates.max(by: { prominence($0.color, count: $0.count) < prominence($1.color, count: $1.count) })
    else { return neutralCoverColors }
    let primary = best.color.lifted
    let matches = samples.compactMap { choice -> (color: CoverRGB, score: Double)? in
        let color = choice.color.lifted
        let distance = color.distance(from: primary), hueDistance = color.hueDistance(from: primary)
        guard distance >= 18, hueDistance <= 60 else { return nil }
        return (color, sqrt(choice.count) * min(90, distance) * (1 - hueDistance / 120))
    }
    if let next = matches.max(by: { $0.score < $1.score }) { return [primary.color, next.color.color] }
    let lift = (primary.r + primary.g + primary.b) / 3 < 155 ? 255.0 * 0.24 : 0
    let secondary = CoverRGB(r: primary.r * 0.76 + lift, g: primary.g * 0.76 + lift, b: primary.b * 0.76 + lift)
    return [primary.color, secondary.color]
}

@MainActor
func softenCoverLight(_ color: Color, saturation: Double, light: Bool) -> Color {
    guard let rgb = NSColor(color).usingColorSpace(.sRGB) else { return color }
    let r = rgb.redComponent, g = rgb.greenComponent, b = rgb.blueComponent
    let high = max(r, g, b), low = min(r, g, b)
    let delta = high - low
    guard delta > 0.00001 else { return color }
    let originalLightness = (high + low) / 2
    let chroma = delta / (1 - abs(2 * originalLightness - 1))
    let luminosity = light ? 0.78 + originalLightness * 0.10 : min(0.68, max(0.54, originalLightness))
    let h = CoverRGB(r: r, g: g, b: b).hue / 60
    let c = (1 - abs(2 * luminosity - 1)) * chroma * saturation
    let x = c * (1 - abs(h.truncatingRemainder(dividingBy: 2) - 1))
    let m = luminosity - c / 2
    let channels: (Double, Double, Double) = switch h {
    case ..<1: (c, x, 0)
    case ..<2: (x, c, 0)
    case ..<3: (0, c, x)
    case ..<4: (0, x, c)
    case ..<5: (x, 0, c)
    default: (c, 0, x)
    }
    return Color(red: channels.0 + m, green: channels.1 + m, blue: channels.2 + m)
}

func lyricCacheKey(_ text: String) -> String {
    SHA256.hash(data: Data(text.utf8)).map { String(format: "%02x", $0) }.joined()
}
