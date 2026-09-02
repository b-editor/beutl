using Beutl.AgentToolkit.Schema;

namespace Beutl.AgentToolkit.Tools;

public sealed record ListExamplesResponse(
    string SchemaVersion,
    IReadOnlyList<DeclarativeExampleSummary> Examples,
    string SelectionHint);
