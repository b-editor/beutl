using Beutl.AgentToolkit.Schema;

namespace Beutl.AgentToolkit.Tools;

public sealed record ListEffectsResponse(
    string SchemaVersion,
    IReadOnlyList<EffectSummary> Effects,
    string SelectionHint);
