import CoreFoundation
import Foundation
import QQMusicDES
import zlib

enum OnlineLyricCatalogs {
    static func qqMusic(for track: Track, report: @escaping LyricLookup.Report = { _ in }) async -> [LyricCandidate] {
        let request: [String: Any] = [
            "comm": ["ct": 19, "cv": 1859, "uin": "0"],
            "req": [
                "module": "music.search.SearchCgiService",
                "method": "DoSearchForQQMusicDesktop",
                "param": ["query": "\(track.title) \(track.artist)", "page_num": 1, "num_per_page": 15, "search_type": 0],
            ],
        ]
        let keyword = "\(track.title) \(track.artist)"
        var searches = [("https://c.y.qq.com/soso/fcgi-bin/client_search_cp",
                         [("format", "json"), ("w", keyword), ("n", "15"), ("p", "1"), ("new_json", "1")])]
        if let json = try? JSONSerialization.data(withJSONObject: request),
           let payload = String(data: json, encoding: .utf8) {
            searches.insert(("https://u.y.qq.com/cgi-bin/musicu.fcg", [("format", "json"), ("data", payload)]), at: 0)
        }
        var checked = Set<String>()
        for (address, query) in searches {
            guard let data = await read(address, query, referer: "https://y.qq.com/"),
                  let search = decodeQQ(QQSearch.self, from: data) else { continue }
            let songs = search.songs.compactMap { item -> LyricRecording? in
                guard let mid = item.mid, !mid.isEmpty else { return nil }
                return LyricRecording(id: mid, title: item.name ?? item.title ?? "",
                            artist: item.singer?.compactMap(\.name).joined(separator: " ") ?? "",
                            album: item.album?.name ?? "", duration: item.interval ?? 0,
                            allowsAlbumArtist: item.singer?.count == 1)
            }
            let ids = Dictionary(search.songs.compactMap { item in
                guard let mid = item.mid, let id = item.id, id > 0 else { return nil as (String, Int)? }
                return (mid, id)
            }, uniquingKeysWith: { first, _ in first })
            let matches = matching(songs, for: track).filter { checked.insert($0.id).inserted }
            let results = await fetch(matches, from: .qqMusic, report: report) { song in
                await qqLyrics(for: song, songID: ids[song.id], track: track)
            }
            if !results.isEmpty { return results }
        }
        return []
    }

    private static func qqLyrics(for song: LyricRecording, songID: Int?, track: Track) async -> [Lyric] {
        var translation: String?
        if let songID, !Task.isCancelled {
            let params: [String: Any] = [
                "songID": songID, "songMID": song.id, "crypt": 0, "qrc": 1, "qrc_t": 0,
                "lrc_t": 0, "trans": 1, "trans_t": 0, "roma": 0, "roma_t": 0, "type": 0,
            ]
            let payload: [String: Any] = [
                "comm": ["ct": 19, "cv": 2111, "uin": "0"],
                "req": ["method": "GetPlayLyricInfo", "module": "music.musichallSong.PlayLyricInfo", "param": params],
            ]
            if let body = try? JSONSerialization.data(withJSONObject: payload),
               let data = await read("https://u.y.qq.com/cgi-bin/musicu.fcg", referer: "https://y.qq.com/", json: body),
               let result = decodeQQ(QQTimedLyrics.self, from: data)?.req, result.code == 0, let reply = result.data {
                translation = decodedLRC(reply.trans)
                let original = decodedLRC(reply.lyric)
                let qrc = reply.lyric.flatMap(decodeQRC).flatMap(qrcWordTiming)
                let lines = Lyric.parse(original, translation: translation, wordTiming: qrc)
                if Lyric.hasSyncedVocals(in: lines, for: track) { return lines }
            }
        }
        let query = [("format", "json"), ("nobase64", "0"), ("songmid", song.id), ("g_tk", "5381")]
        for host in ["c.y.qq.com", "shc.y.qq.com"] {
            guard let data = await read("https://\(host)/lyric/fcgi-bin/fcg_query_lyric_new.fcg", query,
                                        referer: "https://y.qq.com/"),
                  let reply = decodeQQ(QQLyrics.self, from: data), reply.code == nil || reply.code == 0 else { continue }
            let lines = Lyric.parse(decodedLRC(reply.lyric), translation: translation ?? decodedLRC(reply.trans))
            if Lyric.hasSyncedVocals(in: lines, for: track) { return lines }
        }
        return []
    }

    private static func decodeQRC(_ hex: String) -> String? {
        let scalars = hex.utf8
        guard !scalars.isEmpty, scalars.count <= 1_048_576, scalars.count.isMultiple(of: 16) else { return nil }
        var encrypted = [UInt8]()
        encrypted.reserveCapacity(scalars.count / 2)
        var cursor = scalars.startIndex
        while cursor < scalars.endIndex {
            let next = scalars.index(after: cursor)
            guard let byte = UInt8(String(decoding: scalars[cursor...next], as: UTF8.self), radix: 16) else { return nil }
            encrypted.append(byte)
            cursor = scalars.index(after: next)
        }
        encrypted.withUnsafeMutableBufferPointer { qq_qrc_decrypt($0.baseAddress, $0.count) }
        let text = inflateLyrics(encrypted)
        // QQ leaves literal quotes in the attribute value, so use the enclosing track tag.
        guard let text, let start = text.range(of: #"LyricContent=""#),
              let end = text[start.upperBound...].firstMatch(of: /"\s*\/>/) else { return text }
        return String(text[start.upperBound..<end.range.lowerBound])
            .replacingOccurrences(of: "&quot;", with: "\"")
            .replacingOccurrences(of: "&apos;", with: "'")
            .replacingOccurrences(of: "&lt;", with: "<")
            .replacingOccurrences(of: "&gt;", with: ">")
            .replacingOccurrences(of: "&amp;", with: "&")
    }

    private static func qrcWordTiming(_ qrc: String) -> String? {
        let lineTag = /^\[(\d{1,9}),(\d{1,9})\]/
        let wordTag = /\((\d{1,9}),(\d{1,9})\)/
        let lines = qrc.split(whereSeparator: \.isNewline).compactMap { line -> String? in
            guard let header = line.firstMatch(of: lineTag) else { return nil }
            let text = line[header.range.upperBound...]
            var start = text.startIndex
            var payload = ""
            for tag in text.matches(of: wordTag) {
                payload += "(\(tag.1),\(tag.2),0)" + text[start..<tag.range.lowerBound]
                start = tag.range.upperBound
            }
            guard !payload.isEmpty else { return nil }
            return String(line[header.range]) + payload + text[start...]
        }
        return lines.isEmpty ? nil : lines.joined(separator: "\n")
    }

    private static func decodeQQ<Value: Decodable>(_ type: Value.Type, from data: Data) -> Value? {
        // The same web endpoints can wrap their JSON response in a callback.
        guard let first = data.firstIndex(of: 0x7b), let last = data.lastIndex(of: 0x7d), first < last else { return nil }
        return try? JSONDecoder().decode(type, from: data.subdata(in: first..<(last + 1)))
    }

    static func kugou(for track: Track, report: @escaping LyricLookup.Report = { _ in }) async -> [LyricCandidate] {
        let milliseconds = track.duration.isFinite && (0...86_400).contains(track.duration)
            ? Int((track.duration * 1000).rounded()) : 0
        guard let data = await read("https://lyrics.kugou.com/search",
                                    [("ver", "1"), ("man", "yes"), ("client", "pc"),
                                     ("keyword", "\(track.artist) - \(track.title)"), ("duration", String(milliseconds))]),
              let reply = try? JSONDecoder().decode(KugouSearch.self, from: data) else { return [] }
        let songs = (reply.candidates ?? []).map {
            LyricRecording(id: $0.id, title: $0.song ?? "", artist: $0.singer ?? "", album: "",
                 duration: ($0.duration ?? 0) / 1000, accessKey: $0.accesskey)
        }
        return await fetch(matching(songs, for: track), from: .kugou, report: report) { song in
            guard let key = song.accessKey, !key.isEmpty else { return [] }
            let query = [("ver", "1"), ("client", "pc"), ("id", song.id), ("accesskey", key), ("charset", "utf8")]
            if let text = await kugouDownload(query, format: "krc").flatMap(decodeKRC) {
                let lines = parseKRC(text)
                if Lyric.hasSyncedVocals(in: lines, for: track) { return lines }
            }
            if let text = await kugouDownload(query, format: "lrc").flatMap({ String(data: $0, encoding: .utf8) }) {
                return Lyric.parse(text)
            }
            return []
        }
    }

    static func kuwo(for track: Track, report: @escaping LyricLookup.Report = { _ in }) async -> [LyricCandidate] {
        guard let data = await read("https://search.kuwo.cn/r.s",
                                    [("all", "\(track.title) \(track.artist)"), ("ft", "music"),
                                     ("itemset", "web_2013"), ("client", "kt"), ("pn", "0"), ("rn", "15"),
                                     ("rformat", "json"), ("encoding", "utf8"), ("pcjson", "1"), ("vipver", "1")],
                                    referer: "https://www.kuwo.cn/"),
              let search = try? JSONDecoder().decode(KuwoSearch.self, from: data) else { return [] }
        let songs = (search.abslist ?? []).compactMap { item -> LyricRecording? in
            guard let id = item.MUSICRID?.split(separator: "_").last, Int64(id).map({ $0 > 0 }) == true else { return nil }
            return LyricRecording(id: String(id), title: item.SONGNAME ?? item.NAME ?? "", artist: item.ARTIST ?? "",
                        album: item.ALBUM ?? "", duration: seconds(item.DURATION))
        }
        return await fetch(matching(songs, for: track), from: .kuwo, report: report) { song in
            let query = [("musicId", song.id)]
            for base in ["https://www.kuwo.cn", "https://kuwo.cn"] {
                guard let data = await read(base + "/openapi/v1/www/lyric/getlyric", query, referer: base + "/"),
                      let response = try? JSONDecoder().decode(KuwoLyrics.self, from: data),
                      response.code == 200 else { continue }
                return parseKuwo(response.data?.lrclist ?? [])
            }
            return []
        }
    }

    private static func parseKuwo(_ entries: [KuwoLyrics.Payload.Line]) -> [Lyric] {
        let timed = entries.compactMap { entry -> (time: Int, text: String)? in
            guard let time = Double(entry.time), time.isFinite, (0...86_400).contains(time) else { return nil }
            let text = entry.lineLyric.trimmingCharacters(in: .whitespacesAndNewlines)
            guard !text.isEmpty else { return nil }
            return (Int((time * 1000).rounded()), text)
        }
        let grouped = Dictionary(grouping: timed, by: \.time)
        let times = grouped.keys.sorted()
        func isTranslationCredit(_ entry: (time: Int, text: String)) -> Bool {
            entry.text.contains("歌词翻译由") && entry.text.hasSuffix("提供")
        }
        if let start = times.firstIndex(where: { time in
            guard let group = grouped[time], group.count == 2 else { return false }
            return isTranslationCredit(group[0]) && !group[1].text.contains(/\p{Han}/)
        }) {
            // On this feed the first slot of each timestamp is the previous line's translation.
            // A credit in the first slot and a translation-only tail identify that layout.
            let pairs = times[start...].compactMap { grouped[$0] }
            guard pairs.count >= 4, let tail = pairs.last, tail.count == 1,
                  tail[0].text.contains(/\p{Han}/),
                  pairs.dropLast().allSatisfy({ $0.count == 2 && $0[0].text.contains(/\p{Han}/) && !$0[1].text.contains(/\p{Han}/) })
            else { return [] }
            return pairs.dropLast().indices.map { index in
                let original = pairs[index][1]
                return Lyric(time: Double(original.time) / 1000, text: original.text,
                             translation: pairs[index + 1][0].text)
            }
        }
        let lrc = timed.filter { !isTranslationCredit($0) }.map { time, text in
            String(format: "[%02d:%02d.%03d]", time / 60_000, time / 1000 % 60, time % 1000) + text
        }.joined(separator: "\n")
        return Lyric.parse(lrc)
    }

    private struct LRCLIBSong: Decodable {
        let id: Int
        let trackName, artistName, albumName: String
        let duration: Double?
        let instrumental: Bool?
        let syncedLyrics: String?
    }

    static func lrclib(for track: Track) async -> [LyricCandidate] {
        let query = [("track_name", track.title), ("artist_name", track.artist)]
        guard let data = await read("https://lrclib.net/api/search", query, userAgent: lyricClientUserAgent),
              let songs = try? JSONDecoder().decode([LRCLIBSong].self, from: data) else { return [] }
        return songs.prefix(20).compactMap { song in
            guard song.instrumental != true, let lyrics = song.syncedLyrics else { return nil }
            let recording = LyricRecording(id: String(song.id), title: song.trackName, artist: song.artistName,
                                           album: song.albumName, duration: song.duration ?? 0)
            return LyricCandidate(source: .lrclib, recording: recording, lines: Lyric.parse(lyrics))
        }
    }

    private struct MegalobizSearch: Decodable {
        struct Song: Decodable {
            let id: Int
            let title, artist: String
            let duration: Double?
        }
        let results: [Song]
    }

    static func megalobiz(for track: Track, report: @escaping LyricLookup.Report = { _ in }) async -> [LyricCandidate] {
        let base = "https://api.megalobiz.com/api/public/v1"
        let agent = lyricClientUserAgent
        var query = [("title", track.title), ("artist", track.artist), ("limit", "10")]
        if track.duration.isFinite, (1...86_400).contains(track.duration) {
            query.append(("duration", String(Int(track.duration.rounded()))))
        }
        guard let data = await read(base + "/search", query, userAgent: agent),
              let songs = try? JSONDecoder().decode(MegalobizSearch.self, from: data) else { return [] }
        let recordings = songs.results.map {
            LyricRecording(id: String($0.id), title: $0.title, artist: $0.artist,
                           album: "", duration: $0.duration ?? 0)
        }
        return await fetch(matching(recordings, for: track), from: .megalobiz, report: report) { song in
            guard let data = await read(base + "/lrc/" + song.id, [("format", "lrc")], userAgent: agent),
                  let lrc = String(data: data, encoding: .utf8) else { return [] }
            let lines = Lyric.parse(lrc)
            // Reject broken uploads where most of the lyric text is pinned to one timestamp.
            if lines.count <= 3, lines.contains(where: { $0.text.split(separator: "\n").count >= 8 }) { return [] }
            return lines
        }
    }

    private struct MiguSearch: Decodable {
        struct Songs: Decodable {
            let result: [Song]?
        }
        struct Song: Decodable {
            struct Named: Decodable { let name: String? }
            struct AudioFormat: Decodable { let format, fileType, size: String? }
            let name, copyrightId, lyricUrl, trcUrl: String?
            let singers, albums: [Named]?
            let rateFormats, newRateFormats: [AudioFormat]?

            var duration: Double {
                // The reported audio size gives the duration for the fixed-bitrate MP3 variants.
                let formats = (newRateFormats ?? []) + (rateFormats ?? [])
                let bitrates: [(String, Double)] = [("020007", 128_000), ("020010", 320_000), ("000019", 64_000)]
                for (code, bitsPerSecond) in bitrates {
                    guard let match = formats.first(where: { $0.format == code && $0.fileType?.lowercased() == "mp3" }),
                          let size = match.size.flatMap(Double.init), size.isFinite, size > 0 else { continue }
                    return size * 8 / bitsPerSecond
                }
                return 0
            }
        }
        let code: String?
        let songResultData: Songs?
    }

    static func migu(for track: Track, report: @escaping LyricLookup.Report = { _ in }) async -> [LyricCandidate] {
        let referer = "https://m.music.migu.cn/"
        let endpoint = "https://pd.musicapp.migu.cn/MIGUM2.0/v1.0/content/search_all.do"
        let query = [("text", "\(track.artist) \(track.title)"), ("pageNo", "1"), ("pageSize", "10"),
                     ("searchSwitch", #"{"song":1}"#), ("isCorrect", "1")]
        guard let data = await read(endpoint, query, referer: referer),
              let search = try? JSONDecoder().decode(MiguSearch.self, from: data), search.code == "000000",
              let songs = search.songResultData?.result else { return [] }
        let withLyrics = songs.compactMap { song -> (LyricRecording, MiguSearch.Song)? in
            guard let id = song.copyrightId, !id.isEmpty, let title = song.name, miguAsset(song.lyricUrl) != nil else { return nil }
            let artist = song.singers?.compactMap(\.name).joined(separator: " ") ?? ""
            let recording = LyricRecording(id: id, title: title, artist: artist, album: song.albums?.first?.name ?? "",
                                           duration: song.duration)
            return (recording, song)
        }
        let byID = Dictionary(withLyrics.map { ($0.0.id, $0.1) }, uniquingKeysWith: { first, _ in first })
        return await fetch(matching(withLyrics.map(\.0), for: track), from: .migu, report: report) { recording in
            guard let song = byID[recording.id], let lyricURL = miguAsset(song.lyricUrl),
                  let data = await read(lyricURL, referer: referer), let original = miguText(data) else { return [] }
            let translation: String?
            if let url = miguAsset(song.trcUrl) { translation = await read(url, referer: referer).flatMap(miguText) }
            else { translation = nil }
            return Lyric.parse(original, translation: translation)
        }
    }

    private static func miguAsset(_ address: String?) -> String? {
        guard let address, let url = URLComponents(string: address), url.scheme == "https",
              let host = url.host?.lowercased(), host.hasSuffix(".migu.cn"),
              url.user == nil, url.password == nil else { return nil }
        return url.string
    }

    private static func miguText(_ data: Data) -> String? {
        if let utf8 = String(data: data, encoding: .utf8) { return utf8 }
        let gb18030 = CFStringConvertEncodingToNSStringEncoding(CFStringEncoding(CFStringEncodings.GB_18030_2000.rawValue))
        return String(data: data, encoding: .init(rawValue: gb18030))
    }

    static func fetch(
        _ recordings: [LyricRecording], from source: MusicModel.LyricSource,
        report: LyricLookup.Report,
        using load: @escaping @Sendable (LyricRecording) async -> [Lyric]
    ) async -> [LyricCandidate] {
        await withTaskGroup(of: LyricCandidate.self, returning: [LyricCandidate].self) { tasks in
            for recording in recordings.prefix(3) {
                tasks.addTask {
                    let lines = await load(recording)
                    return LyricCandidate(source: source, recording: recording, lines: lines)
                }
            }
            var results: [LyricCandidate] = []
            for await candidate in tasks where !candidate.lines.isEmpty && !Task.isCancelled {
                await report(candidate)
                results.append(candidate)
            }
            return results
        }
    }

    private struct AMLLItem: Decodable {
        let id: Int64
        let musicNames: [String]
        let artistNames: [String]
        let albumNames: [String]
        let ncmMusicIds: [String]
        let lyrics: String?
    }

    private struct AMLLSearch: Decodable { let items: [AMLLItem] }
    private struct AMLLResponse<Value: Decodable>: Decodable { let data: Value }

    private static func amllResponse<Value: Decodable>(_ path: String, _ query: [(String, String)],
                                                        as type: Value.Type) async -> Value? {
        guard let data = await read("https://api.amll.dev/v1/lyrics/" + path, query) else { return nil }
        return (try? JSONDecoder().decode(AMLLResponse<Value>.self, from: data))?.data
    }

    static func amll(for track: Track) async -> [LyricCandidate] {
        if track.player == .netease, let id = track.catalogID, Int64(id).map({ $0 > 0 }) == true,
           let item = await amllResponse("get", [("ncmMusicId", id)], as: AMLLItem.self),
           item.ncmMusicIds.contains(id), let source = item.lyrics {
            let recording = LyricRecording(id: id, title: track.title, artist: track.artist,
                                           album: track.album, duration: track.duration, confirmedID: true)
            let candidate = LyricCandidate(source: .amll, recording: recording, lines: AMLLTiming.parse(Data(source.utf8)))
            if candidate.score(for: track) != nil {
                return [candidate]
            }
        }

        let titleKey = LyricRecording.identity(track.title)
        // Some official titles append a movie/theme credit. Search the shorter phrase, then verify a full catalog alias.
        let searchTitle = track.title.replacingOccurrences(of: #"[\s　]+(?:[（(].*[）)]|[-–—]\s+.+)$"#,
                                                           with: "", options: .regularExpression)
        let credits = track.artist.components(separatedBy: CharacterSet(charactersIn: "/、;；"))
            .map { $0.trimmingCharacters(in: .whitespacesAndNewlines) }.filter { !$0.isEmpty }
        guard !titleKey.isEmpty, let artist = credits.first,
              let search = await amllResponse("search", [("musicName", searchTitle.isEmpty ? track.title : searchTitle),
                                                          ("artistName", artist), ("pageSize", "30")],
                                              as: AMLLSearch.self) else { return [] }
        let albumKey = LyricRecording.identity(track.album)
        let artistKey = LyricRecording.identity(track.artist)
        let creditKeys = Set(credits.map(LyricRecording.identity))
        let recordings = search.items.compactMap { item -> LyricRecording? in
            guard let title = item.musicNames.first(where: { LyricRecording.identity($0) == titleKey }) else { return nil }
            let album = item.albumNames.first(where: { LyricRecording.identity($0) == albumKey }) ?? item.albumNames.first ?? ""
            // The catalog lists translated aliases alongside performer credits.
            let advertised = Set(item.artistNames.map(LyricRecording.identity))
            let artist = advertised.contains(artistKey) || (creditKeys.count > 1 && creditKeys.isSubset(of: advertised))
                ? track.artist : item.artistNames.joined(separator: " ")
            // AMLL metadata has no playback duration. Leave it unknown and require the normal recording match.
            return LyricRecording(id: String(item.id), title: title, artist: artist, album: album, duration: 0)
        }
        return await fetch(matching(recordings, for: track), from: .amll, report: { _ in }) { recording in
            guard let item = await amllResponse("get", [("id", recording.id)], as: AMLLItem.self),
                  String(item.id) == recording.id, let source = item.lyrics else { return [] }
            return AMLLTiming.parse(Data(source.utf8))
        }
    }

    // The original TTML can also carry a translation that converted files omit.
    static func amll(neteaseID: String?, for track: Track) async -> [Lyric] {
        guard let id = neteaseID, Int64(id).map({ $0 > 0 }) == true else { return [] }
        let base = "https://raw.githubusercontent.com/amll-dev/amll-ttml-db/main/ncm-lyrics/\(id)"
        if let data = await read(base + ".ttml") {
            let lines = AMLLTiming.parse(data)
            if Lyric.hasSyncedVocals(in: lines, for: track) { return lines }
        }
        if let data = await read(base + ".yrc"), let text = String(data: data, encoding: .utf8) {
            let lines = Lyric.parse(nil, translation: nil, wordTiming: text)
            if Lyric.hasSyncedVocals(in: lines, for: track) { return lines }
        }
        guard let data = await read(base + ".lrc"), let text = String(data: data, encoding: .utf8) else { return [] }
        return Lyric.parse(text)
    }

    private static var lyricClientUserAgent: String {
        let version = Bundle.main.object(forInfoDictionaryKey: "CFBundleShortVersionString") as? String ?? "0.1"
        return "LumiBar/\(version) (+https://github.com/mgqlfc/LumiBar)"
    }

    static func read(_ address: String, _ query: [(String, String)] = [], referer: String? = nil,
                     userAgent: String = "Mozilla/5.0", json: Data? = nil, headers: [String: String] = [:]) async -> Data? {
        guard !Task.isCancelled, var components = URLComponents(string: address) else { return nil }
        if !query.isEmpty {
            components.queryItems = query.map { URLQueryItem(name: $0.0, value: $0.1) }
            components.percentEncodedQuery = components.percentEncodedQuery?.replacingOccurrences(of: "+", with: "%2B")
        }
        guard let url = components.url else { return nil }
        var request = URLRequest(url: url, timeoutInterval: 4)
        request.setValue(userAgent, forHTTPHeaderField: "User-Agent")
        if let referer { request.setValue(referer, forHTTPHeaderField: "Referer") }
        if let json {
            request.httpMethod = "POST"
            request.httpBody = json
            request.setValue("application/json", forHTTPHeaderField: "Content-Type")
        }
        for (field, value) in headers { request.setValue(value, forHTTPHeaderField: field) }
        guard let (data, response) = try? await URLSession.shared.data(for: request), !Task.isCancelled,
              (response as? HTTPURLResponse)?.statusCode == 200, data.count <= 1_048_576 else { return nil }
        return data
    }

    static func matching(_ recordings: [LyricRecording], for track: Track) -> [LyricRecording] {
        var seen = Set<String>()
        return recordings.compactMap { recording -> (LyricRecording, Int)? in
            guard let score = recording.match(to: track), seen.insert(recording.id).inserted else { return nil }
            return (recording, score.points)
        }.sorted {
            $0.1 == $1.1 ? $0.0.id < $1.0.id : $0.1 > $1.1
        }.map { $0.0 }
    }

    private static func seconds(_ raw: String?) -> Double {
        guard let raw else { return 0 }
        if let duration = Double(raw) { return duration }
        let fields = raw.split(separator: ":")
        guard fields.count == 2, let minutes = Double(fields[0]), let seconds = Double(fields[1]),
              minutes >= 0, (0..<60).contains(seconds) else { return 0 }
        return minutes * 60 + seconds
    }

    private static func decodedLRC(_ raw: String?) -> String? {
        guard let raw, !raw.isEmpty else { return nil }
        if raw.contains("[") { return raw }
        return Data(base64Encoded: raw).flatMap { String(data: $0, encoding: .utf8) }
    }

    private static func kugouDownload(_ query: [(String, String)], format: String) async -> Data? {
        guard let data = await read("https://lyrics.kugou.com/download", query + [("fmt", format)]),
              let reply = try? JSONDecoder().decode(KugouPayload.self, from: data),
              let content = reply.content else { return nil }
        return Data(base64Encoded: content)
    }

    private static func decodeKRC(_ wrapped: Data) -> String? {
        guard wrapped.count > 4, wrapped.prefix(4).elementsEqual("krc1".utf8) else { return nil }
        // KRC files consist of this public format's byte mask and a bounded zlib payload.
        let mask: [UInt8] = [0x40, 0x47, 0x61, 0x77, 0x5e, 0x32, 0x74, 0x47, 0x51, 0x36, 0x31, 0x2d, 0xce, 0xd2, 0x6e, 0x69]
        let compressed = wrapped.dropFirst(4).enumerated().map { index, value in value ^ mask[index % mask.count] }
        return inflateLyrics(compressed)
    }

    private static func inflateLyrics(_ compressed: [UInt8]) -> String? {
        var decoded = [UInt8](repeating: 0, count: 1_048_576)
        var length = uLongf(decoded.count)
        let result = decoded.withUnsafeMutableBufferPointer { output in
            compressed.withUnsafeBufferPointer { input in
                uncompress(output.baseAddress, &length, input.baseAddress, uLong(input.count))
            }
        }
        guard result == Z_OK else { return nil }
        return String(bytes: decoded.prefix(Int(length)), encoding: .utf8)
    }

    static func parseKRC(_ text: String) -> [Lyric] {
        let lineTag = /^\[(\d{1,9}),(\d{1,9})\]/
        let wordTag = /<(\d{1,9}),(\d{1,9}),\d+>/
        var lines: [Lyric] = []
        var translation: [[String]] = []
        for line in text.split(whereSeparator: \.isNewline) {
            if line.hasPrefix("[language:"), line.last == "]",
               let bytes = Data(base64Encoded: String(line.dropFirst(10).dropLast())),
               let language = try? JSONDecoder().decode(KugouLanguages.self, from: bytes) {
                translation = language.content.first { $0.type == 1 }?.lyricContent ?? []
                continue
            }
            guard let timeTag = line.firstMatch(of: lineTag), let start = Double(timeTag.1) else { continue }
            let body = line[timeTag.range.upperBound...]
            let tags = body.matches(of: wordTag)
            let words = tags.indices.compactMap { index -> Lyric.Word? in
                let tag = tags[index]
                let end = tags.indices.contains(index + 1) ? tags[index + 1].range.lowerBound : body.endIndex
                let word = String(body[tag.range.upperBound..<end])
                guard let offset = Double(tag.1), let duration = Double(tag.2), !word.isEmpty else { return nil }
                return .init(time: (start + offset) / 1000, duration: duration / 1000, text: word)
            }
            let lyric = words.map(\.text).joined()
            if !lyric.trimmingCharacters(in: .whitespaces).isEmpty {
                lines.append(Lyric(time: start / 1000, text: lyric, words: words))
            }
        }
        if translation.count == lines.count {
            for index in lines.indices {
                let translated = translation[index].joined().trimmingCharacters(in: .whitespacesAndNewlines)
                if !translated.isEmpty, translated != lines[index].text { lines[index].translation = translated }
            }
        }
        var seen = Set<Double>()
        return lines.sorted { $0.time < $1.time }.filter { seen.insert($0.time).inserted }
    }

    private struct QQSearch: Decodable {
        struct Result: Decodable {
            struct Songs: Decodable { let list: [QQSong]? }
            let song: Songs?
        }
        struct Request: Decodable {
            struct Payload: Decodable { let body: Result? }
            let data: Payload?
        }
        let req: Request?
        let data: Result?
        var songs: [QQSong] { req?.data?.body?.song?.list ?? data?.song?.list ?? [] }
    }

    private struct QQSong: Decodable {
        struct Named: Decodable { let name: String? }
        let mid, name, title: String?
        let id: Int?
        let interval: Double?
        let singer: [Named]?
        let album: Named?
    }

    private struct QQLyrics: Decodable {
        let code: Int?
        let lyric, trans: String?
    }

    private struct QQTimedLyrics: Decodable {
        struct Request: Decodable { let code: Int; let data: QQLyrics? }
        let req: Request?
    }

    private struct KugouSearch: Decodable {
        struct Candidate: Decodable {
            let id: String
            let accesskey, song, singer: String?
            let duration: Double?
        }
        let candidates: [Candidate]?
    }

    private struct KugouPayload: Decodable { let content: String? }

    private struct KugouLanguages: Decodable {
        struct Language: Decodable { let type: Int; let lyricContent: [[String]] }
        let content: [Language]
    }

    private struct KuwoSearch: Decodable {
        struct Item: Decodable {
            let MUSICRID, SONGNAME, NAME, ARTIST, ALBUM, DURATION: String?
        }
        let abslist: [Item]?
    }

    private struct KuwoLyrics: Decodable {
        struct Payload: Decodable {
            struct Line: Decodable { let time, lineLyric: String }
            let lrclist: [Line]?
        }
        let code: Int
        let data: Payload?
    }
}
