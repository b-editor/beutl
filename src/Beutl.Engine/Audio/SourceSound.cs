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

    [Display(Name = nameof(Strings.Source), ResourceType = typeof(Strings))]
    public IProperty<SoundSource?> Source { get; } = Property.Create<SoundSource?>();

    public override bool HasOriginalLength()
    {
        return Source.CurrentValue != null;
    }

    public override bool TryGetOriginalLength(out TimeSpan timeSpan)
    {
        using var resource = Source.CurrentValue?.ToResource(RenderContext.Default);
        if (resource != null)
        {
            timeSpan = resource.Duration;
            return true;
        }
        else
        {
            timeSpan = TimeSpan.Zero;
            return false;
        }
    }

    public override void OnSplit(bool backward, TimeSpan startDelta, TimeSpan lengthDelta)
    {
        if (backward)
        {
            OffsetPosition.CurrentValue += startDelta;
        }
    }

    protected override SoundSource? GetSoundSource()
    {
        return Source.CurrentValue;
    }
}
