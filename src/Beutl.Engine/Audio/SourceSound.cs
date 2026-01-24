using System.ComponentModel.DataAnnotations;
using Beutl.Engine;
using Beutl.Language;
using Beutl.Media.Source;

namespace Beutl.Audio;

[Display(Name = nameof(Strings.Sound), ResourceType = typeof(Strings))]
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
