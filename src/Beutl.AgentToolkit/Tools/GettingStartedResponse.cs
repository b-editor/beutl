using Beutl.AgentToolkit.Design;

namespace Beutl.AgentToolkit.Tools;

public sealed record GettingStartedResponse(
    string SchemaVersion,
    IReadOnlyList<string> RecommendedCalls,
    IReadOnlyList<RecommendedSkill> RecommendedSkills,
    IReadOnlyDictionary<string, string> CategoryAliases,
    string RawHttpNote,
    IReadOnlyList<VideoTypeSummary>? VideoTypes = null,
    VideoTypeSummary? SelectedVideoType = null);
