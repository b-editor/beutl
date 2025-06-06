﻿using Vortice.XAudio2;

namespace Beutl.Audio.Platforms.XAudio2;

public sealed class XAudioSource(XAudioContext context) : IDisposable
{
    private IXAudio2SourceVoice? _sourceVoice;
    
    public XAudioSource(XAudioContext context) : this()
    {
        MemoryManagement.TrackDisposable(this, DisposableCategory.Audio);
    }

    public int BuffersQueued => (int?)_sourceVoice?.State.BuffersQueued ?? -1;

    public void Dispose()
    {
        _sourceVoice?.DestroyVoice();
        _sourceVoice?.Dispose();
        MemoryManagement.MarkDisposed(this);
    }

    public bool IsPlaying()
    {
        return _sourceVoice?.State.BuffersQueued > 0;
    }

    public void Play()
    {
        _sourceVoice?.Start();
    }

    public void Stop()
    {
        _sourceVoice?.Stop();
    }

    public void QueueBuffer(XAudioBuffer buffer)
    {
        if (_sourceVoice == null)
        {
            _sourceVoice = context.Device.CreateSourceVoice(buffer.Format!);
        }

        _sourceVoice.SubmitSourceBuffer(buffer.Buffer);
    }

    public void Flush()
    {
        _sourceVoice?.FlushSourceBuffers();
    }
}
