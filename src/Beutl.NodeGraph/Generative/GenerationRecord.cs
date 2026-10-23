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
public sealed partial class GenerationRecord : Hierarchical
{
    public GenerationRecord()
    {
        ScanProperties<GenerationRecord>();
    }

    // The sources are not made children, as their CoreProperty forms did not make them.
    /// <summary>The generated picture, stored as a file next to the project.</summary>
    public IProperty<ImageSource?> Image { get; } = new ValueOnlyProperty<ImageSource?>(null) { OwnsValue = false };

    /// <summary>The generated clip, for a video generation.</summary>
    public IProperty<VideoSource?> Video { get; } = new ValueOnlyProperty<VideoSource?>(null) { OwnsValue = false };

    /// <summary>Identifies the complete request, reference pictures included.</summary>
    public IProperty<string> Fingerprint { get; } = Property.CreateValue(string.Empty);

    /// <summary>
    /// Identifies the scalar inputs only. Cheap enough to compare every frame, so it is
    /// what the stale indicator uses; <see cref="Fingerprint"/> decides at queue time.
    /// </summary>
    public IProperty<string> ParameterFingerprint { get; } = Property.CreateValue(string.Empty);

    public IProperty<string?> ModelId { get; } = Property.CreateValue<string?>(null);

    public IProperty<int?> Seed { get; } = Property.CreateValue<int?>(null);

    /// <summary>A short description of what was asked for, shown in the history.</summary>
    public IProperty<string?> Summary { get; } = Property.CreateValue<string?>(null);

    public IProperty<DateTimeOffset> CreatedAt { get; } = Property.CreateValue<DateTimeOffset>(default);

    /// <summary>A pinned generation is kept when older ones are pruned.</summary>
    public IProperty<bool> IsPinned { get; } = Property.CreateValue(false);
}
