using Beutl.AgentToolkit.Sessions;

namespace Beutl.AgentToolkit.Tools;

public sealed record CreativeDirectionResponse(
    string SchemaVersion,
    IReadOnlyList<string> DirectionAxes,
    IReadOnlyList<CreativeInspirationSeed> InspirationSeeds,
    IReadOnlyList<string> CombinationRules,
    IReadOnlyList<string> OriginalityConstraints,
    IReadOnlyList<string> VariationPrompts,
    IReadOnlyList<string> OverusedMotifs,
    IReadOnlyList<string> WorkflowHints,
    IReadOnlyList<string> StyleGuardrails,
    IReadOnlyList<string> PaletteGuidelines,
    IReadOnlyList<string> TypographyGuidelines,
    IReadOnlyList<string> MotionGuidelines,
    IReadOnlyList<CreativeDirectionFingerprint> RecentToAvoid,
    string SelectionHint,
    CreativeDirectionSelectionTrace? SelectionTrace = null);
