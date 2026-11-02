using System.ComponentModel.DataAnnotations;
using Beutl.Composition;
using Beutl.Engine;
using Beutl.Language;
using Beutl.Media.Source;

namespace Beutl.Audio;

[Display(Name = nameof(AudioStrings.SourceSound), ResourceType = typeof(AudioStrings))]
public sealed partial class SourceSound : Sound, IOriginalDurationProvider, ITimeMappedSource
{
    public SourceSound()
    {
        ScanProperties<SourceSound>();
    }

    [Display(Name = nameof(AudioStrings.SourceSound_Source), ResourceType = typeof(AudioStrings))]
    public IExpressionProperty<SoundSource?> Source { get; } = Property.CreateExpression<SoundSource?>();

    [Display(Name = nameof(AudioStrings.SourceSound_ChannelMap),
        Description = nameof(AudioStrings.SourceSound_ChannelMap_Description), ResourceType = typeof(AudioStrings))]
    public IExpressionProperty<AudioChannelMap> ChannelMap { get; } = Property.CreateExpression(AudioChannelMap.Auto);

    public override AudioChannelLayout GetClipLayout(AudioChannelLayout projectLayout, MediaTime time)
    {
        var context = new CompositionContext(time);
        using var source = Source.Evaluate(context)?.ToResource(context);
        return source?.ReadableChannelLayout is { } layout
            ? ChannelMap.Evaluate(context).ResolveClipLayout(layout)
            : projectLayout;
    }

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

    // An expression on Source hides only the file's length.
    bool ITimeMappedSource.HasDynamicTimeline => Source.HasExpression;

    MediaTime? ITimeMappedSource.GetSourceDuration()
        => TryGetOriginalDuration(out MediaTime duration) ? duration : null;

    public partial class Resource
    {
        public override SoundSource.Resource? GetSoundSource() => Source;

        public override AudioChannelMap GetChannelMap() => ChannelMap;
    }
}
