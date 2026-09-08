namespace Beutl.AgentToolkit.Tools;

public sealed record ObjectSummary(
    string Id,
    string Name,
    string Type,
    string Discriminator,
    IReadOnlyList<string> AnimatedProperties,
    IReadOnlyList<string> ExpressionProperties,
    IReadOnlyList<string> BrushProperties,
    IReadOnlyList<string> EffectProperties,
    IReadOnlyList<string> NestedAnimatedProperties,
    bool IsFallback = false,
    string? FallbackReason = null,
    string? FallbackTypeName = null,
    string? FallbackMessage = null);
