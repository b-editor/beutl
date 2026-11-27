using System.Text.Json.Serialization;

namespace Beutl.Agents;

[JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
[JsonDerivedType(typeof(StdioMcpServerSpec), "stdio")]
[JsonDerivedType(typeof(HttpMcpServerSpec), "http")]
public abstract record McpServerSpec(string Name);

public sealed record StdioMcpServerSpec(
    string Name, string Command, IReadOnlyList<string>? Args = null, IReadOnlyDictionary<string, string>? Env = null)
    : McpServerSpec(Name);

public sealed record HttpMcpServerSpec(string Name, Uri Url, IReadOnlyDictionary<string, string>? Headers = null)
    : McpServerSpec(Name);

public sealed record AgentAttachedFile(string Path, string? MediaType = null);

public enum AgentContextMentionKind { Scene, Element, File, Object }

public sealed record AgentContextMention(
    AgentContextMentionKind Kind,
    string Name,
    string? TargetId = null,
    string? Path = null,
    string? Detail = null,
    string? Token = null,
    string? SceneId = null,
    string? ElementId = null,
    string? TypeName = null,
    double? StartSeconds = null,
    double? EndSeconds = null);

public sealed record AgentContextSnapshot(
    string InstanceId,
    string ProjectPath,
    string ProjectName,
    string? SceneId,
    string? SceneName,
    int? FrameWidth,
    int? FrameHeight,
    double? FrameRate,
    double? DurationSeconds,
    double? PlayheadSeconds,
    IReadOnlyList<string> SelectedElementIds,
    IReadOnlyList<AgentAttachedFile> AttachedFiles,
    IReadOnlyList<AgentContextMention>? Mentions = null);
