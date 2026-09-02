using Beutl.AgentToolkit.Schema;

namespace Beutl.AgentToolkit.Tools;

public sealed record ListCompositionsResponse(
    string SchemaVersion,
    string Seed,
    IReadOnlyList<CompositionTemplateSummary> Compositions,
    IReadOnlyList<string> RecentlyUsedCompositions,
    IReadOnlyList<string> PreAttachPreviewedCompositions,
    bool PreviewOnly,
    string SelectionHint);
