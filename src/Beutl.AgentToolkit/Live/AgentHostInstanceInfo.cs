namespace Beutl.AgentToolkit.Live;

public sealed record AgentHostInstanceInfo(
    string InstanceId, int ProcessId, string? ProjectName, string? ProjectPath,
    string? SceneId, string? SceneName);

// ConnectedInstanceId is the process that receives calls without an explicit instanceId: the host
// itself on a direct connection. Through the installed server it is always null, because a call
// without instanceId works headlessly on files and every live call names its target.
public sealed record ListAgentHostInstancesResponse(
    string? ConnectedInstanceId, IReadOnlyList<AgentHostInstanceInfo> Instances);
