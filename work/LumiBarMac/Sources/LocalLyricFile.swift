import Foundation
import UniformTypeIdentifiers

enum LocalLyricFile: String, CaseIterable {
    case lrc, ttml

    static let maximumBytes = 1_048_576
    private static let lrcMarker = "[lumibar:1]"

    var contentType: UTType {
        switch self {
        case .lrc: UTType(filenameExtension: rawValue, conformingTo: .plainText) ?? .plainText
        case .ttml: UTType(filenameExtension: rawValue, conformingTo: .xml) ?? .xml
        }
    }

    static func preferred(for lines: [Lyric]) -> Self {
        lines.contains { !$0.words.isEmpty || $0.translation?.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty == false }
            ? .ttml : .lrc
    }

    func decode(_ data: Data) -> (lines: [Lyric], isLumiBarExport: Bool) {
        guard data.count <= Self.maximumBytes else { return ([], false) }
        switch self {
        case .lrc:
            guard let text = String(data: data, encoding: .utf8) else { return ([], false) }
            let firstLine = text.drop(while: { $0 == "\u{FEFF}" }).prefix(while: { !$0.isNewline })
            let isExport = firstLine == Self.lrcMarker
            return (isExport ? Self.readLRC(text) : Lyric.parse(text), isExport)
        case .ttml:
            guard Self.isUnicodeXMLWithoutDTD(data) else { return ([], false) }
            return AMLLTiming.parseLocal(data)
        }
    }

    private static func isUnicodeXMLWithoutDTD(_ data: Data) -> Bool {
        // Local lyrics do not need DTDs or legacy XML encodings. Recognize the
        // text before XMLParser can process any entity declarations.
        let encodings: [String.Encoding] = [.utf8, .utf16LittleEndian, .utf16BigEndian, .utf32LittleEndian, .utf32BigEndian]
        for encoding in encodings {
            guard let xml = String(data: data, encoding: encoding), !xml.contains("\0") else { continue }
            let document = xml.drop(while: { $0 == "\u{FEFF}" || $0.isWhitespace })
            guard document.first == "<" else { continue }
            guard !document.contains("<!DOCTYPE") else { return false }
            if document.hasPrefix("<?xml"), let end = document.range(of: "?>")?.lowerBound,
               let declaration = document[..<end].firstMatch(of: /\bencoding\s*=\s*["']([^"']+)["']/) {
                let names = ["UTF-8", "UTF-16", "UTF-16LE", "UTF-16BE", "UTF-32", "UTF-32LE", "UTF-32BE"]
                guard names.contains(declaration.1.uppercased()) else { return false }
            }
            return true
        }
        return false
    }

    func encode(lines: [Lyric], track: Track, offset: Double) -> Data? {
        guard !lines.isEmpty, lines.count < Self.maximumBytes, offset.isFinite,
              lines.allSatisfy({ (0...86_400).contains($0.time) &&
                  !$0.text.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty }) else { return nil }
        let ordered = lines.enumerated().sorted {
            $0.element.time == $1.element.time ? $0.offset < $1.offset : $0.element.time < $1.element.time
        }.map(\.element)
        let resolution: Int64 = self == .lrc ? 1_000 : 1_000_000
        let clock = Clock(resolution: resolution, offset: offset)
        let starts = clock.uniqueStarts(ordered)
        switch self {
        case .lrc: return Self.writeLRC(ordered, starts: starts, track: track)
        case .ttml: return Self.writeTTML(ordered, starts: starts, track: track, clock: clock)
        }
    }

    private struct Clock {
        let resolution: Int64
        let offset: Double
        var maximum: Int64 { 86_400 * resolution }

        func ticks(_ seconds: Double) -> Int64 {
            Int64((max(0, min(86_400, seconds - offset)) * Double(resolution)).rounded())
        }

        func uniqueStarts(_ lines: [Lyric]) -> [Int64] {
            // Slightly separate lines that would otherwise share an ID after clipping or rounding.
            var previous: Int64 = -1
            return lines.enumerated().map { index, line in
                let remaining = Int64(lines.count - index - 1)
                previous = max(previous + 1, min(ticks(line.time), maximum - remaining))
                return previous
            }
        }
    }

    private static func normalizedNewlines(_ value: String) -> String {
        value.replacingOccurrences(of: "\r\n", with: "\n").replacingOccurrences(of: "\r", with: "\n")
    }

    private static func readLRC(_ input: String) -> [Lyric] {
        let timestamp = /^\[(\d{1,4}):(\d{2})(?:[.:](\d{1,3}))?\]/
        let offsetTag = /^\s*\[offset:([+-]?\d+)\]\s*$/.ignoresCase()
        let rows = normalizedNewlines(input).drop(while: { $0 == "\u{FEFF}" }).split(separator: "\n")
        let requestedOffset = rows.lazy.compactMap { row in
            row.firstMatch(of: offsetTag).flatMap { Double($0.1) }
        }.first ?? 0
        let offset = min(3600, max(-3600, requestedOffset / 1000))
        var byTime: [Double: [String]] = [:]
        for row in rows {
            var payload = row.drop(while: { $0 == " " || $0 == "\t" })
            var times: [Double] = []
            while let match = payload.firstMatch(of: timestamp) {
                payload = payload[match.range.upperBound...]
                guard let minutes = Double(match.1), let seconds = Double(match.2), seconds < 60 else { continue }
                let fraction = match.3.flatMap { Double("0." + $0) } ?? 0
                let value = minutes * 60 + seconds + fraction - offset
                guard value <= 86_400 else { continue }
                let time = (max(0, value) * 1000).rounded() / 1000
                if !times.contains(time) { times.append(time) }
            }
            let text = payload.first == " " ? String(payload.dropFirst()) : String(payload)
            for time in times { byTime[time, default: []].append(text) }
        }
        return byTime.keys.sorted().compactMap { time in
            let text = byTime[time, default: []].joined(separator: "\n")
            return text.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty ? nil : Lyric(time: time, text: text)
        }
    }

    private static func writeLRC(_ lines: [Lyric], starts: [Int64], track: Track) -> Data? {
        var output = Data((lrcMarker + "\n").utf8)
        for (tag, value) in [("ti", track.title), ("ar", track.artist), ("al", track.album)] where !value.isEmpty {
            let line = normalizedNewlines(value).replacingOccurrences(of: "\n", with: " ")
            guard append("[\(tag):\(line)]\n", to: &output) else { return nil }
        }
        for (line, start) in zip(lines, starts) {
            let tag = String(format: "[%02lld:%02lld.%03lld]", start / 60_000, start / 1000 % 60, start % 1000)
            // Separate text from the time tags, including text that starts with a literal bracket.
            for text in normalizedNewlines(line.text).split(separator: "\n", omittingEmptySubsequences: false) {
                guard append(tag + " " + text + "\n", to: &output) else { return nil }
            }
        }
        return output
    }

    private static func writeTTML(_ lines: [Lyric], starts: [Int64], track: Track, clock: Clock) -> Data? {
        var output = Data()
        let opening = """
            <?xml version="1.0" encoding="UTF-8"?>
            <tt xmlns="http://www.w3.org/ns/ttml" xmlns:ttp="http://www.w3.org/ns/ttml#parameter" xmlns:ttm="http://www.w3.org/ns/ttml#metadata" xmlns:lumi="\(AMLLTiming.lumiBarNamespace)" xml:lang="und" ttp:timeBase="media" lumi:timing="relative">
            """
        guard let title = escapeXML(track.title, attribute: true),
              append(opening + "\n<head><metadata><ttm:title>\(title)</ttm:title></metadata></head>\n<body><div>\n", to: &output) else { return nil }
        for (index, line) in lines.enumerated() {
            let start = starts[index]
            var base = start
            var words: [(begin: Int64, end: Int64, text: String)] = []
            for word in line.words {
                guard (0...86_400).contains(word.time), word.duration.isFinite, word.duration >= 0,
                      word.time + word.duration <= 86_400, !word.text.isEmpty,
                      let text = escapeXML(word.text, attribute: false) else { return nil }
                let begin = clock.ticks(word.time)
                let end = clock.ticks(word.time + word.duration)
                // A span completely clipped by a positive offset should still contribute its text.
                let displayedEnd = begin == end && word.duration > 0 ? min(clock.maximum, begin + 1) : end
                base = min(base, begin)
                words.append((begin, displayedEnd, text))
            }
            let next = starts.indices.contains(index + 1) ? starts[index + 1] : nil
            let trackEnd = track.duration.isFinite && track.duration > 0
                ? Int64((min(86_400, track.duration) * 1_000_000).rounded()) : nil
            let inferredEnd = next ?? trackEnd ?? min(clock.maximum, start + 2_000_000)
            let end = max(min(clock.maximum, base + 1), min(clock.maximum, start + 1), inferredEnd,
                          words.map(\.end).max() ?? 0)
            var paragraph = "<p begin=\"\(ttmlTime(base))\" end=\"\(ttmlTime(end))\" xml:space=\"preserve\""
            if base != start { paragraph += " lumi:lineBegin=\"\(ttmlTime(start))\"" }
            if !words.isEmpty && line.words.map(\.text).joined() != line.text {
                guard let original = escapeXML(line.text, attribute: true) else { return nil }
                paragraph += " lumi:original=\"\(original)\""
            }
            paragraph += ">"
            if words.isEmpty {
                guard let text = escapeXML(line.text, attribute: false) else { return nil }
                paragraph += text
            } else {
                for word in words {
                    paragraph += "<span begin=\"\(ttmlTime(word.begin - base))\" end=\"\(ttmlTime(word.end - base))\">\(word.text)</span>"
                    if paragraph.utf8.count > maximumBytes { return nil }
                }
            }
            if let translation = line.translation, !translation.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty {
                guard let text = escapeXML(translation, attribute: false) else { return nil }
                paragraph += "<span ttm:role=\"x-translation\">\(text)</span>"
            }
            guard append(paragraph + "</p>\n", to: &output) else { return nil }
        }
        return append("</div></body></tt>\n", to: &output) ? output : nil
    }

    private static func ttmlTime(_ microseconds: Int64) -> String {
        String(format: "%lld.%06llds", microseconds / 1_000_000, microseconds % 1_000_000)
    }

    private static func escapeXML(_ text: String, attribute: Bool) -> String? {
        guard text.utf8.count <= maximumBytes else { return nil }
        var escaped = ""
        for scalar in text.unicodeScalars {
            switch scalar {
            case "&": escaped += "&amp;"
            case "<": escaped += "&lt;"
            case ">": escaped += "&gt;"
            case "\"" where attribute: escaped += "&quot;"
            case "\u{9}" where attribute: escaped += "&#9;"
            case "\u{A}" where attribute: escaped += "&#10;"
            case "\u{D}": escaped += "&#13;"
            case "\u{A}": escaped += "<br/>"
            default:
                guard scalar == "\u{9}" || (0x20...0xD7FF).contains(scalar.value) || (0xE000...0xFFFD).contains(scalar.value)
                    || scalar.value >= 0x10000 else { return nil }
                escaped.unicodeScalars.append(scalar)
            }
        }
        return escaped
    }

    private static func append(_ text: String, to output: inout Data) -> Bool {
        guard output.count + text.utf8.count <= maximumBytes else { return false }
        output.append(contentsOf: text.utf8)
        return true
    }
}
