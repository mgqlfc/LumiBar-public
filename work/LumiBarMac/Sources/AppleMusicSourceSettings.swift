import AppKit
import SwiftUI
import WebKit

struct AppleMusicSourceSettings: View {
    let music: MusicModel
    @State private var login: AppleMusicLoginWindow?
    @State private var message: String?
    @State private var disconnecting = false

    private var connectionLabel: String {
        switch music.appleMusicLyricConnection {
        case .disconnected: "官方同步歌词，需要订阅"
        case let .connected(storefront): "已连接 · \(storefront.uppercased())"
        case .reconnect: "请重新连接 Apple Music"
        }
    }

    var body: some View {
        HStack(spacing: 8) {
            VStack(alignment: .leading, spacing: 2) {
                LyricSourceLabel(source: .appleMusic)
                Text(message ?? connectionLabel).font(.caption).foregroundStyle(.secondary)
            }
            Spacer(minLength: 0)
            if !music.appleMusicLyricConnection.canSearch {
                Button(music.appleMusicLyricConnection == .reconnect ? "重新连接…" : "连接…", action: connect)
                    .accessibilityLabel("连接 Apple Music 歌词")
            }
            if music.appleMusicLyricConnection != .disconnected {
                Button("断开", action: disconnect).accessibilityLabel("断开 Apple Music 歌词")
            }
        }
        .buttonStyle(.glass).controlSize(.small)
        .disabled(music.previewSpectrum != nil || disconnecting)
        .task { await music.refreshAppleMusicLyricConnection() }
    }

    private func connect() {
        if let login { login.showWindow(nil); return }
        message = nil
        let controller = AppleMusicLoginWindow { result in
            login = nil
            guard !disconnecting else { return }
            if let result {
                message = result
            } else {
                Task { await music.refreshAppleMusicLyricConnection(reload: true) }
            }
        }
        login = controller
        NSApp.activate()
        controller.showWindow(nil)
    }

    private func disconnect() {
        disconnecting = true
        login?.close()
        login = nil
        Task {
            defer { disconnecting = false }
            do {
                try await AppleMusicLyrics.shared.disconnect()
                message = nil
                await music.refreshAppleMusicLyricConnection(reload: true)
            } catch {
                message = AppleMusicLyrics.Failure.keychain.errorDescription
            }
        }
    }
}

@MainActor
private final class AppleMusicLoginWindow: NSWindowController, NSWindowDelegate, WKNavigationDelegate, WKUIDelegate {
    private let browser: WKWebView
    private let notice = NSTextField(labelWithString: "在 Apple 页面登录，用于查找同步歌词。")
    private var completion: ((String?) -> Void)?
    private var checking: Task<Void, Never>?
    private var poll: Timer?
    private var readingCookies = false
    private var popup: NSWindow?

    init(completion: @escaping (String?) -> Void) {
        let configuration = WKWebViewConfiguration()
        configuration.websiteDataStore = .nonPersistent()
        browser = WKWebView(frame: .zero, configuration: configuration)
        self.completion = completion
        let window = NSWindow(contentRect: CGRect(x: 0, y: 0, width: 890, height: 640),
                              styleMask: [.titled, .closable, .resizable], backing: .buffered, defer: false)
        window.title = "连接 Apple Music · music.apple.com"
        window.minSize = CGSize(width: 575, height: 420)
        window.isReleasedWhenClosed = false
        super.init(window: window)
        window.delegate = self
        browser.navigationDelegate = self
        browser.uiDelegate = self
        let content = NSView()
        for view in [notice, browser] {
            view.translatesAutoresizingMaskIntoConstraints = false
            content.addSubview(view)
        }
        notice.textColor = .secondaryLabelColor
        notice.font = .systemFont(ofSize: NSFont.smallSystemFontSize)
        NSLayoutConstraint.activate([
            notice.leadingAnchor.constraint(equalTo: content.leadingAnchor, constant: 14),
            notice.trailingAnchor.constraint(lessThanOrEqualTo: content.trailingAnchor, constant: -14),
            notice.topAnchor.constraint(equalTo: content.topAnchor, constant: 8),
            browser.topAnchor.constraint(equalTo: notice.bottomAnchor, constant: 8),
            browser.leadingAnchor.constraint(equalTo: content.leadingAnchor),
            browser.trailingAnchor.constraint(equalTo: content.trailingAnchor),
            browser.bottomAnchor.constraint(equalTo: content.bottomAnchor),
        ])
        window.contentView = content
        window.center()
        browser.load(URLRequest(url: AppleMusicLyrics.loginURL))
        let timer = Timer(timeInterval: 1, repeats: true) { [weak self] _ in
            Task { @MainActor in self?.readLogin() }
        }
        RunLoop.main.add(timer, forMode: .common)
        poll = timer
    }

    @available(*, unavailable)
    required init?(coder: NSCoder) { fatalError("init(coder:) has not been implemented") }

    func windowWillClose(_ notification: Notification) {
        if let closing = notification.object as? NSWindow, closing === popup {
            popup = nil
        } else {
            complete(nil)
        }
    }

    private func allows(_ url: URL) -> Bool {
        if url.absoluteString == "about:blank" { return true }
        guard url.scheme == "https", url.port == nil || url.port == 443, url.user == nil, url.password == nil,
              let host = url.host?.lowercased() else { return false }
        return host == "apple.com" || host.hasSuffix(".apple.com")
    }

    func webView(_ webView: WKWebView, decidePolicyFor navigationAction: WKNavigationAction) async -> WKNavigationActionPolicy {
        navigationAction.request.url.map(allows) == true ? .allow : .cancel
    }

    func webView(_ webView: WKWebView, createWebViewWith configuration: WKWebViewConfiguration,
                 for navigationAction: WKNavigationAction, windowFeatures: WKWindowFeatures) -> WKWebView? {
        guard popup == nil, let url = navigationAction.request.url, allows(url) else { return nil }
        let child = WKWebView(frame: .zero, configuration: configuration)
        child.navigationDelegate = self
        child.uiDelegate = self
        let panel = NSWindow(contentRect: CGRect(x: 0, y: 0, width: 580, height: 595),
                             styleMask: [.titled, .closable], backing: .buffered, defer: false)
        panel.isReleasedWhenClosed = false
        panel.title = "Apple 账户登录"
        panel.contentView = child
        panel.delegate = self
        panel.center()
        popup = panel
        panel.makeKeyAndOrderFront(nil)
        return child
    }

    func webViewDidClose(_ webView: WKWebView) {
        if popup?.contentView === webView { popup?.close() }
    }

    func webView(_ webView: WKWebView, didFailProvisionalNavigation navigation: WKNavigation!, withError error: any Error) {
        if (error as NSError).code != NSURLErrorCancelled { notice.stringValue = "无法加载 Apple Music，请检查网络后重新连接。" }
    }

    private func readLogin() {
        guard completion != nil, checking == nil, !readingCookies else { return }
        readingCookies = true
        browser.configuration.websiteDataStore.httpCookieStore.getAllCookies { [weak self] cookies in
            Task { @MainActor in
                guard let self else { return }
                self.readingCookies = false
                guard self.completion != nil, self.checking == nil,
                      let cookie = cookies.first(where: { cookie in
                          let domain = cookie.domain.trimmingCharacters(in: CharacterSet(charactersIn: "."))
                          return cookie.name == "media-user-token" && cookie.isSecure &&
                              (domain == "music.apple.com" || domain == "apple.com")
                      }) else { return }
                self.notice.stringValue = "正在验证 Apple Music 连接…"
                self.checking = Task { @MainActor [weak self] in
                    do {
                        try await AppleMusicLyrics.shared.connect(token: cookie.value, expires: cookie.expiresDate)
                        guard !Task.isCancelled else { return }
                        self?.finishAndClose(nil)
                    } catch {
                        guard !Task.isCancelled else { return }
                        self?.finishAndClose((error as? AppleMusicLyrics.Failure)?.errorDescription ?? "暂时无法连接 Apple Music，请重试。")
                    }
                }
            }
        }
    }

    private func finishAndClose(_ message: String?) {
        complete(message)
        close()
    }

    private func complete(_ message: String?) {
        poll?.invalidate()
        poll = nil
        checking?.cancel()
        checking = nil
        popup?.close()
        browser.stopLoading()
        let callback = completion
        completion = nil
        callback?(message)
    }
}
