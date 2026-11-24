import Foundation

/// Reads the vocal track and inline translation from a public AMLL TTML file.
enum AMLLTiming {
    static let lumiBarNamespace = "https://github.com/mgqlfc/LumiBar-public/ttml"

    static func parse(_ data: Data) -> [Lyric] {
        read(data, localFile: false).lines
    }

    static func parseLocal(_ data: Data) -> (lines: [Lyric], isLumiBarExport: Bool) {
        read(data, localFile: true)
    }

    private static func read(_ data: Data, localFile: Bool) -> (lines: [Lyric], isLumiBarExport: Bool) {
        guard data.count <= 1_048_576 else { return ([], false) }
        let delegate = Reader(localFile: localFile)
        let parser = XMLParser(data: data)
        parser.shouldResolveExternalEntities = false
        parser.delegate = delegate
        guard parser.parse() else { return ([], false) }
        var seen = Set<Double>()
        let lines = delegate.lines.sorted { $0.time < $1.time }.filter { seen.insert($0.time).inserted }
        return (lines, delegate.isLumiBarExport)
    }

    private final class Reader: NSObject, XMLParserDelegate {
        private struct HeadTranslation {
            let depth: Int
            let id: String
            var text = ""
        }

        private struct Segment {
            let depth: Int
            let start: Double
            let end: Double
            var text = ""
        }

        private struct Line {
            let depth: Int
            let start: Double
            let id: String?
            let relativeBase: Double
            let preserveWhitespace: Bool
            let original: String?
            var text = ""
            var untimed = ""
            var originalWords: [Lyric.Word] = []
            var translation = ""
        }

        private var depth = 0
        private var line: Line?
        private var word: Segment?
        private var translationDepth: Int?
        private var ignoredDepth: Int?
        private var translatedGroup: Int?
        private var headTranslation: HeadTranslation?
        private var translations: [String: String] = [:]
        private var entries: [(id: String?, lyric: Lyric)] = []
        private let localFile: Bool
        private var relativeTiming = false
        private(set) var isLumiBarExport = false

        init(localFile: Bool) {
            self.localFile = localFile
        }

        var lines: [Lyric] {
            entries.map { entry in
                var lyric = entry.lyric
                if let id = entry.id, let value = translations[id],
                   lyric.translation == nil, lyric.text != value { lyric.translation = value }
                return lyric
            }
        }

        func parser(_ parser: XMLParser, didStartElement name: String, namespaceURI: String?,
                    qualifiedName: String?, attributes: [String: String]) {
            depth += 1
            let element = name.split(separator: ":").last
            if localFile, depth == 1, element == "tt" {
                isLumiBarExport = attributes["xmlns:lumi"] == AMLLTiming.lumiBarNamespace &&
                    attributes["lumi:timing"] == "relative"
                // AMLL and Apple lyric catalogs time words against the whole recording.
                relativeTiming = isLumiBarExport || (attributes["xmlns:itunes"] == nil && attributes["xmlns:amll"] == nil)
            }
            if element == "p", let start = attributes["begin"].flatMap(AMLLTiming.seconds) {
                let authoredStart = isLumiBarExport ? attributes["lumi:lineBegin"].flatMap(AMLLTiming.seconds) : nil
                line = Line(depth: depth, start: authoredStart ?? start, id: attributes["itunes:key"] ?? attributes["xml:id"],
                            relativeBase: relativeTiming ? start : 0,
                            preserveWhitespace: localFile && attributes["xml:space"] == "preserve",
                            original: isLumiBarExport ? attributes["lumi:original"] : nil)
                return
            }
            if line == nil {
                if element == "translation", translatedGroup == nil, attributes["type"]?.lowercased() != "replacement",
                   Self.isTranslationLanguage(attributes["xml:lang"]) {
                    translatedGroup = depth
                }
                if element == "text", translatedGroup != nil, let id = attributes["for"] {
                    headTranslation = HeadTranslation(depth: depth, id: id)
                }
                return
            }
            if localFile, element == "br" {
                self.parser(parser, foundCharacters: "\n")
                return
            }
            guard line != nil, ignoredDepth == nil, translationDepth == nil else { return }
            switch attributes["ttm:role"] ?? attributes["role"] {
            case "x-bg", "x-roman":
                ignoredDepth = depth
            case "x-translation":
                if line?.translation.isEmpty == true, localFile || Self.isTranslationLanguage(attributes["xml:lang"]) {
                    translationDepth = depth
                }
                else { ignoredDepth = depth }
            default:
                if word == nil, let start = attributes["begin"].flatMap(AMLLTiming.seconds),
                   let end = attributes["end"].flatMap(AMLLTiming.seconds), end > start || (localFile && end == start) {
                    let prefix = line?.untimed ?? ""
                    let first = line?.originalWords.isEmpty == true
                    let base = line?.relativeBase ?? 0
                    word = Segment(depth: depth, start: base + start, end: base + end,
                                   text: first && line?.preserveWhitespace != true ? prefix.trimmingCharacters(in: .whitespacesAndNewlines) : prefix)
                    line?.untimed = ""
                }
            }
        }

        func parser(_ parser: XMLParser, foundCharacters text: String) {
            if headTranslation != nil { headTranslation?.text += text }
            guard line != nil, ignoredDepth == nil else { return }
            if translationDepth != nil {
                line?.translation += text
            } else {
                line?.text += text
                if word != nil { word?.text += text }
                else if line?.preserveWhitespace == true { line?.untimed += text }
                else { line?.untimed += text.components(separatedBy: .newlines).joined(separator: " ") }
            }
        }

        func parser(_ parser: XMLParser, foundCDATA data: Data) {
            if localFile, let text = String(data: data, encoding: .utf8) { self.parser(parser, foundCharacters: text) }
        }

        func parser(_ parser: XMLParser, didEndElement name: String, namespaceURI: String?, qualifiedName: String?) {
            defer { depth -= 1 }
            if let entry = headTranslation, entry.depth == depth {
                let value = entry.text.trimmingCharacters(in: .whitespacesAndNewlines)
                if !value.isEmpty, translations[entry.id] == nil { translations[entry.id] = value }
                headTranslation = nil
            }
            if translatedGroup == depth { translatedGroup = nil }
            if ignoredDepth == depth { ignoredDepth = nil; return }
            if translationDepth == depth { translationDepth = nil; return }
            if let segment = word, segment.depth == depth {
                let content = line?.preserveWhitespace == true ? segment.text : segment.text.trimmingCharacters(in: .whitespacesAndNewlines)
                if !content.isEmpty, segment.end <= 86_400 {
                    line?.originalWords.append(.init(time: segment.start, duration: segment.end - segment.start, text: segment.text))
                }
                word = nil
            }
            guard let rendered = line, rendered.depth == depth else { return }
            line = nil
            let raw = rendered.original ?? rendered.text
            guard !raw.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty else { return }
            let text = rendered.preserveWhitespace ? raw : raw.trimmingCharacters(in: .whitespacesAndNewlines)
            let translation = rendered.preserveWhitespace ? rendered.translation : rendered.translation.trimmingCharacters(in: .whitespacesAndNewlines)
            var words = rendered.originalWords
            if let last = words.popLast() {
                let suffix = rendered.preserveWhitespace ? rendered.untimed : rendered.untimed.trimmingCharacters(in: .whitespacesAndNewlines)
                words.append(.init(time: last.time, duration: last.duration, text: last.text + suffix))
            }
            let lyric = Lyric(time: rendered.start, text: text,
                              translation: translation.isEmpty || (!localFile && text == translation) ? nil : translation, words: words)
            entries.append((rendered.id, lyric))
        }

        private static func isTranslationLanguage(_ language: String?) -> Bool {
            let language = language?.lowercased() ?? ""
            return language.isEmpty || language == "zh" || language.hasPrefix("zh-")
        }
    }

    private static func seconds(_ value: String) -> Double? {
        let fields = value.split(separator: ":")
        let time: Double
        if (2...3).contains(fields.count), fields.allSatisfy({ Double($0) != nil }) {
            time = fields.reduce(0) { $0 * 60 + (Double($1) ?? 0) }
        } else if value.hasSuffix("ms"), let amount = Double(value.dropLast(2)) {
            time = amount / 1000
        } else if value.hasSuffix("s"), let amount = Double(value.dropLast()) {
            time = amount
        } else if let amount = Double(value) {
            time = amount
        } else {
            return nil
        }
        return time.isFinite && (0...86_400).contains(time) ? time : nil
    }
}
