import CoreMedia
import Foundation

// Fixed-capacity ring buffer for decoded audio samples. Unlike the video cache,
// a single read can span multiple cached entries, so ``search`` advances the
// caller-supplied cursor until the requested range is satisfied or exhausted.
final class AudioSampleCache {
    private struct Entry {
        var startSample: Int
        var sample: CMSampleBuffer
        var sampleCount: Int
    }

    private let capacity: Int
    private var storage: [Entry] = []
    private var blockAlign: Int = 0  // bytes per interleaved frame (e.g. 8 for Stereo32BitFloat)

    init(capacity: Int) {
        self.capacity = max(1, capacity)
    }

    func reset(blockAlign: Int) {
        self.blockAlign = blockAlign
        storage.removeAll(keepingCapacity: true)
    }

    func add(startSample: Int, sample: CMSampleBuffer) {
        let count = CMSampleBufferGetNumSamples(sample)
        let effectiveStart: Int
        if let last = storage.last {
            effectiveStart = last.startSample + last.sampleCount
        } else {
            effectiveStart = startSample
        }

        if storage.count >= capacity {
            storage.removeFirst()
        }
        storage.append(Entry(startSample: effectiveStart, sample: sample, sampleCount: count))
    }

    func lastAudioSampleNumber() -> Int {
        return storage.last?.startSample ?? -1
    }

    // Iterate over cached buffers and copy any overlap with [startSample, startSample+length).
    // Advances `startSample`, `remaining`, and `buffer` as bytes are copied.
    // Returns true iff the full requested range was satisfied from cache.
    @discardableResult
    func copyInto(
        startSample: inout Int,
        remaining: inout Int,
        buffer: inout UnsafeMutableRawPointer
    ) -> Bool {
        guard blockAlign > 0 else { return false }

        for entry in storage {
            let cacheEnd = entry.startSample + entry.sampleCount
            guard entry.startSample <= startSample, startSample < cacheEnd else { continue }

            let requestEnd = startSample + remaining
            let overlapEnd = min(requestEnd, cacheEnd)
            let bytesOffset = (startSample - entry.startSample) * blockAlign
            let bytesToCopy = (overlapEnd - startSample) * blockAlign

            copy(sample: entry.sample,
                 offsetBytes: bytesOffset,
                 byteCount: bytesToCopy,
                 destination: buffer)

            startSample = overlapEnd
            remaining = requestEnd - overlapEnd
            buffer = buffer.advanced(by: bytesToCopy)

            if remaining == 0 { return true }
        }
        return remaining == 0
    }

    private func copy(
        sample: CMSampleBuffer,
        offsetBytes: Int,
        byteCount: Int,
        destination: UnsafeMutableRawPointer
    ) {
        guard let dataBuffer = CMSampleBufferGetDataBuffer(sample) else { return }
        let status = CMBlockBufferCopyDataBytes(
            dataBuffer,
            atOffset: offsetBytes,
            dataLength: byteCount,
            destination: destination)
        if status != kCMBlockBufferNoErr {
            setLastErrorMessage("CMBlockBufferCopyDataBytes failed: \(status)")
        }
    }
}
