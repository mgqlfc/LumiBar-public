import Foundation

struct LyricCountdown: Equatable {
    let nextIndex: Int
    let progress: Double
    let isIntro: Bool

    var caption: String { isIntro ? "开场倒计时" : "间奏倒计时" }

    static func opacity(of dot: Int, progress: Double) -> Double {
        0.22 + 0.78 * min(1, max(0, progress * 3 - Double(dot)))
    }

    static func current(in lyrics: [Lyric], track: Track, position: Double, offset: Double,
                        acceptsManualText: Bool) -> Self? {
        guard position.isFinite, offset.isFinite,
              let first = Lyric.firstVocal(in: lyrics, for: track, acceptsManualText: acceptsManualText) else { return nil }

        let isIntro = position < first.time
        let nextIndex: Int
        let start: Double
        let end: Double
        if isIntro {
            nextIndex = first.index
            // On the shifted lyric clock, playback starts at the offset.
            start = offset
            end = first.time
        } else {
            let upcoming = lyrics.indices.dropFirst(first.index + 1).lazy.compactMap { index -> (index: Int, time: Double)? in
                let line = lyrics[index]
                let time = line.vocalStartTime
                guard time > position, line.isVocalLine else { return nil }
                return (index, time)
            }.first
            guard let upcoming, let previous = lyrics[first.index..<upcoming.index].last(where: \.isVocalLine) else { return nil }
            nextIndex = upcoming.index
            end = upcoming.time
            // A line timestamp alone does not establish when the previous voice stops.
            let vocalEnd = previous.words.compactMap { word -> Double? in
                guard word.time.isFinite, word.duration.isFinite, word.duration > 0,
                      word.time >= max(0, previous.time - 0.3),
                      !word.text.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty else { return nil }
                let ending = word.time + word.duration
                return ending.isFinite ? ending : nil
            }.max()
            guard let vocalEnd, end - vocalEnd >= 4 else { return nil }
            start = max(offset, vocalEnd)
        }

        let length = end - start
        guard start <= position, position < end, length.isFinite,
              track.duration <= 0 || end - offset < track.duration else { return nil }
        return Self(nextIndex: nextIndex, progress: (position - start) / length, isIntro: isIntro)
    }

}
