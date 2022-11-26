using Beutl.Media.Music;

namespace Beutl.Audio;

public interface IAudio : IDisposable
{
    int SamplesPerFrame { get; }

    bool IsDisposed { get; }

    float Gain { get; set; }

    void RecordPcm(IPcm pcm);

    AudioPushedState PushGain(float gain);

    void PopGain(int level = -1);
}
