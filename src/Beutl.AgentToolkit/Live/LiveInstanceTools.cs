using System.ComponentModel;
using Beutl.AgentToolkit.Common;
using ModelContextProtocol.Server;

namespace Beutl.AgentToolkit.Live;

[McpServerToolType]
public sealed class LiveInstanceTools(LiveMcpBroker broker)
{
    [McpServerTool(Name = LiveMcpBroker.ListInstancesToolName, ReadOnly = true)]
    [Description("Lists the Beutl editors running in this profile with their instanceId, PID, project and active scene. To work in one of them, pass its instanceId on every call (list_scenes, reads, edits, history, rendering and job polling); scene operations there also take sceneId. Calls without instanceId run headlessly on project files in the workspace instead.")]
    public Task<ToolResult<ListAgentHostInstancesResponse>> ListInstances(CancellationToken cancellationToken)
        => broker.ListInstancesAsync(cancellationToken);
}
