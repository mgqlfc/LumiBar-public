import AppKit
import Foundation

enum SystemMedia {
    struct Snapshot: Decodable, Sendable {
        let bundleIdentifier: String?
        let processIdentifier: Int32?
        let title: String?
        let artist: String?
        let album: String?
        let durationMicros: Double?
        let elapsedTimeMicros: Double?
        let timestampEpochMicros: Double?
        let playing: Bool?
        let playbackRate: Double?
        let artworkData: String?

        func matches(_ track: Track) -> Bool {
            bundleIdentifier == track.player.rawValue && title == track.title
                && (processIdentifier == nil || processIdentifier == track.pid)
        }

        @MainActor func track(at date: Date) -> Track? {
            guard let id = bundleIdentifier, !id.isEmpty, let title, !title.isEmpty else { return nil }
            let app = processIdentifier.flatMap(NSRunningApplication.init(processIdentifier:))
                ?? NSRunningApplication.runningApplications(withBundleIdentifier: id).first
            let player = Player.allCases.first { $0.rawValue == id }
                ?? Player(rawValue: id, name: app?.localizedName ?? id)
            let reportedRate = max(0, min(16, playbackRate ?? 1))
            let isPlaying = playing ?? (reportedRate > 0)
            let rate = isPlaying ? (reportedRate > 0 ? reportedRate : 1) : 0
            let elapsed = (elapsedTimeMicros ?? 0) / 1_000_000
            let duration = min(86_400, max(0, (durationMicros ?? 0) / 1_000_000))
            let reportedAt = timestampEpochMicros.map { Date(timeIntervalSince1970: $0 / 1_000_000) }
            return Track(player: player, title: title, artist: artist ?? "", album: album ?? "",
                         duration: duration, position: max(0, elapsed), playing: isPlaying,
                         artworkURL: nil, pid: processIdentifier ?? app?.processIdentifier ?? 0,
                         observedAt: min(date, reportedAt ?? date), estimatedPosition: elapsedTimeMicros == nil,
                         systemSession: true, playbackRate: rate)
        }
    }

    struct Event: Decodable {
        let type: String
        let payload: Snapshot?
    }

    enum Command: Equatable {
        case previous, playPause, next, seek(Double)

        var arguments: [String]? {
            switch self {
            case .previous: return ["previous-track"]
            case .playPause: return ["toggle-play-pause"]
            case .next: return ["next-track"]
            case .seek(let position):
                // Integer microseconds are also accepted for positions under one second.
                guard let micros = Int64(exactly: (position * 1_000_000).rounded()), micros >= 0 else { return nil }
                return ["seek", "--micros", String(micros)]
            }
        }
    }

    struct CommandReceipt {
        let before: Snapshot
        let sentAt: Date
    }

    struct SeekConfirmation {
        let before: Track
        let target: Double
        let sentAt: Date
        private let positionTolerance = 0.35

        var alreadyAtTarget: Bool { abs(target - before.position(at: sentAt)) <= positionTolerance }

        func accepts(_ reported: Track) -> Bool {
            guard reported.key == before.key, reported.pid == before.pid, reported.systemSession,
                  !reported.estimatedPosition, reported.position.isFinite else { return false }
            let delay = reported.observedAt.timeIntervalSince(sentAt)
            guard delay >= -0.125, reported.observedAt != before.observedAt || reported.position != before.position
            else { return false }

            // The player may report the new anchor after playback has already advanced from the target.
            let advance = reported.playing ? max(0, delay) * reported.playbackRate : 0
            let lower = target - positionTolerance
            let upper = min(before.duration, target + advance) + positionTolerance
            let originalPosition = before.position(at: reported.observedAt)
            let minimumJump = min(0.60, abs(target - before.position(at: sentAt)) / 2)
            return reported.position >= lower && reported.position <= upper
                && abs(reported.position - originalPosition) >= minimumJump
        }
    }

    static var executable: URL? {
        let bundled = Bundle.main.resourceURL?.appending(path: "media-control/bin/media-control")
        let system = ["/opt/homebrew/bin/media-control", "/usr/local/bin/media-control"]
            .map { URL(fileURLWithPath: $0) }
        return ([bundled].compactMap { $0 } + system).first { FileManager.default.isExecutableFile(atPath: $0.path) }
    }

    static func stream(using executable: URL) -> AsyncStream<Data> {
        AsyncStream { continuation in
            let process = Process()
            let pipe = Pipe()
            process.executableURL = executable
            process.arguments = ["stream", "--no-diff", "--micros", "--no-artwork"]
            process.standardOutput = pipe
            process.standardError = FileHandle.nullDevice
            let reader = pipe.fileHandleForReading
            continuation.onTermination = { _ in
                reader.readabilityHandler = nil
                if process.isRunning { process.terminate() }
            }
            reader.readabilityHandler = { handle in
                let bytes = handle.availableData
                if bytes.isEmpty { continuation.finish() }
                else { continuation.yield(bytes) }
            }
            do { try process.run() }
            catch { continuation.finish() }
        }
    }

    static func snapshot(using executable: URL, artwork: Bool = false, timeout: TimeInterval = 3) async -> Snapshot? {
        let args = artwork ? ["get", "--micros"] : ["get", "--micros", "--no-artwork"]
        guard let data = await invoke(executable, args, timeout: timeout), data.count < 12_000_000 else { return nil }
        return try? JSONDecoder().decode(Snapshot.self, from: data)
    }

    static func send(_ command: Command, to track: Track, using executable: URL) async -> CommandReceipt? {
        guard let arguments = command.arguments else { return nil }
        var observation: Snapshot?
        if case .seek(let target) = command {
            guard target.isFinite, track.duration > 0, target >= 0, target <= track.duration else { return nil }
            observation = await snapshot(using: executable, timeout: 0.65)
        } else {
            observation = await snapshot(using: executable)
        }
        if case .seek = command, observation?.bundleIdentifier == nil, !Task.isCancelled {
            observation = await snapshot(using: executable, timeout: 0.65)
        }
        guard let observation, observation.matches(track), !Task.isCancelled else { return nil }
        let sentAt = Date.now
        guard await invoke(executable, arguments) != nil else { return nil }
        return CommandReceipt(before: observation, sentAt: sentAt)
    }

    private static func invoke(_ executable: URL, _ arguments: [String], timeout: TimeInterval = 3) async -> Data? {
        await withCheckedContinuation { continuation in
            DispatchQueue.global(qos: .userInitiated).async {
                let process = Process()
                let pipe = Pipe()
                process.executableURL = executable
                process.arguments = arguments
                process.standardOutput = pipe
                process.standardError = FileHandle.nullDevice
                do { try process.run() }
                catch { continuation.resume(returning: nil); return }
                let timeoutTask = DispatchWorkItem { if process.isRunning { process.terminate() } }
                DispatchQueue.global().asyncAfter(deadline: .now() + timeout, execute: timeoutTask)
                let result = pipe.fileHandleForReading.readDataToEndOfFile()
                process.waitUntilExit()
                timeoutTask.cancel()
                continuation.resume(returning: process.terminationStatus == 0 ? result : nil)
            }
        }
    }
}
