using System.Runtime.CompilerServices;
using Beutl.Extensibility;

namespace Beutl.NodeGraph.Generative;

/// <summary>Which list a generative node's text input picks from.</summary>
public enum GenerativeChoiceKind
{
    Model,
    AspectRatio,
    Background,
    Resolution,
    Duration,
}

/// <summary>Marks a node input as a choice from the model catalog.</summary>
public sealed record GenerativeChoice(GenerativeNode Node, GenerativeChoiceKind Kind);

/// <summary>
/// Looks up which inputs are catalog choices. Node inputs share one adapter type, so the
/// editor finds the choice through the adapter rather than through its type.
/// </summary>
public static class GenerativeChoices
{
    private static readonly ConditionalWeakTable<IPropertyAdapter, GenerativeChoice> s_choices = [];

    public static void Register(IPropertyAdapter adapter, GenerativeChoice choice)
        => s_choices.AddOrUpdate(adapter, choice);

    public static bool TryGet(IPropertyAdapter adapter, [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out GenerativeChoice? choice)
        => s_choices.TryGetValue(adapter, out choice);
}

/// <summary>What one image model takes. A null list means the model publishes none.</summary>
public sealed record GenerativeImageCapabilities(
    IReadOnlyList<string>? AspectRatios,
    IReadOnlyList<string>? Backgrounds,
    bool SupportsSeed,
    int MaxReferenceImages)
{
    /// <summary>What the AI dialog offers for a model that publishes no aspect ratios.</summary>
    public static IReadOnlyList<string> DefaultAspectRatios { get; } =
        ["16:9", "1:1", "9:16", "4:3", "3:4", "3:2", "2:3"];

    /// <summary>What the AI dialog offers for a model that publishes no backgrounds.</summary>
    public static IReadOnlyList<string> DefaultBackgrounds { get; } = ["auto", "opaque", "transparent"];

    public IReadOnlyList<string> AspectRatioChoices => AspectRatios ?? DefaultAspectRatios;

    public IReadOnlyList<string> BackgroundChoices => Backgrounds ?? DefaultBackgrounds;
}

/// <summary>One model offered for an operation, labelled as the AI dialog labels it.</summary>
public sealed record GenerativeModelInfo(
    string Id,
    string Label,
    bool IsDefault,
    bool IsAvailable,
    GenerativeImageCapabilities? Image,
    GenerativeVideoCapabilities? Video = null);

/// <summary>What one video model takes. A null list means the model publishes none.</summary>
public sealed record GenerativeVideoCapabilities(
    IReadOnlyList<int>? DurationsSeconds,
    IReadOnlyList<string>? Resolutions,
    IReadOnlyList<string>? AspectRatios,
    bool SupportsAudio,
    bool SupportsSeed,
    bool SupportsFirstFrame,
    bool SupportsLastFrame,
    bool SupportsPromptToVideo,
    bool SupportsInputReferences,
    int MaxImageReferences,
    long MaxImageReferenceBytes,
    int MaxVideoReferences,
    long MaxVideoReferenceBytes,
    int MaxPromptLength)
{
    /// <summary>What the AI tab offers for a model that publishes none, and its starting choice.</summary>
    public static IReadOnlyList<int> DefaultDurations { get; } = [4, 6, 8];

    public const int DefaultDuration = 6;

    public static IReadOnlyList<string> DefaultResolutions { get; } = ["720p", "1080p"];

    public static IReadOnlyList<string> DefaultAspectRatios { get; } = ["16:9", "9:16"];

    public static GenerativeVideoCapabilities Unrestricted { get; } =
        new(null, null, null, true, true, true, true, true, false, 0, 0, 0, 0, int.MaxValue);

    public IReadOnlyList<int> DurationChoices => DurationsSeconds ?? DefaultDurations;

    public IReadOnlyList<string> ResolutionChoices => Resolutions ?? DefaultResolutions;

    public IReadOnlyList<string> AspectRatioChoices => AspectRatios ?? DefaultAspectRatios;
}

/// <summary>The models the editor can offer to generative nodes. Implemented by the application.</summary>
public interface IGenerativeModelCatalog
{
    /// <summary>
    /// The models for an operation such as <c>image.generate</c> or <c>image.edit.upscale</c>;
    /// empty when the server offers none or cannot be reached.
    /// </summary>
    Task<IReadOnlyList<GenerativeModelInfo>> GetModelsAsync(
        string operationId,
        CancellationToken cancellationToken);
}
