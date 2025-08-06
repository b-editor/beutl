using Beutl.Audio.Graph;

namespace Beutl.Audio.Effects;

public interface IAudioEffectProcessor : IDisposable
{
    void Process(AudioBuffer input, AudioBuffer output, AudioProcessContext context);

    void Reset();

    void Prepare(Media.TimeRange range, int sampleRate);
}
