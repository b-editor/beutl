using Beutl.AgentToolkit.Schema;

namespace Beutl.AgentToolkit.Tools;

public sealed record GetExamplesResponse(
    string SchemaVersion,
    IReadOnlyList<DeclarativeExample> Examples,
    string SelectionHint);
