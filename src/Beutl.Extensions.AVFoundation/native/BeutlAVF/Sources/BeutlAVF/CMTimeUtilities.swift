import CoreMedia
import Foundation

enum CMTimeUtilities {
    static func frame(fromTimestamp timestamp: CMTime, rate: Double) -> Int {
        let seconds = CMTimeGetSeconds(timestamp)
        guard seconds.isFinite, rate > 0 else { return 0 }
        return Int((seconds * rate).rounded(.toNearestOrAwayFromZero))
    }

    static func timestamp(fromFrame frame: Int, rate: Double) -> CMTime {
        guard rate > 0 else { return .zero }
        return CMTimeMakeWithSeconds(Double(frame) / rate, preferredTimescale: 1)
    }
}
