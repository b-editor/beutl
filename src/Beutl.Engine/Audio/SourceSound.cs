using System.ComponentModel.DataAnnotations;
using Beutl.Composition;
using Beutl.Engine;
using Beutl.Language;
using Beutl.Media.Source;

namespace Beutl.Audio;

[Display(Name = nameof(Strings.Sound), ResourceType = typeof(Strings))]
public sealed partial class SourceSound : Sound, IOriginalDurationProvider, ISplittable
{
    public SourceSound()
    {
        ScanProperties<SourceSound>();
    }

    [Display(Name = nameof(Strings.Source), ResourceType = typeof(Strings))]
    public IProperty<SoundSource?> Source { get; } = Property.Create<SoundSource?>();

    public bool HasOriginalDuration()
    {
        return Source.CurrentValue != null;
    }

    public bool TryGetOriginalDuration(out TimeSpan timeSpan)
    {
        using var resource = Source.CurrentValue?.ToResource(CompositionContext.Default);
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

    public void NotifySplitted(bool backward, TimeSpan startDelta, TimeSpan durationDelta)
    {
        if (backward)
        {
            OffsetPosition.CurrentValue += startDelta;
        }
    }

    public partial class Resource
    {
        public override SoundSource.Resource? GetSoundSource() => Source;
    }
}
