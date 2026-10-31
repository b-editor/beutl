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

    let capacity: Int
    private var storage: [Entry] = []

    init(capacity: Int) {
        // One more slot keeps the sample after the one on screen, which tells where that one ends.
        self.capacity = max(1, capacity) + 1
    }

    func reset() {
        storage.removeAll(keepingCapacity: true)
    }

    // `frame` is the number the sample's timestamp rounds to. Variable frame rate media puts several samples on one
    // number and none on others.
    func add(frame: Int, sample: CMSampleBuffer) {
        if storage.count >= capacity {
            storage.removeFirst()
        }
        storage.append(Entry(frame: frame, sample: sample))
    }

    func lastFrameNumber() -> Int {
        return storage.last?.frame ?? -1
    }

    // The sample on screen at `frame` is the last one numbered at or before it. A later sample has to show that it
    // ends after `frame`, unless the stream has ended; otherwise the cached samples cannot tell, and this returns nil.
    func search(frame: Int, streamEnded: Bool) -> CMSampleBuffer? {
        guard let index = storage.lastIndex(where: { $0.frame <= frame }),
              index < storage.count - 1 || streamEnded
        else {
            return nil
        }
        return storage[index].sample
    }
}
