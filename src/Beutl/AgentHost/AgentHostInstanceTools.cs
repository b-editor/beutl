using System.ComponentModel;
using Beutl.AgentToolkit.Common;
using Beutl.AgentToolkit.Live;
using ModelContextProtocol.Server;

namespace Beutl.AgentHost;

[McpServerToolType]
internal sealed class AgentHostInstanceTools(AgentHostInstanceRouter router)
{
    [McpServerTool(Name = "list_instances", ReadOnly = true)]
    [Description("Lists running Beutl instances in this profile with their instanceId, PID, project and active scene. Choose the requested instance and pass its instanceId on every subsequent tool call, including list_scenes/open_project/create_project, reads, edits, history, rendering and job polling. Omitting instanceId operates on the connected instance; there is no shared selected instance.")]
    public async Task<ToolResult<ListAgentHostInstancesResponse>> ListInstances(CancellationToken cancellationToken)
        => ToolResult<ListAgentHostInstancesResponse>.Success(await router.ListAsync(cancellationToken).ConfigureAwait(false));
}
