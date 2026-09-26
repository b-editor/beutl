using System.Runtime.CompilerServices;
using Beutl.Extensibility;

namespace Beutl.NodeGraph.Generative;

/// <summary>Which list a generative node's text input picks from.</summary>
public enum GenerativeChoiceKind
{
    Model,
    AspectRatio,
    Background,
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
    GenerativeImageCapabilities? Image);

/// <summary>The models the editor can offer to generative nodes. Implemented by the application.</summary>
public interface IGenerativeModelCatalog
{
    /// <summary>The models for an operation; empty when the server offers none or cannot be reached.</summary>
    Task<IReadOnlyList<GenerativeModelInfo>> GetModelsAsync(
        GenerativeOperation operation,
        CancellationToken cancellationToken);
}
