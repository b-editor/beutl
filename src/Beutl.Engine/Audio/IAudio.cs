using Beutl.Media.Music;
using Beutl.Media.Music.Samples;

using static Beutl.Audio.AudioPushedState;

namespace Beutl.Audio;

public sealed class Audio : IAudio
{
    private static readonly TimeSpan s_second = TimeSpan.FromSeconds(1);
    private readonly Stack<float> _gainStack = new();
    private readonly Stack<TimeSpan> _offsetStack = new();
    private readonly Pcm<Stereo32BitFloat> _buffer;
    private TimeSpan _offset;

    public Audio(int sampleRate)
    {
        SampleRate = sampleRate;

        _buffer = new Pcm<Stereo32BitFloat>(SampleRate, SampleRate);
    }

    public int SampleRate { get; }

    public bool IsDisposed { get; private set; }

    public float Gain { get; set; } = 1;

    public TimeSpan Offset
    {
        get => _offset;
        set
        {
            if (value < TimeSpan.Zero || value > s_second)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(value),
                    "Offset must be more than 0 seconds and less than or equal to 1 second.");
            }

            _offset = value;
        }
    }

    private void VerifyAccess()
    {
        ObjectDisposedException.ThrowIf(IsDisposed, this);
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

    public AudioPushedState PushOffset(TimeSpan offset)
    {
        if (offset < TimeSpan.Zero || offset > s_second)
        {
            throw new ArgumentOutOfRangeException(
                nameof(offset),
                "Offset must be more than 0 seconds and less than or equal to 1 second.");
        }

        VerifyAccess();
        int level = _offsetStack.Count;
        _offsetStack.Push(Offset);
        Offset = offset;
        return new AudioPushedState(this, level, PushedStateType.Offset);
    }

    public void PopOffset(int level = -1)
    {
        VerifyAccess();
        level = level < 0 ? _offsetStack.Count - 1 : level;

        while (_offsetStack.Count > level
            && _offsetStack.TryPop(out TimeSpan state))
        {
            Offset = state;
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
        int startSamples = (int)(Offset.TotalSeconds * SampleRate);
        _buffer.Compound(startSamples, stereoFloat);

        stereoFloat.Dispose();
    }

    public Pcm<Stereo32BitFloat> GetPcm()
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

    AudioPushedState PushOffset(TimeSpan offset);

    void PopOffset(int level = -1);

    Pcm<Stereo32BitFloat> GetPcm();
}
