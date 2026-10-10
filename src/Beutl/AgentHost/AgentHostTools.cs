using System.ComponentModel;
using Avalonia.Threading;
using Beutl.AgentToolkit.Common;
using Beutl.AgentToolkit.Tools;
using Beutl.Services;
using Beutl.ViewModels;
using ModelContextProtocol.Server;

namespace Beutl.AgentHost;

public sealed record ListScenesResponse(
    string? ProjectPath, string? ActiveSceneId, IReadOnlyList<SceneSummary> Scenes);

[McpServerToolType]
public sealed class AgentHostTools(
    ProjectService projects, EditorService editors, EditorProjectSessionGateway gateway) : ToolBase
{
    [McpServerTool(Name = "list_scenes")]
    [Description("Lists scenes available in this Beutl instance without changing the editor selection. Pass a returned sceneId on each document, edit, history or render call. Each scene call resolves its own target while preserving the visible editor selection; no attach or prior call is required. instanceId selects the Beutl process independently.")]
    public ToolResult<ListScenesResponse> ListScenes()
        => Execute(() => Dispatcher.UIThread.Invoke(() => new ListScenesResponse(
            projects.CurrentProject.Value?.Uri?.LocalPath,
            (editors.SelectedTabItem.Value?.Context.Value as EditViewModel)?.Scene?.Id.ToString(),
            gateway.GetScenes().Select(scene => new SceneSummary(
                scene.Id.ToString(), scene.Name, scene.FrameSize.Width, scene.FrameSize.Height,
                scene.Start.ToString("c"), scene.Duration.ToString("c"), scene.Children.Count)).ToArray())));
}
