using System.ComponentModel;
using Beutl.AgentToolkit.Common;
using Beutl.AgentToolkit.Reconciliation;
using Beutl.AgentToolkit.Sessions;
using Beutl.AgentToolkit.Tools;
using Beutl.ProjectSystem;
using Beutl.Services;
using Beutl.ViewModels;
using ModelContextProtocol.Server;

namespace Beutl.AgentHost;

public sealed record AgentHostSceneSummary(string SceneId, string Name, int Width, int Height, string Duration, int Elements);

public sealed record AttachActiveEditorResponse(string Session, string Source, AgentHostSceneSummary Summary)
{
    public string Persistence =>
        "LiveEditor sessions edit the open scene. Persist with the Beutl editor UI; create_project and save_project are available only in the stdio MCP host.";

    public IReadOnlyList<string> NextSteps { get; } =
    [
        "Call read_document_summary to observe the scene without pulling the full document.",
        "Call list_effects and list_effect_recipes to discover Beutl visual effects before settling on a repeated look.",
        "After attach_active_editor succeeds, call list_compositions without seed for a session-varied composition order; for no-context motion graphics, use the first returned composition and do not re-rank from memory.",
        "Call list_examples to choose a compact declarative snippet only when composition tools are unavailable or you need a targeted patch.",
        "Call get_schema with includeProperties/includeExamples filters for detailed discovery.",
        "Call read_document when you need the normalized declarative scene.",
        "Call plan_edit with a patch or desired document.",
        "Call apply_edit with plan_edit.expectedChangeSet.",
        "Use apply_edit's returned document or read_document to get minted Ids before follow-up edits.",
        "Use render_still or export_video for workspace outputs."
    ];
}

[McpServerToolType]
public sealed class AgentHostTools(
    EditorService editorService,
    LiveSessionSource liveSessions,
    AgentSessionManager sessions) : ToolBase
{
    [McpServerTool(Name = "attach_active_editor")]
    [Description("Attaches the toolkit to the active editor tab so read_document, plan_edit, apply_edit, render_still, and export_video can operate on the live scene and history. Live mode does not expose create_project or save_project; use the editor UI for persistence.")]
    public ToolResult<AttachActiveEditorResponse> AttachActiveEditor()
    {
        return Execute(() =>
        {
            if (editorService.SelectedTabItem.Value?.Context.Value is not EditViewModel editViewModel)
            {
                throw new ReconcileException(new ToolError(
                    ErrorCode.NoActiveEditorSession,
                    "No active Beutl editor scene is available.",
                    null,
                    "Open or create a project/scene in the Beutl editor first, then call attach_active_editor again. In live mode, project creation and saving are currently UI operations."));
            }

            LiveEditingSession session = liveSessions.Attach(new EditViewModelLiveBinding(editViewModel));
            sessions.UseSource(liveSessions);
            Scene scene = editViewModel.Scene;
            return new AttachActiveEditorResponse(
                session.SessionId,
                session.Source.ToString(),
                new AgentHostSceneSummary(
                    scene.Id.ToString(),
                    scene.Name,
                    scene.FrameSize.Width,
                    scene.FrameSize.Height,
                    scene.Duration.ToString("c"),
                    scene.Children.Count));
        });
    }
}
