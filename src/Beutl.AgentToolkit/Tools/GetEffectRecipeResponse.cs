using Beutl.AgentToolkit.Schema;

namespace Beutl.AgentToolkit.Tools;

public sealed record GetEffectRecipeResponse(
    string SchemaVersion,
    EffectRecipe Recipe,
    string UsageHint);
