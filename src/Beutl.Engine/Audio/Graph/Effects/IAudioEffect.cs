using System;
using Beutl.Animation;

namespace Beutl.Audio.Graph.Effects;

public interface IAudioEffect : IAnimatable
{
    bool IsEnabled { get; }
    
    IAudioEffectProcessor CreateProcessor();
}

public interface IAudioEffectProcessor : IDisposable
{
    void Process(AudioBuffer input, AudioBuffer output, AudioProcessContext context);
    
    void Reset();
    
    void Prepare(Media.TimeRange range, int sampleRate);
}