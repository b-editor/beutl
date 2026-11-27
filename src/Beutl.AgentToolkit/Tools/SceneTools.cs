using System.ComponentModel;
using Beutl.AgentToolkit.Common;
using Beutl.AgentToolkit.Reconciliation;
using Beutl.AgentToolkit.Sessions;
using Beutl.Services;
using ModelContextProtocol.Server;

namespace Beutl.AgentToolkit.Tools;

public sealed record RemoveSceneResponse(string SceneId);

[McpServerToolType]
public sealed class SceneTools(IProjectSessionGateway projects) : ToolBase
{
    internal const string RemoveSceneToolName = "remove_scene";

    [McpServerTool(Name = RemoveSceneToolName)]
    [Description("Removes a scene from the current project and keeps its file. The last scene and scenes with unsaved edits are refused. The live editor persists the removal immediately. In a file session, call save_project to persist it.")]
    public ValueTask<ToolResult<RemoveSceneResponse>> RemoveScene(
        [Description("ID of the scene to remove, from list_scenes or the project session summary.")]
        string sceneId,
        CancellationToken cancellationToken = default)
        => ExecuteAsync(async () =>
        {
            if (!Guid.TryParse(sceneId, out Guid id) || id == Guid.Empty)
                throw new ReconcileException(new ToolError(ErrorCode.ValidationRejected, "Supply a valid scene ID.", nameof(sceneId)));

            if (projects is not IProjectSceneRemovalGateway removal)
                throw new SessionUnavailableException();

            SceneRemovalResult result = await removal.RemoveSceneAsync(id, cancellationToken).ConfigureAwait(false);
            return result switch
            {
                SceneRemovalResult.Removed => new RemoveSceneResponse(id.ToString()),
                SceneRemovalResult.SceneNotFound => throw new ReconcileException(new ToolError(
                    ErrorCode.StaleHandle, "The scene is no longer in the current project.", sceneId)),
                SceneRemovalResult.LastScene => throw new ReconcileException(new ToolError(
                    ErrorCode.ValidationRejected, "The last scene in a project cannot be removed.", sceneId)),
                SceneRemovalResult.UnsavedChanges => throw new ReconcileException(new ToolError(
                    ErrorCode.ValidationRejected, "Save this scene's changes before removing it.", sceneId)),
                _ => throw new ArgumentOutOfRangeException(nameof(result))
            };
        });
}
