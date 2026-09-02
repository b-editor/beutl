using Beutl.AgentToolkit.Schema;

namespace Beutl.AgentToolkit.Tools;

public sealed record GetCompositionResponse(
    string SchemaVersion,
    CompositionTemplateDetail Composition);
