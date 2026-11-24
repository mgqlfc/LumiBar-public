import CoreText
import Foundation
import NaturalLanguage

enum NotchLyricBreaks {
    private struct Boundary {
        let start: Int
        let previousEnd: Int
        let wordsBefore: Int
        let penalty: Double
    }

    private struct Choice {
        let pieces: Int
        let cost: Double
        let next: Int
    }

    static func ranges(for text: String, using typesetter: CTTypesetter, width: CGFloat,
                       fullWidth: CGFloat) -> [NSRange]? {
        // Keep planning work bounded for malformed cues with thousands of characters.
        guard text.utf16.count <= 512, width > 0 else { return nil }
        let characters = Array(text)
        guard let first = characters.firstIndex(where: { !$0.isWhitespace }),
              let last = characters.lastIndex(where: { !$0.isWhitespace }), first < last else { return nil }

        var offsets = [0]
        for character in characters { offsets.append(offsets[offsets.count - 1] + String(character).utf16.count) }

        let tokenizer = NLTokenizer(unit: .word)
        tokenizer.string = text
        let words = tokenizer.tokens(for: text.startIndex..<text.endIndex).map { NSRange($0, in: text) }
        let wordEdges = Set(words.flatMap { [$0.location, NSMaxRange($0)] })
        let wordStarts = Set(words.map(\.location))
        let wordInteriors = Set(words.filter { $0.length > 1 }.flatMap { ($0.location + 1)..<NSMaxRange($0) })
        let source = text as NSString
        let wordEnds = Dictionary(words.map { (NSMaxRange($0), source.substring(with: $0).lowercased()) },
                                  uniquingKeysWith: { first, _ in first })
        let phraseEnd: Set<Character> = [",", ".", "!", "?", ";", ":", "，", "、", "。", "！", "？", "；", "：", "…", "—"]
        let opening: Set<Character> = ["(", "[", "{", "（", "［", "｛", "「", "『", "《", "〈", "【", "“", "‘", "¿", "¡"]
        let closing: Set<Character> = [",", ".", ":", ";", "!", "?", ")", "]", "}", "，", "、", "。", "：", "；", "！", "？", "）", "」", "』", "》", "〉", "】", "”", "’", "…"]
        let joinsWord: Set<Character> = ["'", "’", "-", "‑"]
        let particles: Set<Character> = ["的", "地", "得", "了", "着", "著", "过", "過", "吗", "嗎", "呢", "吧", "啊"]
        let leadsIntoPhrase: Set<String> = ["a", "an", "the", "at", "in", "on", "to", "of", "by", "for", "with", "from", "and", "or", "but"]

        var boundaries: [Boundary] = [.init(start: offsets[first], previousEnd: offsets[first], wordsBefore: 0, penalty: 0)]
        var lastVisible = first
        var wordCount = 0
        for index in first...last {
            let offset = offsets[index]
            let character = characters[index]
            if index > first && !character.isWhitespace {
                let previous = characters[lastVisible]
                let separated = lastVisible < index - 1
                let previousEnd = offsets[lastVisible + 1]
                let insideWord = wordInteriors.contains(offset)
                let clause = phraseEnd.contains(previous) || (closing.contains(previous) &&
                    lastVisible > first && phraseEnd.contains(characters[lastVisible - 1]))
                let startsWithParticle = particles.contains(character) && !wordInteriors.contains(offsets[index + 1])
                let afterLinkWord = wordEnds[previousEnd].map(leadsIntoPhrase.contains) ?? false
                let kind: Double
                if opening.contains(previous) || closing.contains(character) {
                    kind = 3
                } else if !separated && (joinsWord.contains(previous) || joinsWord.contains(character)) {
                    kind = 2
                } else if insideWord {
                    kind = 1.4
                } else if startsWithParticle || afterLinkWord {
                    kind = 0.48
                } else if clause {
                    kind = 0
                } else if separated || wordEdges.contains(offset) {
                    kind = 0.12
                } else {
                    kind = 1.4
                }
                boundaries.append(.init(start: offset, previousEnd: previousEnd, wordsBefore: wordCount, penalty: kind))
            }
            if wordStarts.contains(offset) { wordCount += 1 }
            if !character.isWhitespace { lastVisible = index }
        }
        boundaries.append(.init(start: offsets[last + 1], previousEnd: offsets[last + 1], wordsBefore: wordCount, penalty: 0))

        let target = fullWidth / max(2, ceil(fullWidth / width))
        // Only use character boundaries when a word cannot fit on its own.
        return plan(boundaries.filter { $0.penalty < 1 }, using: typesetter, width: width, target: target)
            ?? plan(boundaries, using: typesetter, width: width, target: target)
    }

    private static func plan(_ boundaries: [Boundary], using typesetter: CTTypesetter,
                             width: CGFloat, target: CGFloat) -> [NSRange]? {
        let end = boundaries.count - 1
        var choices = Array<Choice?>(repeating: nil, count: boundaries.count)
        choices[end] = .init(pieces: 0, cost: 0, next: end)
        for index in (0..<end).reversed() {
            let start = boundaries[index]
            for next in (index + 1)...end {
                let stop = boundaries[next]
                let count = stop.previousEnd - start.start
                guard count > 0 else { continue }
                let line = CTTypesetterCreateLine(typesetter, CFRange(location: start.start, length: count))
                let measured = CTLineGetTypographicBounds(line, nil, nil, nil)
                if measured > width + 0.01 { break }
                guard let remaining = choices[next] else { continue }
                let difference = (measured - target) / width
                let shortTail = next == end && index > 0 && stop.wordsBefore - start.wordsBefore <= 1 && measured < target * 0.74
                let candidate = Choice(pieces: remaining.pieces + 1,
                                       cost: remaining.cost + difference * difference * 2 + stop.penalty + (shortTail ? 0.75 : 0),
                                       next: next)
                if let best = choices[index], best.pieces < candidate.pieces ||
                    (best.pieces == candidate.pieces && best.cost <= candidate.cost) { continue }
                choices[index] = candidate
            }
        }
        guard choices[0] != nil else { return nil }
        var result: [NSRange] = []
        var index = 0
        while index < end, let choice = choices[index] {
            let start = boundaries[index].start
            result.append(NSRange(location: start, length: boundaries[choice.next].previousEnd - start))
            index = choice.next
        }
        return result
    }
}
