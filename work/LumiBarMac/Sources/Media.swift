import AppKit
import CryptoKit
import Foundation
import SwiftUI

struct Player: Hashable, Sendable {
    let rawValue: String
    let name: String

    static let netease = Self(rawValue: "com.netease.163music", name: "网易云音乐")
    static let music = Self(rawValue: "com.apple.Music", name: "Apple Music")
    static let spotify = Self(rawValue: "com.spotify.client", name: "Spotify")
    static let allCases = [netease, music, spotify]

    static func == (lhs: Self, rhs: Self) -> Bool { lhs.rawValue == rhs.rawValue }
    func hash(into hasher: inout Hasher) { hasher.combine(rawValue) }
}

struct Track: Equatable, Sendable {
    let player: Player
    let title: String
    let artist: String
    let album: String
    var duration: Double
    var position: Double
    let playing: Bool
    let artworkURL: URL?
    let pid: pid_t
    var observedAt: Date
    var catalogID: String? = nil
    var estimatedPosition = false
    var systemSession = false
    var playbackRate = 1.0
    var webDurationVerified = false
    var key: String {
        if player == .netease, let catalogID { return player.rawValue + "|" + catalogID }
        return [player.rawValue, title, artist, album, String(Int(duration))].joined(separator: "|")
    }

    func position(at now: Date) -> Double {
        let elapsed = position + (playing ? now.timeIntervalSince(observedAt) * playbackRate : 0)
        return min(duration > 0 ? duration : .infinity, max(0, elapsed))
    }

    func displayPosition(at now: Date = .now, calibration: Double, frozen: Bool = false) -> Double {
        let position = lyricPosition(at: now, offset: calibration, frozen: frozen)
        return min(duration > 0 ? duration : .infinity, max(0, position))
    }

    func lyricPosition(at now: Date = .now, offset: Double, frozen: Bool = false) -> Double {
        (frozen ? position : position(at: now)) + offset
    }

    mutating func setPlaybackPosition(_ seconds: Double, at now: Date = .now) {
        position = min(duration > 0 ? duration : .infinity, max(0, seconds))
        observedAt = now
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

struct Lyric: Identifiable, Sendable, Codable {
    struct Word: Sendable, Codable, Equatable {
        let time: Double
        let duration: Double
        let text: String
    }

    let time: Double
    let text: String
    var translation: String? = nil
    var words: [Word] = []
    var id: Double { time }

    var firstWordTime: Double? {
        words.first { $0.time.isFinite && $0.time >= 0 &&
            !$0.text.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty
        }?.time
    }

    var vocalStartTime: Double { firstWordTime ?? time }

    var isVocalLine: Bool {
        let spokenText = text.trimmingCharacters(in: .whitespacesAndNewlines)
        return time.isFinite && time >= 0 && !spokenText.isEmpty && !Self.isNonVocalText(spokenText)
    }

    static func isNonVocalText(_ text: String) -> Bool {
        let credits = /^(?:作词|作曲|编曲|制作|演唱|歌手|词|曲|混音|录音|Lyrics by|Composed by)\s*[:：]/.ignoresCase()
        let instrumental = /^(?:(?:此歌曲为)?(?:没有填词的)?纯音乐(?:[，,]\s*请(?:您)?欣赏)?|\[?instrumental(?:\s+version)?\]?|no lyrics(?: available)?)[。！!.]?$/
            .ignoresCase()
        return text.firstMatch(of: credits) != nil || text.firstMatch(of: instrumental) != nil
    }

    static func firstVocal(in lyrics: [Lyric], for track: Track, acceptsManualText: Bool = false) -> (index: Int, time: Double)? {
        let title = track.title.trimmingCharacters(in: .whitespacesAndNewlines)
        let artist = track.artist.trimmingCharacters(in: .whitespacesAndNewlines)
        var manualFallback: (index: Int, time: Double)?
        for (index, lyric) in lyrics.enumerated() {
            let text = lyric.text.trimmingCharacters(in: .whitespacesAndNewlines)
            guard !text.isEmpty, lyric.time.isFinite, lyric.time >= 0 else { continue }
            let isMetadata = isNonVocalText(text)
            if isMetadata && !acceptsManualText { continue }
            let wordTime = lyric.firstWordTime
            let vocal = (index: index, time: wordTime ?? lyric.time)
            if acceptsManualText {
                if track.duration.isFinite, track.duration > 0, vocal.time >= track.duration { continue }
                // Keep recognized headers out of the intro when a vocal line follows.
                if manualFallback == nil { manualFallback = vocal }
            }
            if isMetadata { continue }
            if wordTime != nil { return vocal }
            if lyric.time <= 2,
               text.compare(title, options: .caseInsensitive) == .orderedSame ||
               text.compare(artist, options: .caseInsensitive) == .orderedSame { continue }
            return vocal
        }
        return manualFallback
    }

    static func hasSyncedVocals(in lyrics: [Lyric], for track: Track, acceptsManualText: Bool = false) -> Bool {
        guard let first = firstVocal(in: lyrics, for: track, acceptsManualText: acceptsManualText) else { return false }
        return !track.duration.isFinite || track.duration <= 0 || first.time < track.duration
    }

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

    static func parse(_ original: String?, translation: String?, wordTiming: String? = nil,
                      wordTranslation: String? = nil) -> [Lyric] {
        let timedLines = wordTiming.map(parseWordTiming) ?? []
        var lines = timedLines.isEmpty ? (original.map(parse) ?? []) : timedLines
        if !timedLines.isEmpty, let wordTranslation {
            addTranslations(parse(wordTranslation), to: &lines)
        }
        if let translation { addTranslations(parse(translation), to: &lines) }
        return lines
    }

    private static func parseWordTiming(_ input: String) -> [Lyric] {
        guard input.utf8.count <= 1_048_576 else { return [] }
        let lineTag = /^\[(\d{1,9}),(\d{1,9})\]/
        let wordTag = /\((\d{1,9}),(\d{1,9}),\d+\)/
        var lines: [Double: Lyric] = [:]
        for line in input.split(whereSeparator: \.isNewline) {
            guard let header = line.firstMatch(of: lineTag), let start = Double(header.1),
                  let duration = Double(header.2), duration > 0 else { continue }
            let payload = line[header.range.upperBound...]
            let tags = payload.matches(of: wordTag)
            guard tags.first?.range.lowerBound == payload.startIndex else { continue }
            var words: [Word] = []
            for index in tags.indices {
                let tag = tags[index]
                let next = index + 1
                let end = tags.indices.contains(next) ? tags[next].range.lowerBound : payload.endIndex
                let text = payload[tag.range.upperBound..<end]
                guard let wordStart = Double(tag.1), let wordDuration = Double(tag.2), !text.isEmpty else { continue }
                words.append(.init(time: wordStart / 1000, duration: wordDuration / 1000, text: String(text)))
            }
            let text = words.map(\.text).joined()
            guard !text.trimmingCharacters(in: .whitespaces).isEmpty else { continue }
            let time = start / 1000
            if lines[time] == nil { lines[time] = Lyric(time: time, text: text, words: words) }
        }
        return lines.values.sorted { $0.time < $1.time }
    }

    static func addTranslations(_ translated: [Lyric], to lines: inout [Lyric]) {
        var index = 0
        for line in lines.indices {
            let time = lines[line].time
            while translated.indices.contains(index + 1),
                  abs(translated[index + 1].time - time) < abs(translated[index].time - time) {
                index += 1
            }
            guard translated.indices.contains(index), abs(translated[index].time - time) <= 0.2 else { continue }
            let nextLine = line + 1
            let translatedTime = translated[index].time
            if lines.indices.contains(nextLine), abs(lines[nextLine].time - translatedTime) < abs(time - translatedTime) {
                continue
            }
            let candidate = translated[index].text
            index += 1
            if lines[line].translation == nil, candidate != lines[line].text.trimmingCharacters(in: .whitespaces) {
                lines[line].translation = candidate
            }
        }
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
