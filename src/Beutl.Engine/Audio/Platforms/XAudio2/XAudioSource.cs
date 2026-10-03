using Vortice.Multimedia;
using Vortice.XAudio2;

namespace Beutl.Audio.Platforms.XAudio2;

public sealed class XAudioSource : IDisposable
{
    private readonly Func<WaveFormat, IXAudio2SourceVoice> _createSourceVoice;
    private readonly object _lifetimeGate = new();
    private IXAudio2SourceVoice? _sourceVoice;
    private bool _isDisposed;

    public XAudioSource(XAudioContext context) : this(format => context.Device.CreateSourceVoice(format))
    {
    }

    internal XAudioSource(Func<WaveFormat, IXAudio2SourceVoice> createSourceVoice)
    {
        _createSourceVoice = createSourceVoice;
    }

    public int BuffersQueued
    {
        get { lock (_lifetimeGate) return (int?)_sourceVoice?.State.BuffersQueued ?? -1; }
    }

    public ulong SamplesPlayed
    {
        get { lock (_lifetimeGate) return _sourceVoice?.State.SamplesPlayed ?? 0UL; }
    }

    public void Dispose()
    {
        lock (_lifetimeGate)
        {
            if (_isDisposed) return;
            _isDisposed = true;
            IXAudio2SourceVoice? sourceVoice = _sourceVoice;
            _sourceVoice = null;
            DisposeVoice(sourceVoice);
        }
    }

    private static void DisposeVoice(IXAudio2SourceVoice? voice)
    {
        try { voice?.DestroyVoice(); }
        finally { voice?.Dispose(); }
    }

    public bool IsPlaying()
    {
        lock (_lifetimeGate)
            return _sourceVoice?.State.BuffersQueued > 0;
    }

    public void Play()
    {
        lock (_lifetimeGate)
            _sourceVoice?.Start();
    }

    public void Stop()
    {
        lock (_lifetimeGate)
            _sourceVoice?.Stop();
    }

    public void QueueBuffer(XAudioBuffer buffer)
    {
        lock (_lifetimeGate)
        {
            ObjectDisposedException.ThrowIf(_isDisposed, this);
            if (_sourceVoice == null)
            {
                IXAudio2SourceVoice voice = _createSourceVoice(buffer.Format!);
                // A factory which reenters disposal must not publish new ownership afterward.
                if (_isDisposed)
                {
                    DisposeVoice(voice);
                    throw new ObjectDisposedException(nameof(XAudioSource));
                }
                _sourceVoice = voice;
            }

            _sourceVoice.SubmitSourceBuffer(buffer.Buffer);
        }
    }

    public void Flush()
    {
        lock (_lifetimeGate)
            _sourceVoice?.FlushSourceBuffers();
    }
}
