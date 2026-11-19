import AppKit
import CryptoKit
import Foundation
import SwiftUI

enum Player: String, CaseIterable, Sendable {
    case music = "com.apple.Music"
    case spotify = "com.spotify.client"

    var name: String { self == .music ? "Apple Music" : "Spotify" }
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
    var key: String { [player.rawValue, title, artist, album, String(Int(duration))].joined(separator: "|") }

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

    static func fetch(_ track: Track) async -> [Lyric] {
        var parts = URLComponents(string: "https://lrclib.net/api/get")!
        parts.queryItems = [.init(name: "track_name", value: track.title), .init(name: "artist_name", value: track.artist),
                            .init(name: "album_name", value: track.album),
                            .init(name: "duration", value: String(Int(track.duration.rounded())))]
        struct Response: Decodable { let syncedLyrics: String? }
        guard let (data, response) = try? await URLSession.shared.data(for: URLRequest(url: parts.url!, timeoutInterval: 8)),
              (response as? HTTPURLResponse)?.statusCode == 200,
              let text = (try? JSONDecoder().decode(Response.self, from: data))?.syncedLyrics else { return [] }
        return parse(text)
    }
}

let neutralCoverColors = [Color(red: 0.54, green: 0.60, blue: 0.85),
                          Color(red: 0.65, green: 0.58, blue: 0.87)]

@MainActor
func coverColors(_ image: NSImage) -> [Color] {
    let fallback = neutralCoverColors
    var rect = CGRect(origin: .zero, size: image.size)
    guard let source = image.cgImage(forProposedRect: &rect, context: nil, hints: nil),
          let context = CGContext(data: nil, width: 20, height: 20, bitsPerComponent: 8, bytesPerRow: 80,
                                  space: CGColorSpace(name: CGColorSpace.sRGB)!,
                                  bitmapInfo: CGImageAlphaInfo.premultipliedLast.rawValue) else { return fallback }
    context.draw(source, in: CGRect(x: 0, y: 0, width: 20, height: 20))
    guard let pixels = context.data?.assumingMemoryBound(to: UInt8.self) else { return fallback }
    var bins = Array(repeating: (weight: 0.0, x: 0.0, y: 0.0), count: 18)
    for index in stride(from: 0, to: 1600, by: 4) where pixels[index + 3] > 200 {
        let color = NSColor(srgbRed: Double(pixels[index]) / 255, green: Double(pixels[index + 1]) / 255,
                            blue: Double(pixels[index + 2]) / 255, alpha: 1)
        let weight = color.saturationComponent * color.brightnessComponent
        guard weight > 0.12 else { continue }
        let hue = color.hueComponent
        let bin = min(17, Int(hue * 18))
        bins[bin].weight += weight
        bins[bin].x += cos(hue * 2 * .pi) * weight
        bins[bin].y += sin(hue * 2 * .pi) * weight
    }
    guard let best = bins.max(by: { $0.weight < $1.weight }), best.weight > 0 else { return fallback }
    let hue = (atan2(best.y, best.x) / (2 * .pi) + 1).truncatingRemainder(dividingBy: 1)
    return [Color(hue: hue, saturation: 0.56, brightness: 0.95),
            Color(hue: (hue + 0.08).truncatingRemainder(dividingBy: 1), saturation: 0.40, brightness: 1)]
}

func lyricCacheKey(_ text: String) -> String {
    SHA256.hash(data: Data(text.utf8)).map { String(format: "%02x", $0) }.joined()
}
