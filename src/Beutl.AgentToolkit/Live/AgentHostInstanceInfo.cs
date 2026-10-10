namespace Beutl.AgentToolkit.Live;

public sealed record AgentHostInstanceInfo(
    string InstanceId, int ProcessId, string? ProjectName, string? ProjectPath,
    string? SceneId, string? SceneName, string WorkspaceRoot);

// ConnectedInstanceId is the process that receives calls without an explicit instanceId: the
// host itself on a direct connection, or the broker's current target (null while none runs).
public sealed record ListAgentHostInstancesResponse(
    string? ConnectedInstanceId, IReadOnlyList<AgentHostInstanceInfo> Instances);
