using Beutl.AgentToolkit.Schema;

namespace Beutl.AgentToolkit.Tools;

public sealed record OriginalScaffoldResponse(
    string SchemaVersion,
    OriginalScaffold Scaffold,
    string UsageHint);
