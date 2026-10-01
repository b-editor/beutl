using Beutl.Media.Source;

namespace Beutl.NodeGraph.Generative;

/// <summary>
/// One finished generation of a <see cref="GenerativeNode"/>: the file it produced and
/// everything needed to tell whether the node's current inputs would ask for it again.
/// </summary>
/// <remarks>
/// Records are part of the project, so undoing a generation or picking an older one
/// is an ordinary edit, and the result renders without the network.
/// </remarks>
public sealed class GenerationRecord : Hierarchical
{
    public static readonly CoreProperty<ImageSource?> ImageProperty;
    public static readonly CoreProperty<VideoSource?> VideoProperty;
    public static readonly CoreProperty<string> FingerprintProperty;
    public static readonly CoreProperty<string> ParameterFingerprintProperty;
    public static readonly CoreProperty<string?> ModelIdProperty;
    public static readonly CoreProperty<int?> SeedProperty;
    public static readonly CoreProperty<string?> SummaryProperty;
    public static readonly CoreProperty<DateTimeOffset> CreatedAtProperty;
    public static readonly CoreProperty<bool> IsPinnedProperty;

    static GenerationRecord()
    {
        ImageProperty = ConfigureProperty<ImageSource?, GenerationRecord>(nameof(Image))
            .Accessor(o => o.Image, (o, v) => o.Image = v)
            .Register();
        VideoProperty = ConfigureProperty<VideoSource?, GenerationRecord>(nameof(Video))
            .Accessor(o => o.Video, (o, v) => o.Video = v)
            .Register();
        FingerprintProperty = ConfigureProperty<string, GenerationRecord>(nameof(Fingerprint))
            .Accessor(o => o.Fingerprint, (o, v) => o.Fingerprint = v)
            .DefaultValue(string.Empty)
            .Register();
        ParameterFingerprintProperty = ConfigureProperty<string, GenerationRecord>(nameof(ParameterFingerprint))
            .Accessor(o => o.ParameterFingerprint, (o, v) => o.ParameterFingerprint = v)
            .DefaultValue(string.Empty)
            .Register();
        ModelIdProperty = ConfigureProperty<string?, GenerationRecord>(nameof(ModelId))
            .Accessor(o => o.ModelId, (o, v) => o.ModelId = v)
            .Register();
        SeedProperty = ConfigureProperty<int?, GenerationRecord>(nameof(Seed))
            .Accessor(o => o.Seed, (o, v) => o.Seed = v)
            .Register();
        SummaryProperty = ConfigureProperty<string?, GenerationRecord>(nameof(Summary))
            .Accessor(o => o.Summary, (o, v) => o.Summary = v)
            .Register();
        CreatedAtProperty = ConfigureProperty<DateTimeOffset, GenerationRecord>(nameof(CreatedAt))
            .Accessor(o => o.CreatedAt, (o, v) => o.CreatedAt = v)
            .Register();
        IsPinnedProperty = ConfigureProperty<bool, GenerationRecord>(nameof(IsPinned))
            .Accessor(o => o.IsPinned, (o, v) => o.IsPinned = v)
            .Register();
    }

    /// <summary>The generated picture, stored as a file next to the project.</summary>
    public ImageSource? Image
    {
        get;
        set => SetAndRaise(ImageProperty, ref field, value);
    }

    /// <summary>The generated clip, for a video generation.</summary>
    public VideoSource? Video
    {
        get;
        set => SetAndRaise(VideoProperty, ref field, value);
    }

    /// <summary>Identifies the complete request, reference pictures included.</summary>
    public string Fingerprint
    {
        get;
        set => SetAndRaise(FingerprintProperty, ref field, value);
    } = string.Empty;

    /// <summary>
    /// Identifies the scalar inputs only. Cheap enough to compare every frame, so it is
    /// what the stale indicator uses; <see cref="Fingerprint"/> decides at queue time.
    /// </summary>
    public string ParameterFingerprint
    {
        get;
        set => SetAndRaise(ParameterFingerprintProperty, ref field, value);
    } = string.Empty;

    public string? ModelId
    {
        get;
        set => SetAndRaise(ModelIdProperty, ref field, value);
    }

    public int? Seed
    {
        get;
        set => SetAndRaise(SeedProperty, ref field, value);
    }

    /// <summary>A short description of what was asked for, shown in the history.</summary>
    public string? Summary
    {
        get;
        set => SetAndRaise(SummaryProperty, ref field, value);
    }

    public DateTimeOffset CreatedAt
    {
        get;
        set => SetAndRaise(CreatedAtProperty, ref field, value);
    }

    /// <summary>A pinned generation is kept when older ones are pruned.</summary>
    public bool IsPinned
    {
        get;
        set => SetAndRaise(IsPinnedProperty, ref field, value);
    }
}
