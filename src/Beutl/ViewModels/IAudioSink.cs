using Beutl.Audio;

namespace Beutl.ViewModels;

// A playback device that cycles a fixed set of native buffers. AudioSinkRefillLoop drives it:
// it queues BufferCount buffers, starts playback, then refills each buffer once the device
// reports it as processed.
internal interface IAudioSink
{
    // The number of native buffers the sink cycles through.
    int BufferCount { get; }

    // Samples played since playback started, at the composed audio's sample rate.
    long PlayedSamples { get; }

    // Queued buffers that finished playing and have not been requeued or released yet.
    int ProcessedBufferCount { get; }

    // Whether the device is still playing queued audio. A device that ran out of buffers reports false.
    bool IsPlaying { get; }

    // Queues audio in a buffer that has not been queued yet. A null buffer queues the native buffer without new data.
    void QueueBuffer(AudioBuffer? audio);

    // Takes the oldest processed buffer back, fills it with the audio and queues it again.
    void RequeueProcessedBuffer(AudioBuffer? audio);

    // Takes every processed buffer back without queueing it again, for the drain at the end of the scene.
    void ReleaseProcessedBuffers();

    void Play();

    void Stop();
}
