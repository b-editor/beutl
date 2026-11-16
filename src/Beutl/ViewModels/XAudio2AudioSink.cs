using System.Runtime.Versioning;
using Beutl.Audio;
using Beutl.Audio.Platforms.XAudio2;

namespace Beutl.ViewModels;

// Plays through an XAudio2 source voice with two buffers.
[SupportedOSPlatform("windows")]
internal sealed class XAudio2AudioSink : IAudioSink, IDisposable
{
    private readonly XAudioSource _source;
    private readonly AudioChannelLayout _outputLayout;
    private readonly AudioChannelMatrix? _outputMatrix;
    private readonly bool _useLayout;
    private readonly XAudioBuffer[] _buffers;
    // Buffers submitted to the voice, oldest first.
    private readonly Queue<XAudioBuffer> _submitted = new();
    private int _nextUnqueued;

    public XAudio2AudioSink(XAudioContext context, AudioChannelLayout projectLayout, bool useMultichannelBackend)
    {
        (_outputLayout, _outputMatrix) = AudioPlaybackOutput.Create(projectLayout, context.DeviceLayout);
        _useLayout = useMultichannelBackend;
        _source = new XAudioSource(context);
        _buffers = [new XAudioBuffer(), new XAudioBuffer()];
    }

    public int BufferCount => _buffers.Length;

    public long PlayedSamples => (long)_source.SamplesPlayed;

    // BuffersQueued counts the buffer the voice is reading, so the rest of the submitted ones are done.
    public int ProcessedBufferCount => Math.Max(0, _submitted.Count - Math.Max(0, _source.BuffersQueued));

    // A voice keeps running when it runs out of buffers, so "playing" means it still has some queued.
    public bool IsPlaying => _source.IsPlaying();

    public void QueueBuffer(AudioBuffer? audio)
    {
        if (_nextUnqueued >= _buffers.Length)
            throw new InvalidOperationException("Every buffer is already queued.");

        Submit(_buffers[_nextUnqueued++], audio);
    }

    public void RequeueProcessedBuffer(AudioBuffer? audio)
    {
        if (ProcessedBufferCount <= 0)
            throw new InvalidOperationException("No queued buffer has finished playing.");

        Submit(_submitted.Dequeue(), audio);
    }

    public void ReleaseProcessedBuffers()
    {
        for (int processed = ProcessedBufferCount; processed > 0; processed--)
        {
            _submitted.Dequeue();
        }
    }

    public void Play() => _source.Play();

    public void Stop() => _source.Stop();

    public void Dispose()
    {
        _source.Dispose();
        foreach (XAudioBuffer buffer in _buffers)
        {
            buffer.Dispose();
        }
    }

    private void Submit(XAudioBuffer buffer, AudioBuffer? audio)
    {
        if (audio != null)
        {
            using AudioBuffer? converted = _outputMatrix?.Apply(audio);
            if (_useLayout)
                buffer.BufferData(converted ?? audio, _outputLayout);
            else
                buffer.BufferData(converted ?? audio);
        }

        _source.QueueBuffer(buffer);
        _submitted.Enqueue(buffer);
    }
}
