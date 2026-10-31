import CoreMedia
import Foundation

enum CMTimeUtilities {
    static func frame(fromTimestamp timestamp: CMTime, rate: Double) -> Int {
        let seconds = CMTimeGetSeconds(timestamp)
        guard seconds.isFinite, rate > 0 else { return 0 }
        return Int((seconds * rate).rounded(.toNearestOrAwayFromZero))
    }

    // The exact time of `frame` at num/den frames per second.
    static func timestamp(fromFrame frame: Int, rateNum: Int32, rateDen: Int32) -> CMTime {
        guard rateNum > 0, rateDen > 0 else { return .zero }
        return CMTimeMake(value: Int64(frame) * Int64(rateDen), timescale: rateNum)
    }
}
