using Beutl.AgentToolkit.Schema;

namespace Beutl.AgentToolkit.Tools;

public sealed record RenderCompositionPatchResponse(
    string SchemaVersion,
    CompositionRender Composition,
    string UsageHint);
