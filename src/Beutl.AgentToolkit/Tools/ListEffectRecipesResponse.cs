using Beutl.AgentToolkit.Schema;

namespace Beutl.AgentToolkit.Tools;

public sealed record ListEffectRecipesResponse(
    string SchemaVersion,
    IReadOnlyList<EffectRecipeSummary> Recipes,
    string SelectionHint);
