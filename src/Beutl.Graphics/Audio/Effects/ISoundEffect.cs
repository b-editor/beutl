namespace Beutl.Audio.Effects;

public interface ISoundEffect
{
    bool IsEnabled { get; }

    ISoundProcessor CreateProcessor();
}
