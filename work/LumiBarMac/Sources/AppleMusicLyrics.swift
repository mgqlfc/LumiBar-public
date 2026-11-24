import Foundation
import LocalAuthentication
import Security

actor AppleMusicLyrics {
    enum Connection: Equatable, Sendable {
        case disconnected, connected(storefront: String), reconnect

        var canSearch: Bool { if case .connected = self { true } else { false } }
    }

    enum Failure: LocalizedError {
        case catalog, account, keychain

        var errorDescription: String? {
            switch self {
            case .catalog: "暂时无法连接 Apple Music 曲库，请重试。"
            case .account: "Apple Music 未确认此次登录，请检查账号和订阅后重试。"
            case .keychain: "无法访问钥匙串中的 Apple Music 连接。"
            }
        }
    }

    private struct Client: Codable, Sendable {
        let token: String
        let expiry: Date
        var isCurrent: Bool { expiry.timeIntervalSinceNow > 60 }
    }

    private struct Account: Codable, Sendable {
        let token: String
        let expiry: Date?
        let storefront: String
        let client: Client
    }

    private struct Items<Item: Decodable>: Decodable { let data: [Item] }
    private struct Storefront: Decodable { let id: String }

    private struct Song: Decodable {
        struct Attributes: Decodable {
            let name, artistName, albumName: String
            let durationInMillis: Double
            let hasLyrics, hasTimeSyncedLyrics: Bool?
        }
        let id: String
        let attributes: Attributes

        var recording: LyricRecording? {
            guard id.count <= 24, id.wholeMatch(of: /[0-9]+/) != nil,
                  (1...86_400_000).contains(attributes.durationInMillis),
                  attributes.hasLyrics != false, attributes.hasTimeSyncedLyrics != false else { return nil }
            return .init(id: id, title: attributes.name, artist: attributes.artistName,
                         album: attributes.albumName, duration: attributes.durationInMillis / 1000)
        }
    }

    private struct Search: Decodable {
        struct Results: Decodable { let songs: Items<Song>? }
        let results: Results
    }

    private struct TimedText: Decodable {
        struct Attributes: Decodable { let ttml, ttmlLocalizations: String? }
        let attributes: Attributes
    }

    private struct HTTPStatus: Error { let code: Int }

    private final class Redirects: NSObject, URLSessionTaskDelegate {
        func urlSession(_ session: URLSession, task: URLSessionTask, willPerformHTTPRedirection response: HTTPURLResponse,
                        newRequest request: URLRequest, completionHandler: @escaping @Sendable (URLRequest?) -> Void) {
            // The public homepage may redirect to its regional path. API requests retain their exact host and path.
            guard let url = request.url, url.scheme == "https", url.host == response.url?.host,
                  url.port == nil || url.port == 443, url.user == nil, url.password == nil,
                  url.host != Self.apiHost else { completionHandler(nil); return }
            completionHandler(request)
        }

        private static let apiHost = "amp-api.music.apple.com"
    }

    static let shared = AppleMusicLyrics()
    static let loginURL = URL(string: "https://music.apple.com/login")!
    private static let publicOrigin = "https://music.apple.com"
    private static let apiURL = URL(string: "https://amp-api.music.apple.com/v1/")!
    private static let userAgent = "Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7) AppleWebKit/605.1.15 (KHTML, like Gecko) Version/26.0 Safari/605.1.15"

    private let session: URLSession = {
        let configuration = URLSessionConfiguration.ephemeral
        configuration.timeoutIntervalForRequest = 5
        configuration.timeoutIntervalForResource = 8
        configuration.httpShouldSetCookies = false
        return URLSession(configuration: configuration, delegate: Redirects(), delegateQueue: nil)
    }()
    private var account: Account?
    private var loaded = false
    private var rejected = false
    private var revision = 0
    private var renewal: Task<Account, Error>?

    func connection() -> Connection {
        loadSavedAccount()
        guard let account else { return .disconnected }
        if rejected || account.expiry.map({ $0 <= .now }) == true { return .reconnect }
        return .connected(storefront: account.storefront)
    }

    func connect(token: String, expires: Date?) async throws {
        guard !token.isEmpty, token.utf8.count < 16_384,
              token.rangeOfCharacter(from: .controlCharacters) == nil else { throw Failure.account }
        let expected = startConnectionChange()
        let linked = try await authorize(token: token, expiry: expires)
        try Task.checkCancellation()
        guard expected == revision else { throw CancellationError() }
        try store(linked)
        account = linked
        loaded = true
        rejected = false
    }

    func disconnect() throws {
        _ = startConnectionChange()
        let status = SecItemDelete(Self.keychainQuery as CFDictionary)
        guard status == errSecSuccess || status == errSecItemNotFound else { throw Failure.keychain }
        account = nil
        loaded = true
        rejected = false
    }

    private func startConnectionChange() -> Int {
        revision += 1
        renewal?.cancel()
        renewal = nil
        return revision
    }

    func candidates(for track: Track, report: @escaping LyricLookup.Report) async {
        guard connection().canSearch, let account else { return }
        let expected = revision
        do {
            let credentials = account.client.isCurrent ? account : try await renewed(account, revision: expected)
            let (linked, recordings) = try await search(track, credentials: credentials, revision: expected)
            guard expected == revision, !Task.isCancelled else { return }
            _ = await OnlineLyricCatalogs.fetch(recordings, from: .appleMusic, report: report) { recording in
                await self.fetchLyrics(for: recording, matching: track, account: linked, revision: expected)
            }
        } catch { return }
    }

    private func search(_ track: Track, credentials: Account, revision: Int) async throws -> (Account, [LyricRecording]) {
        do { return (credentials, try await findRecordings(track, account: credentials)) }
        catch let error as HTTPStatus where error.code == 401 || error.code == 403 {
            let fresh = try await renewed(credentials, revision: revision)
            return (fresh, try await findRecordings(track, account: fresh))
        }
    }

    private func findRecordings(_ track: Track, account: Account) async throws -> [LyricRecording] {
        let query = [URLQueryItem(name: "term", value: "\(track.title) \(track.artist)"),
                     .init(name: "types", value: "songs"), .init(name: "limit", value: "12")]
        func find(_ items: [URLQueryItem]) async throws -> [LyricRecording] {
            let data = try await request("catalog/\(account.storefront)/search", query: items, client: account.client)
            let songs = try JSONDecoder().decode(Search.self, from: data).results.songs?.data ?? []
            return Self.bestRecordings(songs.compactMap(\.recording), matching: track)
        }
        let matches = try await find(query)
        guard matches.isEmpty, !Task.isCancelled else { return matches }
        let english = account.storefront == "jp" ? "en-US" : "en-GB"
        let localized = [URLQueryItem(name: "term", value: "\(track.title) \(track.album)"),
                         .init(name: "types", value: "songs"), .init(name: "limit", value: "12"), .init(name: "l", value: english)]
        return try await find(localized)
    }

    static func bestRecordings(_ recordings: [LyricRecording], matching track: Track) -> [LyricRecording] {
        let scored = recordings.compactMap { recording in
            recording.match(to: track).map { (recording: recording, score: $0.points) }
        }
        guard let best = scored.map(\.score).max() else { return [] }
        // Filter by recording identity before word-timing quality can affect the shared ranking.
        return scored.filter { $0.score == best }.prefix(3).map(\.recording)
    }

    private func fetchLyrics(for recording: LyricRecording, matching track: Track, account: Account, revision: Int) async -> [Lyric] {
        let localization = [URLQueryItem(name: "extend", value: "ttmlLocalizations"),
                            .init(name: "l[lyrics]", value: "zh-Hans-CN")]
        for relation in ["syllable-lyrics", "lyrics"] {
            guard self.revision == revision, !Task.isCancelled, !rejected else { return [] }
            let path = "catalog/\(account.storefront)/songs/\(recording.id)/\(relation)"
            do {
                let data = try await request(path, query: localization, client: account.client, token: account.token)
                let lines = Self.parseLyrics(data, duration: track.duration)
                guard self.revision == revision, !Task.isCancelled else { return [] }
                if LyricCandidate(source: .appleMusic, recording: recording, lines: lines).score(for: track) != nil { return lines }
            } catch let status as HTTPStatus {
                if status.code == 401 || status.code == 403 {
                    let publicClientWorks = (try? await request("storefronts/us", client: account.client)) != nil
                    if publicClientWorks, self.revision == revision { rejected = true }
                    return []
                }
                if status.code != 404 { return [] }
            } catch { return [] }
        }
        return []
    }

    static func parseLyrics(_ data: Data, duration: Double) -> [Lyric] {
        guard let item = try? JSONDecoder().decode(Items<TimedText>.self, from: data).data.first else { return [] }
        for document in [item.attributes.ttmlLocalizations, item.attributes.ttml].compactMap({ $0 })
            where !document.contains("<!DOCTYPE") {
            var lines = AMLLTiming.parse(Data(document.utf8))
            if duration.isFinite, duration > 0 {
                for index in lines.indices where lines[index].words.contains(where: { $0.time + $0.duration > duration + 2 }) {
                    lines[index].words = []
                }
            }
            if !lines.isEmpty { return lines }
        }
        return []
    }

    private func renewed(_ saved: Account, revision: Int) async throws -> Account {
        guard self.revision == revision else { throw CancellationError() }
        if let renewal {
            let refreshed = try await renewal.value
            guard self.revision == revision else { throw CancellationError() }
            return refreshed
        }
        let task = Task { try await authorize(token: saved.token, expiry: saved.expiry) }
        renewal = task
        defer { if self.revision == revision { renewal = nil } }
        do {
            let refreshed = try await task.value
            guard self.revision == revision else { throw CancellationError() }
            try store(refreshed)
            account = refreshed
            return refreshed
        } catch Failure.account {
            if self.revision == revision { rejected = true }
            throw Failure.account
        }
    }

    private func authorize(token: String, expiry: Date?) async throws -> Account {
        let clients = try await publicClients()
        var accountRejected = false
        for client in clients {
            try Task.checkCancellation()
            guard (try? await request("storefronts/us", client: client)) != nil else { continue }
            do {
                let data = try await request("me/storefront", client: client, token: token)
                if let storefront = try JSONDecoder().decode(Items<Storefront>.self, from: data).data.first?.id,
                   storefront.wholeMatch(of: /[a-z]{2}/) != nil {
                    return Account(token: token, expiry: expiry, storefront: storefront, client: client)
                }
            } catch let status as HTTPStatus where status.code == 401 || status.code == 403 { accountRejected = true }
            catch is CancellationError { throw CancellationError() }
            catch { continue }
        }
        throw accountRejected ? Failure.account : Failure.catalog
    }

    private func publicClients() async throws -> [Client] {
        let homepage = URL(string: Self.publicOrigin + "/us/browse")!
        let (html, landingPage) = try await publicSource(homepage)
        let scripts = html.matches(of: /<script[^>]+\bsrc=["']([^"']+)["']/).compactMap { match -> URL? in
            guard let url = URL(string: String(match.1), relativeTo: landingPage)?.absoluteURL,
                  url.scheme == "https", url.user == nil, url.password == nil,
                  url.port == nil || url.port == 443, let host = url.host,
                  host == "music.apple.com" || host.hasSuffix(".mzstatic.com"), url.pathExtension == "js" else { return nil }
            return url
        }
        let fromScripts = await withTaskGroup(of: [Client].self, returning: [Client].self) { group in
            for script in scripts.prefix(5) {
                group.addTask {
                    guard let (source, _) = try? await self.publicSource(script) else { return [] }
                    return Self.clients(in: source)
                }
            }
            var result: [Client] = []
            for await clients in group { result += clients }
            return result
        }
        var seen = Set<String>()
        return Array((Self.clients(in: html) + fromScripts).filter { seen.insert($0.token).inserted }.prefix(6))
    }

    private static func clients(in publicSource: String) -> [Client] {
        struct Claims: Decodable { let exp: Double }
        return publicSource.matches(of: /eyJ[\w-]+\.[\w-]+\.[\w-]+/).compactMap { match in
            let pieces = match.output.split(separator: ".")
            guard pieces.count == 3, pieces[2].count >= 60, match.output.utf8.count < 4096 else { return nil }
            var payload = pieces[1].replacingOccurrences(of: "-", with: "+").replacingOccurrences(of: "_", with: "/")
            payload += String(repeating: "=", count: (4 - payload.count % 4) % 4)
            guard let bytes = Data(base64Encoded: payload),
                  let claims = try? JSONDecoder().decode(Claims.self, from: bytes), claims.exp.isFinite else { return nil }
            let client = Client(token: String(match.output), expiry: Date(timeIntervalSince1970: claims.exp))
            return client.isCurrent ? client : nil
        }
    }

    private func publicSource(_ url: URL) async throws -> (String, URL) {
        var request = URLRequest(url: url)
        request.setValue(Self.userAgent, forHTTPHeaderField: "User-Agent")
        let (data, resolvedURL) = try await read(request, limit: 5_242_880)
        guard let result = String(data: data, encoding: .utf8) else { throw Failure.catalog }
        return (result, resolvedURL)
    }

    private func request(_ path: String, query: [URLQueryItem] = [], client: Client, token: String? = nil) async throws -> Data {
        var request = URLRequest(url: Self.apiURL.appending(path: path).appending(queryItems: query))
        request.setValue("Bearer \(client.token)", forHTTPHeaderField: "Authorization")
        request.setValue(Self.userAgent, forHTTPHeaderField: "User-Agent")
        request.setValue(Self.publicOrigin, forHTTPHeaderField: "Origin")
        if let token { request.setValue(token, forHTTPHeaderField: "Media-User-Token") }
        return try await read(request, limit: 2_097_152).0
    }

    private func read(_ request: URLRequest, limit: Int) async throws -> (Data, URL) {
        let (data, response) = try await session.data(for: request)
        guard let http = response as? HTTPURLResponse, let url = http.url else { throw Failure.catalog }
        guard (200...299).contains(http.statusCode) else { throw HTTPStatus(code: http.statusCode) }
        guard data.count <= limit else { throw Failure.catalog }
        return (data, url)
    }

    private static var keychainQuery: [String: Any] {
        [kSecClass as String: kSecClassGenericPassword, kSecAttrService as String: "com.lumibar.macos.apple-music-lyrics",
         kSecAttrAccount as String: "LumiBar Apple Music", kSecAttrSynchronizable as String: false]
    }

    private func loadSavedAccount() {
        guard !loaded else { return }
        let authentication = LAContext()
        authentication.interactionNotAllowed = true
        var query = Self.keychainQuery
        query[kSecReturnData as String] = true
        query[kSecUseAuthenticationContext as String] = authentication
        var value: CFTypeRef?
        let status = SecItemCopyMatching(query as CFDictionary, &value)
        if status == errSecSuccess {
            account = (value as? Data).flatMap { try? JSONDecoder().decode(Account.self, from: $0) }
            loaded = true
        } else if status == errSecItemNotFound {
            loaded = true
        }
    }

    private func store(_ account: Account) throws {
        let data = try JSONEncoder().encode(account)
        let attributes = [kSecValueData as String: data]
        let result = SecItemUpdate(Self.keychainQuery as CFDictionary, attributes as CFDictionary)
        if result == errSecItemNotFound {
            let item = Self.keychainQuery.merging(attributes) { _, new in new }
            guard SecItemAdd(item as CFDictionary, nil) == errSecSuccess else { throw Failure.keychain }
        } else if result != errSecSuccess { throw Failure.keychain }
    }
}
