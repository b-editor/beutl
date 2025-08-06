namespace Beutl.Audio.Effects;

public interface IAudioEffect
{
    bool IsEnabled { get; }

    IAudioEffectProcessor CreateProcessor();
}
