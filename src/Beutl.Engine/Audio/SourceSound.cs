using System.ComponentModel.DataAnnotations;
using Beutl.Composition;
using Beutl.Engine;
using Beutl.Language;
using Beutl.Media.Source;

namespace Beutl.Audio;

[Display(Name = nameof(AudioStrings.SourceSound), ResourceType = typeof(AudioStrings))]
public sealed partial class SourceSound : Sound, IOriginalDurationProvider, ISplittable
{
    public SourceSound()
    {
        ScanProperties<SourceSound>();
    }

    [Display(Name = nameof(AudioStrings.SourceSound_Source), ResourceType = typeof(AudioStrings))]
    public IExpressionProperty<SoundSource?> Source { get; } = Property.CreateExpression<SoundSource?>();

    public bool HasOriginalDuration()
    {
        return Source.Value != null;
    }

    public bool TryGetOriginalDuration(out MediaTime timeSpan)
    {
        using var resource = Source.Value?.ToResource(CompositionContext.Default);
        if (resource != null && resource.Duration > MediaTime.Zero)
        {
            timeSpan = resource.Duration;
            return true;
        }
        else
        {
            timeSpan = MediaTime.Zero;
            return false;
        }
    }

    public void NotifySplitted(bool backward, MediaTime startDelta, MediaTime durationDelta)
    {
        if (backward)
        {
            OffsetPosition.Value += startDelta;
        }
    }

    public partial class Resource
    {
        public override SoundSource.Resource? GetSoundSource() => Source;
    }
}
