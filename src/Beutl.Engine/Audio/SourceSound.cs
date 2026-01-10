using Beutl.Engine;
using Beutl.Media.Source;

namespace Beutl.Audio;

public sealed class SourceSound : Sound
{
    public SourceSound()
    {
        ScanProperties<SourceSound>();
    }

    public IProperty<SoundSource?> Source { get; } = Property.Create<SoundSource?>();

    protected override SoundSource? GetSoundSource()
    {
        return Source.CurrentValue;
    }
}
