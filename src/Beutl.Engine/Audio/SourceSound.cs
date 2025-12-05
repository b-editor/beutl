using Beutl.Engine;
using Beutl.Media.Source;

namespace Beutl.Audio;

public sealed class SourceSound : Sound
{
    public SourceSound()
    {
        ScanProperties<SourceSound>();
    }

    public IProperty<ISoundSource?> Source { get; } = Property.Create<ISoundSource?>();

    protected override ISoundSource? GetSoundSource()
    {
        return Source.CurrentValue;
    }

    protected override TimeSpan TimeCore(TimeSpan available)
    {
        if (Source.CurrentValue != null)
        {
            return Source.CurrentValue.Duration;
        }
        else
        {
            return available;
        }
    }
}
