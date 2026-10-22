using System.ComponentModel;
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
    public GenerationRecord()
    {
        ScanProperties<GenerationRecord>();
        ImageProperty.ValueChanged += (_, _) => OnPropertyChanged(new PropertyChangedEventArgs(nameof(Image)));
        VideoProperty.ValueChanged += (_, _) => OnPropertyChanged(new PropertyChangedEventArgs(nameof(Video)));
        FingerprintProperty.ValueChanged += (_, _) => OnPropertyChanged(new PropertyChangedEventArgs(nameof(Fingerprint)));
        ParameterFingerprintProperty.ValueChanged += (_, _) => OnPropertyChanged(new PropertyChangedEventArgs(nameof(ParameterFingerprint)));
        ModelIdProperty.ValueChanged += (_, _) => OnPropertyChanged(new PropertyChangedEventArgs(nameof(ModelId)));
        SeedProperty.ValueChanged += (_, _) => OnPropertyChanged(new PropertyChangedEventArgs(nameof(Seed)));
        SummaryProperty.ValueChanged += (_, _) => OnPropertyChanged(new PropertyChangedEventArgs(nameof(Summary)));
        CreatedAtProperty.ValueChanged += (_, _) => OnPropertyChanged(new PropertyChangedEventArgs(nameof(CreatedAt)));
        IsPinnedProperty.ValueChanged += (_, _) => OnPropertyChanged(new PropertyChangedEventArgs(nameof(IsPinned)));
    }

    // The sources are not made children, as their CoreProperty forms did not make them.
    public IProperty<ImageSource?> ImageProperty { get; } = new ValueOnlyProperty<ImageSource?>(null) { OwnsValue = false };

    public IProperty<VideoSource?> VideoProperty { get; } = new ValueOnlyProperty<VideoSource?>(null) { OwnsValue = false };

    public IProperty<string> FingerprintProperty { get; } = new ValueOnlyProperty<string>(string.Empty);

    public IProperty<string> ParameterFingerprintProperty { get; } = new ValueOnlyProperty<string>(string.Empty);

    public IProperty<string?> ModelIdProperty { get; } = new ValueOnlyProperty<string?>(null);

    public IProperty<int?> SeedProperty { get; } = new ValueOnlyProperty<int?>(null);

    public IProperty<string?> SummaryProperty { get; } = new ValueOnlyProperty<string?>(null);

    public IProperty<DateTimeOffset> CreatedAtProperty { get; } = new ValueOnlyProperty<DateTimeOffset>(default);

    public IProperty<bool> IsPinnedProperty { get; } = new ValueOnlyProperty<bool>(false);

    /// <summary>The generated picture, stored as a file next to the project.</summary>
    public ImageSource? Image
    {
        get => ImageProperty.Value;
        set => ImageProperty.Value = value;
    }

    /// <summary>The generated clip, for a video generation.</summary>
    public VideoSource? Video
    {
        get => VideoProperty.Value;
        set => VideoProperty.Value = value;
    }

    /// <summary>Identifies the complete request, reference pictures included.</summary>
    public string Fingerprint
    {
        get => FingerprintProperty.Value;
        set => FingerprintProperty.Value = value;
    }

    /// <summary>
    /// Identifies the scalar inputs only. Cheap enough to compare every frame, so it is
    /// what the stale indicator uses; <see cref="Fingerprint"/> decides at queue time.
    /// </summary>
    public string ParameterFingerprint
    {
        get => ParameterFingerprintProperty.Value;
        set => ParameterFingerprintProperty.Value = value;
    }

    public string? ModelId
    {
        get => ModelIdProperty.Value;
        set => ModelIdProperty.Value = value;
    }

    public int? Seed
    {
        get => SeedProperty.Value;
        set => SeedProperty.Value = value;
    }

    /// <summary>A short description of what was asked for, shown in the history.</summary>
    public string? Summary
    {
        get => SummaryProperty.Value;
        set => SummaryProperty.Value = value;
    }

    public DateTimeOffset CreatedAt
    {
        get => CreatedAtProperty.Value;
        set => CreatedAtProperty.Value = value;
    }

    /// <summary>A pinned generation is kept when older ones are pruned.</summary>
    public bool IsPinned
    {
        get => IsPinnedProperty.Value;
        set => IsPinnedProperty.Value = value;
    }
}
