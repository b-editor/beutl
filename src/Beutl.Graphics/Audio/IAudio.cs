using Beutl.Media.Music;
using Beutl.Media.Music.Samples;

using static Beutl.Audio.AudioPushedState;

namespace Beutl.Audio;

public sealed class Audio : IAudio
{
    private readonly Stack<float> _gainStack = new();
    private readonly Pcm<Stereo32BitFloat> _buffer;

    public Audio(int sampleRate)
    {
        SampleRate = sampleRate;

        _buffer = new Pcm<Stereo32BitFloat>(SampleRate, SampleRate);
    }

    public int SampleRate { get; }

    public bool IsDisposed { get; private set; }

    public float Gain { get; set; } = 1;

    private void VerifyAccess()
    {
        if (IsDisposed)
            throw new ObjectDisposedException(nameof(Audio));
    }

    public void Dispose()
    {
        if (!IsDisposed)
        {
            _gainStack.Clear();
            _gainStack.TrimExcess();
            _buffer.Dispose();
            IsDisposed = true;
        }
    }

    public AudioPushedState PushGain(float gain)
    {
        VerifyAccess();
        int level = _gainStack.Count;
        _gainStack.Push(Gain);
        Gain = gain;
        return new AudioPushedState(this, level, PushedStateType.Gain);
    }

    public void PopGain(int level = -1)
    {
        VerifyAccess();
        level = level < 0 ? _gainStack.Count - 1 : level;

        while (_gainStack.Count > level
            && _gainStack.TryPop(out float state))
        {
            Gain = state;
        }
    }

    public void Clear()
    {
        VerifyAccess();
        _buffer.DataSpan.Clear();
    }

    public void RecordPcm(IPcm pcm)
    {
        VerifyAccess();
        if (pcm is not Pcm<Stereo32BitFloat> stereoFloat)
        {
            stereoFloat = pcm.Convert<Stereo32BitFloat>();
        }
        else
        {
            stereoFloat = stereoFloat.Clone();
        }

        stereoFloat.Amplifier(new Sample(Gain, Gain));
        _buffer.Compound(stereoFloat);

        stereoFloat.Dispose();
    }

    public IPcm GetPcm()
    {
        VerifyAccess();
        return _buffer.Clone();
    }
}

public interface IAudio : IDisposable
{
    int SampleRate { get; }

    bool IsDisposed { get; }

    float Gain { get; set; }

    void RecordPcm(IPcm pcm);

    AudioPushedState PushGain(float gain);

    void PopGain(int level = -1);
}
