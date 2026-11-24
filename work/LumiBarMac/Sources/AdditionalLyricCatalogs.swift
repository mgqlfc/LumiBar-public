import Foundation

extension OnlineLyricCatalogs {
    static func soda(for track: Track, report: @escaping LyricLookup.Report = { _ in }) async -> [LyricCandidate] {
        let webAgent = "Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7) AppleWebKit/537.36 Safari/537.36"
        let hosts = ["api.qishui.com", "beta-luna.douyin.com"]
        let title = LyricRecording.identity(track.title)
        let artistHint = LyricRecording.identity(track.artist)
        guard !title.isEmpty else { return [] }
        let queries = artistHint.isEmpty ? [track.title]
            : ["\(track.artist) \(track.title)", "\(track.title) \(track.artist)", track.title]
        let ids = await withTaskGroup(of: (Int, [String]).self, returning: [String].self) { tasks in
            for (index, query) in queries.enumerated() where !Task.isCancelled {
                tasks.addTask {
                    for host in hosts {
                        guard let data = await read("https://\(host)/luna/sug", [("q", query), ("aid", "386088")],
                                                    userAgent: webAgent),
                              let reply = try? JSONDecoder().decode(SodaSuggestions.self, from: data) else { continue }
                        let hits = (reply.sugs ?? []).compactMap { row -> (id: String, hasArtist: Bool)? in
                            let suggestion = LyricRecording.identity(row.suggestion ?? "")
                            guard row.content_type == "track", suggestion.contains(title),
                                  let id = row.entity?.track?.id, Int64(id).map({ $0 > 0 }) == true else { return nil }
                            return (id, !artistHint.isEmpty && suggestion.contains(artistHint))
                        }
                        // The suggestion text only prioritizes detail requests; the detail still verifies the recording.
                        if !hits.isEmpty { return (index, hits.filter(\.hasArtist).map(\.id)
                            + hits.filter { !$0.hasArtist }.map(\.id)) }
                    }
                    return (index, [])
                }
            }
            var batches = Array(repeating: [String](), count: queries.count)
            for await (index, hits) in tasks { batches[index] = hits }
            var seen = Set<String>()
            return batches.joined().filter { seen.insert($0).inserted }
        }
        return await withTaskGroup(of: LyricCandidate?.self, returning: [LyricCandidate].self) { tasks in
            for id in ids.prefix(5) {
                tasks.addTask {
                    for host in hosts {
                        guard let data = await read("https://\(host)/luna/h5/seo_track",
                                                    [("track_id", id), ("device_platform", "web")], userAgent: webAgent),
                              let reply = try? JSONDecoder().decode(SodaTrack.self, from: data),
                              let song = reply.seo_track?.track, song.id == id, let name = song.name else { continue }
                        // An OT suffix names the original song, not another arrangement of this recording.
                        let baseName = name.replacingOccurrences(of: #"\s*[（(]\s*OT\s*[:：]\s*[^()（）]+[）)]$"#,
                                                                 with: "", options: [.regularExpression, .caseInsensitive])
                        let recording = LyricRecording(id: id, title: LyricRecording.identity(baseName) == title ? baseName : name,
                            artist: (song.artists ?? []).compactMap(\.name).joined(separator: " "),
                            album: song.album?.name ?? "", duration: Double(song.duration ?? 0) / 1000,
                            allowsAlbumArtist: song.artists?.count == 1)
                        guard recording.match(to: track) != nil,
                              let lyric = reply.lyric ?? reply.seo_track?.lyric, let text = lyric.content else { return nil }
                        var lines = parseKRC(text)
                        if lines.isEmpty { lines = Lyric.parse(text) }
                        if let translation = lyric.translations?["cn"] {
                            Lyric.addTranslations(Lyric.parse(translation), to: &lines)
                        }
                        guard Lyric.hasSyncedVocals(in: lines, for: track) else { return nil }
                        return LyricCandidate(source: .soda, recording: recording, lines: lines)
                    }
                    return nil
                }
            }
            var results: [LyricCandidate] = []
            for await candidate in tasks {
                guard let candidate, !Task.isCancelled else { continue }
                await report(candidate)
                results.append(candidate)
            }
            return results
        }
    }

    static func deezer(for track: Track, report: @escaping LyricLookup.Report = { _ in }) async -> [LyricCandidate] {
        let query = "\(track.title) \(track.artist)"
        async let authData = read("https://auth.deezer.com/login/anonymous", [("jo", "p"), ("rto", "c"), ("i", "c")])
        guard let data = await read("https://api.deezer.com/search", [("q", query), ("limit", "20")]),
              let search = try? JSONDecoder().decode(DeezerSearch.self, from: data) else { return [] }
        let recordings = (search.data ?? []).compactMap { song -> LyricRecording? in
            guard let id = song.id, id > 0, let title = song.title else { return nil }
            return LyricRecording(id: String(id), title: title, artist: song.artist?.name ?? "",
                                  album: song.album?.title ?? "", duration: song.duration ?? 0,
                                  allowsAlbumArtist: true)
        }
        let matches = matching(recordings, for: track)
        guard !matches.isEmpty, let auth = await authData,
              let session = try? JSONDecoder().decode(DeezerSession.self, from: auth),
              let token = session.jwt, !token.isEmpty, token.utf8.count < 8192 else { return [] }
        return await fetch(matches, from: .deezer, report: report) { recording in
            await deezerLyrics(id: recording.id, token: token)
        }
    }

    private static func deezerLyrics(id: String, token: String) async -> [Lyric] {
        let query = """
        query LumiBarLyrics($id: String!) {
          track(trackId: $id) {
            lyrics {
              synchronizedLines { milliseconds line lineTranslated }
              synchronizedWordByWordLines { start words { start end word } }
            }
          }
        }
        """
        let request: [String: Any] = ["operationName": "LumiBarLyrics", "query": query, "variables": ["id": id]]
        guard let body = try? JSONSerialization.data(withJSONObject: request),
              let data = await read("https://pipe.deezer.com/api", json: body,
                                    headers: ["Authorization": "Bearer " + token,
                                              "Accept-Language": Locale.preferredLanguages.first ?? "zh-CN"]),
              let reply = try? JSONDecoder().decode(DeezerLyrics.self, from: data),
              let payload = reply.data?.track?.lyrics else { return [] }
        return deezerTiming(payload)
    }

    private static func deezerTiming(_ payload: DeezerLyrics.Payload) -> [Lyric] {
        var lines = (payload.synchronizedLines ?? []).compactMap { segment -> Lyric? in
            guard let time = segment.milliseconds, (0...86_400_000).contains(time),
                  let text = segment.line?.trimmingCharacters(in: .whitespacesAndNewlines), !text.isEmpty else { return nil }
            let translation = segment.lineTranslated?.trimmingCharacters(in: .whitespacesAndNewlines)
            return Lyric(time: Double(time) / 1000, text: text,
                         translation: translation?.isEmpty == false && translation != text ? translation : nil)
        }.sorted { $0.time < $1.time }
        let identities = lines.map { LyricRecording.identity($0.text) }
        let timed = payload.synchronizedWordByWordLines ?? []
        var claimed = Set<Int>()
        for segment in timed {
            guard let start = segment.start, (0...86_400_000).contains(start), let tokens = segment.words, !tokens.isEmpty else { continue }
            let identity = LyricRecording.identity(tokens.compactMap(\.word).joined())
            let time = Double(start) / 1000
            guard !identity.isEmpty, let match = lines.indices.filter({
                abs(lines[$0].time - time) <= 1.2 && !claimed.contains($0) && identities[$0] == identity
            }).min(by: { abs(lines[$0].time - time) < abs(lines[$1].time - time) }),
                let timing = deezerWords(tokens, using: lines[match].text) else { continue }
            claimed.insert(match)
            // Keep the line clock from the verified line track. Word timings use the source's absolute clock.
            lines[match].words = timing
        }
        return lines
    }

    private static func deezerWords(_ tokens: [DeezerLyrics.Payload.Word], using text: String) -> [Lyric.Word]? {
        var cursor = text.startIndex
        var words: [Lyric.Word] = []
        for token in tokens {
            guard let start = token.start, let end = token.end, (0...86_400_000).contains(start),
                  end >= start, end <= 86_400_000, let word = token.word, !word.isEmpty,
                  let range = text.range(of: word, options: [.caseInsensitive, .diacriticInsensitive],
                                         range: cursor..<text.endIndex) else { return nil }
            words.append(Lyric.Word(time: Double(start) / 1000, duration: Double(end - start) / 1000,
                                    text: String(text[cursor..<range.upperBound])))
            cursor = range.upperBound
        }
        if cursor < text.endIndex, let last = words.popLast() {
            words.append(Lyric.Word(time: last.time, duration: last.duration, text: last.text + text[cursor...]))
        }
        return words
    }

    private struct SodaSuggestions: Decodable {
        struct Suggestion: Decodable {
            struct Entity: Decodable {
                struct Track: Decodable { let id: String? }
                let track: Track?
            }
            let content_type: String?
            let suggestion: String?
            let entity: Entity?
        }
        let sugs: [Suggestion]?
    }

    private struct SodaTrack: Decodable {
        struct Lyric: Decodable { let content: String?; let translations: [String: String]? }
        struct SEO: Decodable {
            struct Song: Decodable {
                struct Credit: Decodable { let name: String? }
                let id, name: String?
                let duration: Int?
                let artists: [Credit]?
                let album: Credit?
            }
            let track: Song?
            let lyric: Lyric?
        }
        let seo_track: SEO?
        let lyric: Lyric?
    }

    private struct DeezerSearch: Decodable {
        struct Song: Decodable {
            struct Artist: Decodable { let name: String? }
            struct Album: Decodable { let title: String? }
            let id: Int64?
            let title: String?
            let duration: Double?
            let artist: Artist?
            let album: Album?
        }
        let data: [Song]?
    }

    private struct DeezerSession: Decodable { let jwt: String? }

    private struct DeezerLyrics: Decodable {
        struct Payload: Decodable {
            struct Line: Decodable {
                let milliseconds: Int?
                let line, lineTranslated: String?
            }
            struct Word: Decodable {
                let start, end: Int?
                let word: String?
            }
            struct WordLine: Decodable {
                let start: Int?
                let words: [Word]?
            }
            let synchronizedLines: [Line]?
            let synchronizedWordByWordLines: [WordLine]?
        }
        struct Response: Decodable {
            struct Track: Decodable { let lyrics: Payload? }
            let track: Track?
        }
        let data: Response?
    }
}
