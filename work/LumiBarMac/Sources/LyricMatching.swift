import Foundation

struct LyricRecording: Sendable {
    let id: String
    let title: String
    let artist: String
    let album: String
    let duration: Double
    var confirmedID = false
    var accessKey: String? = nil
    var allowsAlbumArtist = false

    func match(to track: Track) -> LyricMatchScore? {
        let titleKey = Self.identity(title)
        let artistKey = Self.identity(artist)
        let trackArtist = Self.identity(track.artist)
        let matchingArtist = !artistKey.isEmpty && artistKey == trackArtist
        let matchingTitle = !titleKey.isEmpty && titleKey == Self.identity(track.title)

        let albumKey = Self.identity(album)
        let trackAlbum = Self.identity(track.album)
        let sameAlbum = !albumKey.isEmpty && albumKey == trackAlbum
        if !confirmedID, !sameAlbum, Self.version(in: album) != Self.version(in: track.album) { return nil }
        let hasDurations = duration.isFinite && duration > 0 && track.duration.isFinite && track.duration > 0
        let difference = hasDurations ? abs(duration - track.duration) : nil
        let closeDuration = (difference ?? .infinity) <= 1.5
        let artistUsesAnotherScript = Self.crossScript(artistKey, trackArtist)
        let titleUsesAnotherScript = Self.crossScript(titleKey, Self.identity(track.title))
        let localizedTitle = titleUsesAnotherScript && matchingArtist && sameAlbum && closeDuration
            && Self.version(in: title) == Self.version(in: track.title)
        guard matchingTitle || localizedTitle || (confirmedID && matchingArtist) else { return nil }
        // Some soundtrack catalogs credit the production in place of the track performers.
        let productionName = album.components(separatedBy: " (")[0].components(separatedBy: " - ")[0]
        let matchingAlbumArtist = allowsAlbumArtist && sameAlbum && (difference ?? .infinity) <= 2
            && artistKey.count >= 5 && artistKey == Self.identity(productionName)
        let artistDiffers = matchingTitle && sameAlbum && !artistKey.isEmpty && !trackArtist.isEmpty
            && (artistUsesAnotherScript ? closeDuration : (difference ?? .infinity) <= 0.6)
        guard confirmedID || matchingArtist || matchingAlbumArtist || artistDiffers else { return nil }
        // An exact provider ID identifies a recording, but doesn't establish that a shorter
        // or longer version of its timeline will match this playback.
        if let difference, difference > 3 { return nil }
        guard difference != nil || sameAlbum || confirmedID else { return nil }

        var score = LyricMatchScore(points: matchingArtist ? 72 : 64,
                                    reasons: [matchingTitle && matchingArtist ? "歌名、歌手一致" : matchingTitle ? "歌名一致" : "歌手一致"])
        score.canPreview = confirmedID || (matchingTitle && (matchingArtist || matchingAlbumArtist))
        if !matchingTitle { score.add(confirmedID ? -18 : -66, "歌名写法不同") }
        if matchingAlbumArtist && !matchingArtist { score.add(0, "来源使用专辑制作团体署名") }
        if artistDiffers && !matchingArtist && !matchingAlbumArtist {
            if artistUsesAnotherScript && Self.closeTransliteration(artistKey, trackArtist) {
                score.add(-12, "歌手名为近似音译")
            } else {
                // Strong timing should not outweigh a more certain recording when the artist alias is unclear.
                score.add(artistUsesAnotherScript ? -58 : -62,
                          artistUsesAnotherScript ? "歌手名可能为不同语言写法" : "歌手名不同")
            }
        }
        if sameAlbum { score.add(8, "专辑一致") }
        if !sameAlbum, !albumKey.isEmpty, !trackAlbum.isEmpty { score.add(-3, "专辑名称不同") }
        if let difference {
            score.add(16 - Int((difference * 4).rounded()),
                      "曲长相差 \(difference.formatted(.number.precision(.fractionLength(1)))) 秒")
        }
        if confirmedID { score.add(8, "播放器曲目 ID 一致") }
        return score
    }

    private static func crossScript(_ first: String, _ second: String) -> Bool {
        let cjk = /[\p{Han}\p{Hiragana}\p{Katakana}\p{Hangul}]/
        let latin = /\p{Latin}/
        if first.contains(cjk) { return !second.contains(cjk) && second.contains(latin) }
        return second.contains(cjk) && first.contains(latin)
    }

    private static func closeTransliteration(_ first: String, _ second: String) -> Bool {
        let left = identity(first.applyingTransform(.toLatin, reverse: false) ?? first)
        let right = identity(second.applyingTransform(.toLatin, reverse: false) ?? second)
        guard min(left.count, right.count) >= 4 else { return false }
        if left == right { return true }
        // Some Chinese transliterations add a final vowel, as in 肯特 (kente) and Kent.
        let (shorter, longer) = left.count < right.count ? (left, right) : (right, left)
        return longer.count == shorter.count + 1 && longer.hasPrefix(shorter) &&
            longer.last.map({ "aeiou".contains($0) }) == true
    }

    static func identity(_ raw: String) -> String {
        let decoded = raw.replacingOccurrences(of: "&nbsp;", with: " ")
            .replacingOccurrences(of: "&quot;", with: "\"")
            .replacingOccurrences(of: "&#39;", with: "'")
            .replacingOccurrences(of: "&amp;", with: "&")
        let folded = decoded.folding(options: [.caseInsensitive, .diacriticInsensitive, .widthInsensitive], locale: nil)
        return String(folded.unicodeScalars.filter { CharacterSet.alphanumerics.contains($0) })
    }

    private static func version(in text: String) -> Set<String> {
        let words = text.lowercased()
        var versions = Set<String>()
        if words.contains("现场") || words.contains("演唱会") || words.trimmingCharacters(in: .whitespaces) == "live" ||
            words.contains(/\b(?:live (?:at|from|in)|live album|in concert)\b|\(live\)/) {
            versions.insert("live")
        }
        if words.contains("伴奏") || words.contains(/\b(?:instrumental|karaoke)\b/) {
            versions.insert("instrumental")
        }
        if words.contains(/\b(?:radio edit|extended mix|sped up|slowed)\b/) {
            versions.insert("alternate")
        }
        return versions
    }
}

struct LyricMatchScore: Sendable {
    private(set) var points: Int
    private(set) var reasons: [String]
    var hasWordTiming = false
    var canPreview = true

    mutating func add(_ points: Int, _ reason: String) {
        self.points += points
        reasons.append(reason)
    }
}

struct LyricCandidate: Sendable, Identifiable {
    let source: MusicModel.LyricSource
    let recording: LyricRecording
    let lines: [Lyric]

    var id: String { "\(source.rawValue):\(recording.id)" }

    func score(for track: Track) -> LyricMatchScore? {
        guard var result = recording.match(to: track),
              let first = Lyric.firstVocal(in: lines, for: track) else { return nil }
        let vocalLines = lines[first.index...].filter { $0.time.isFinite && !$0.text.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty }
        guard !vocalLines.isEmpty else { return nil }
        if track.duration.isFinite, track.duration > 0 {
            guard first.time < track.duration else { return nil }
            let outside = vocalLines.filter { $0.time > track.duration + 2 }.count
            guard outside == 0 else { return nil }
        }

        let measured = vocalLines.filter { line in
            let timed = line.words.filter { word in
                word.duration.isFinite && word.duration > 0 && word.time.isFinite
                    && word.time >= max(0, line.time - 0.3)
                    && (track.duration <= 0 || word.time < track.duration + 2)
                    && !word.text.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty
            }
            return Set(timed.map(\.time)).count >= 2
        }.count
        if measured >= min(2, vocalLines.count), measured * 5 >= vocalLines.count * 3 {
            result.hasWordTiming = true
            result.add(40 + measured * 8 / vocalLines.count, "真实逐字时间轴 \(measured)/\(vocalLines.count) 行")
        } else {
            result.add(0, "逐行时间轴")
        }
        let translated = vocalLines.filter { $0.translation?.isEmpty == false }.count
        if translated > 0 { result.add(max(1, translated * 4 / vocalLines.count), "有译文") }
        return result
    }
}

struct RankedLyric: Sendable, Identifiable {
    let candidate: LyricCandidate
    let score: LyricMatchScore?
    var translationSource: MusicModel.LyricSource? = nil
    var rejected = false
    var id: String { candidate.id }

    static func rank(_ candidates: [LyricCandidate], for track: Track,
                     sourceOrder: [MusicModel.LyricSource], rejecting rejectedIDs: Set<String> = []) -> [Self] {
        let priorities = Dictionary(uniqueKeysWithValues: sourceOrder.enumerated().map { ($1, $0) })
        func precedes(_ lhs: Self, _ rhs: Self) -> Bool {
            let first = lhs.score?.points ?? Int.min
            let second = rhs.score?.points ?? Int.min
            if first != second { return first > second }
            let left = priorities[lhs.candidate.source] ?? sourceOrder.count
            let right = priorities[rhs.candidate.source] ?? sourceOrder.count
            if left != right { return left < right }
            return lhs.id < rhs.id
        }
        let scored = candidates.map { candidate in
            let rejected = rejectedIDs.contains(candidate.id)
            return Self(candidate: candidate, score: rejected ? nil : candidate.score(for: track), rejected: rejected)
        }.sorted(by: precedes)
        let donors = scored.filter { $0.score != nil && $0.candidate.lines.contains { $0.translation?.isEmpty == false } }
        guard !donors.isEmpty else { return scored }
        return scored.map { $0.withTranslation(from: donors, for: track) }.sorted(by: precedes)
    }

    private func withTranslation(from donors: [Self], for track: Track) -> Self {
        guard var score, score.hasWordTiming, let start = Lyric.firstVocal(in: candidate.lines, for: track)?.index else { return self }
        let body = candidate.lines[start...]
        let missing = body.filter { $0.translation?.isEmpty != false }.count
        guard missing > 0 else { return self }

        var best: (source: MusicModel.LyricSource, matches: [Range<Int>: String], filled: Int)?
        for donor in donors where donor.candidate.source != candidate.source {
            guard let donorStart = Lyric.firstVocal(in: donor.candidate.lines, for: track)?.index else { continue }
            let additions = Self.alignedTranslation(for: body, from: donor.candidate.lines[donorStart...])
            let filled = additions.keys.reduce(0) { $0 + $1.count }
            if filled > (best?.filled ?? 0) { best = (donor.candidate.source, additions, filled) }
            if filled == missing { break }
        }
        guard let best else { return self }
        var combined = candidate.lines
        for (range, translation) in best.matches.sorted(by: { $0.key.lowerBound > $1.key.lowerBound }) {
            if range.count == 1 {
                combined[range.lowerBound].translation = translation
            } else {
                let line = Self.joinTranslatedLine(combined[range], translation: translation)
                combined.replaceSubrange(range, with: CollectionOfOne(line))
            }
        }
        let hadTranslation = body.count - missing
        func translationPoints(_ count: Int) -> Int { count == 0 ? 0 : max(1, count * 4 / body.count) }
        let bonus = translationPoints(hadTranslation + best.filled) - translationPoints(hadTranslation)
        score.add(bonus, "译文来自\(best.source.title) · 对齐 \(best.matches.count) 行")
        return Self(candidate: LyricCandidate(source: candidate.source, recording: candidate.recording, lines: combined),
                    score: score, translationSource: best.source)
    }

    private static func alignedTranslation(for primary: ArraySlice<Lyric>, from donor: ArraySlice<Lyric>) -> [Range<Int>: String] {
        let tolerance = 1.2
        let primaryKeys = primary.map { LyricRecording.identity($0.text) }
        let donorKeys = donor.map { LyricRecording.identity($0.text) }
        var start = donor.startIndex
        var agreed = 0
        var distinct = Set<String>()
        var translations: [Range<Int>: String] = [:]
        var index = primary.startIndex
        while index < primary.endIndex {
            let line = primary[index]
            while start < donor.endIndex, donor[start].time < line.time - tolerance { start += 1 }
            // Some catalogs publish successive clauses as separate timed lines.
            var joinedKeys: [String] = []
            var joined = ""
            for fragment in primaryKeys.dropFirst(index - primary.startIndex).prefix(3) {
                guard !fragment.isEmpty else { break }
                joined += fragment
                joinedKeys.append(joined)
            }
            var nearest: (donorIndex: Int, nextIndex: Int, difference: Double)?
            for other in start..<donor.endIndex {
                let difference = donor[other].time - line.time
                if difference > tolerance { break }
                let donorKey = donorKeys[other - donor.startIndex]
                guard abs(difference) < (nearest?.difference ?? .infinity),
                      let length = joinedKeys.firstIndex(of: donorKey) else { continue }
                let lastIndex = index + length
                if length > 0, donor.indices.contains(other + 1),
                   primary[lastIndex].time >= donor[other + 1].time + tolerance { continue }
                nearest = (other, lastIndex + 1, abs(difference))
            }
            if let match = nearest {
                let range = index..<match.nextIndex
                let key = donorKeys[match.donorIndex - donor.startIndex]
                start = match.donorIndex + 1
                agreed += range.count
                distinct.insert(key)
                if let text = donor[match.donorIndex].translation, !text.isEmpty, LyricRecording.identity(text) != key,
                   primary[range].allSatisfy({ $0.translation?.isEmpty != false && (range.count == 1 || !$0.words.isEmpty) }) {
                    translations[range] = text
                }
                index = match.nextIndex
            } else {
                index += 1
            }
        }
        // Several distinct original lines must agree in time and order before borrowing a translation.
        guard distinct.count >= 3, agreed * 5 >= primary.count * 3 else { return [:] }
        return translations
    }

    private static func joinTranslatedLine(_ lines: ArraySlice<Lyric>, translation: String) -> Lyric {
        var words: [Lyric.Word] = []
        for line in lines {
            let first = line.words[0]
            let separator: String
            if let left = words.last?.text.last, let right = first.text.first,
               !left.isWhitespace, !right.isWhitespace, !right.isPunctuation,
               String([left, right]).wholeMatch(of: /[\p{Han}\p{Hiragana}\p{Katakana}、。，！？]{2}/) == nil {
                separator = " "
            } else {
                separator = ""
            }
            words.append(Lyric.Word(time: first.time, duration: first.duration, text: separator + first.text))
            words.append(contentsOf: line.words.dropFirst())
        }
        return Lyric(time: lines[lines.startIndex].time, text: words.map(\.text).joined(),
                     translation: translation, words: words)
    }
}

enum LyricLookup {
    typealias Source = MusicModel.LyricSource
    typealias Report = @Sendable (LyricCandidate) async -> Void

    private actor Results {
        let track: Track
        let rejectedIDs: Set<String>
        let firstMatch: @MainActor @Sendable (RankedLyric) -> Void
        private(set) var received: [LyricCandidate] = []
        private var announcedFirstMatch = false

        init(track: Track, rejectedIDs: Set<String>, firstMatch: @escaping @MainActor @Sendable (RankedLyric) -> Void) {
            self.track = track
            self.rejectedIDs = rejectedIDs
            self.firstMatch = firstMatch
        }

        func accept(_ candidate: LyricCandidate) async {
            guard !candidate.lines.isEmpty, !Task.isCancelled else { return }
            received.append(candidate)
            if !announcedFirstMatch, !rejectedIDs.contains(candidate.id), let score = candidate.score(for: track), score.canPreview {
                announcedFirstMatch = true
                await firstMatch(RankedLyric(candidate: candidate, score: score))
            }
        }
    }

    @concurrent
    static func ranked(for track: Track, sources: [Source], sourceOrder: [Source], rejecting rejectedIDs: Set<String> = [],
                       firstMatch: @escaping @MainActor @Sendable (RankedLyric) -> Void = { _ in }) async -> [RankedLyric] {
        let sources = sources.filter { $0 != .automatic && $0 != .local }
        let catalog = sources.contains(.netease)
            ? Task { await NeteaseBridge.lyricsRecordings(for: track) } : nil
        let results = Results(track: track, rejectedIDs: rejectedIDs, firstMatch: firstMatch)
        await withTaskGroup(of: Bool.self) { tasks in
            for source in sources {
                tasks.addTask {
                    await fetch(source, for: track, catalog: catalog, report: { await results.accept($0) })
                    return true
                }
            }
            tasks.addTask {
                try? await Task.sleep(for: .seconds(8))
                return false
            }

            for _ in sources {
                guard await tasks.next() == true, !Task.isCancelled else { break }
            }
            tasks.cancelAll()
            catalog?.cancel()
        }
        guard !Task.isCancelled else { return [] }
        return await RankedLyric.rank(results.received, for: track, sourceOrder: sourceOrder, rejecting: rejectedIDs)
    }

    private static func fetch(_ source: Source, for track: Track,
                              catalog: Task<[LyricRecording], Never>?, report: @escaping Report) async {
        switch source {
        case .qqMusic: _ = await OnlineLyricCatalogs.qqMusic(for: track, report: report)
        case .kugou: _ = await OnlineLyricCatalogs.kugou(for: track, report: report)
        case .kuwo: _ = await OnlineLyricCatalogs.kuwo(for: track, report: report)
        case .lrclib:
            for candidate in await OnlineLyricCatalogs.lrclib(for: track) { await report(candidate) }
        case .megalobiz: _ = await OnlineLyricCatalogs.megalobiz(for: track, report: report)
        case .migu: _ = await OnlineLyricCatalogs.migu(for: track, report: report)
        case .soda: _ = await OnlineLyricCatalogs.soda(for: track, report: report)
        case .deezer: _ = await OnlineLyricCatalogs.deezer(for: track, report: report)
        case .appleMusic: await AppleMusicLyrics.shared.candidates(for: track, report: report)
        case .automatic, .local: break
        case .amll:
            // Race the independent catalog and the legacy index so a slow endpoint cannot consume both paths' time.
            await withTaskGroup(of: [LyricCandidate].self) { variants in
                variants.addTask { await OnlineLyricCatalogs.amll(for: track) }
                variants.addTask {
                    let recordings = if let catalog { await catalog.value }
                        else { await NeteaseBridge.lyricsRecordings(for: track) }
                    guard !Task.isCancelled else { return [] }
                    return await OnlineLyricCatalogs.fetch(recordings, from: .amll, report: { _ in }) { recording in
                        await OnlineLyricCatalogs.amll(neteaseID: recording.id, for: track)
                    }
                }
                var otherCandidates: [LyricCandidate] = []
                for await candidates in variants {
                    guard !Task.isCancelled else { return }
                    if candidates.contains(where: { $0.score(for: track)?.hasWordTiming == true }) {
                        variants.cancelAll()
                        for candidate in candidates { await report(candidate) }
                        return
                    }
                    otherCandidates.append(contentsOf: candidates)
                }
                var seen = Set<String>()
                for candidate in otherCandidates where seen.insert(candidate.id).inserted { await report(candidate) }
            }
        case .netease:
            let recordings = await catalog?.value ?? []
            await withTaskGroup(of: Void.self) { variants in
                for recording in recordings {
                    variants.addTask {
                        let lines = await NeteaseBridge.syncLyrics(catalogID: recording.id, for: track)
                        await report(LyricCandidate(source: source, recording: recording, lines: lines))
                    }
                }
            }
        }
    }
}
