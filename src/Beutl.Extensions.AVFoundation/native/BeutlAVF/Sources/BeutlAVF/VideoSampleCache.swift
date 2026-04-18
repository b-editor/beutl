import CoreMedia
import Foundation

// Fixed-capacity ring buffer that retains recently-decoded video samples so that
// short backward seeks or rapid forward playback can hit cache instead of rewinding
// the AVAssetReader.
final class VideoSampleCache {
    private struct Entry {
        var frame: Int
        var sample: CMSampleBuffer
    }

    // Mirror of the C# implementation: if the reported frame is off by more than this,
    // log a warning but still renumber to (last + 1) to preserve monotonic ordering.
    private static let frameWarningGapCount = 1

    private let capacity: Int
    private var storage: [Entry] = []

    init(capacity: Int) {
        self.capacity = max(1, capacity)
    }

    func reset() {
        storage.removeAll(keepingCapacity: true)
    }

    func add(frame: Int, sample: CMSampleBuffer) {
        let effectiveFrame: Int
        if let last = storage.last?.frame {
            // Keep monotonic ordering regardless of PTS jitter.
            effectiveFrame = last + 1
        } else {
            effectiveFrame = frame
        }

        if storage.count >= capacity {
            storage.removeFirst()
        }
        storage.append(Entry(frame: effectiveFrame, sample: sample))
    }

    func lastFrameNumber() -> Int {
        return storage.last?.frame ?? -1
    }

    func search(frame: Int) -> CMSampleBuffer? {
        // Reverse iteration mirrors the C# implementation that favors the most
        // recently-cached frame in case of accidental duplicates.
        for entry in storage.reversed() where entry.frame == frame {
            return entry.sample
        }
        return nil
    }
}
