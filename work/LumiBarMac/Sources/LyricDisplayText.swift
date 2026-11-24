import Foundation

struct LyricDisplayText {
    let string: String
    private let wideGaps: [NSRange]

    init(_ source: String, normalizesSpacing: Bool = true) {
        guard normalizesSpacing, source.contains("  ") || source.contains("\t") || source.contains("\u{3000}") else {
            string = source
            wideGaps = []
            return
        }

        // Keep CJK spacing and typographic spaces, such as the narrow no-break space in French.
        let gaps = source.matches(of: /[\p{Latin}0-9][\p{M}\p{P}]*([ \t\u{3000}]+)(?=\p{P}*[\p{Latin}0-9])/)
            .compactMap { match in
                match.1 == " " ? nil : NSRange(match.1.startIndex..<match.1.endIndex, in: source)
            }
        wideGaps = gaps
        guard !gaps.isEmpty else {
            string = source
            return
        }
        let display = NSMutableString(string: source)
        for gap in gaps.reversed() { display.replaceCharacters(in: gap, with: " ") }
        string = display as String
    }

    func timedWords(_ words: [Lyric.Word]) -> [Lyric.Word] {
        guard !wideGaps.isEmpty, !words.isEmpty else { return words }
        let display = string as NSString
        var sourceOffset = 0
        var displayOffset = 0
        var gapIndex = 0
        var removed = 0
        return words.map { word in
            sourceOffset += word.text.utf16.count
            while gapIndex < wideGaps.count, NSMaxRange(wideGaps[gapIndex]) <= sourceOffset {
                removed += wideGaps[gapIndex].length - 1
                gapIndex += 1
            }
            // The first unit of each gap becomes a space even if the gap spans several timed words.
            let removedInGap = gapIndex < wideGaps.count ? max(0, sourceOffset - wideGaps[gapIndex].location - 1) : 0
            let end = sourceOffset - removed - removedInGap
            let range = NSRange(location: displayOffset, length: end - displayOffset)
            displayOffset = end
            return Lyric.Word(time: word.time, duration: word.duration, text: display.substring(with: range))
        }
    }
}
