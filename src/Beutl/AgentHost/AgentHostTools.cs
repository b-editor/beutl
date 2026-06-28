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
        "LiveEditor sessions edit the open scene. Persist live edits with the Beutl editor UI; create_project, open_project, and save_project create or persist a file-backed session.";

    public IReadOnlyList<string> NextSteps { get; } =
    [
        "Call read_document_summary to observe the scene without pulling the full document.",
        "For vague or no-context motion graphics, call list_creative_directions and choose a concept before authoring.",
        "Call list_effects and list_effect_recipes to discover Beutl visual effects before settling on a repeated look.",
        "Build original scenes with plan_edit/apply_edit; call list_compositions only when the user explicitly asks for a template, starter, or named composition style.",
        "Call list_examples to choose a compact declarative snippet only when you need a targeted patch.",
        "Call get_schema with includeProperties/includeExamples filters for detailed discovery.",
        "Call read_document when you need the normalized declarative scene.",
        "Call plan_edit with a patch or desired document.",
        "Call apply_edit with plan_edit.expectedChangeSet.",
        "Use apply_edit.createdIds or read_document to get new Ids before follow-up edits.",
        "Use render_still, evaluate_motion_variation, and export_video for workspace outputs."
    ];
}

[McpServerToolType]
public sealed class AgentHostTools(
    EditorService editorService,
    LiveSessionSource liveSessions,
    AgentSessionManager sessions) : ToolBase
{
    [McpServerTool(Name = "attach_active_editor")]
    [Description("Attaches the toolkit to the active editor tab so read_document, plan_edit, apply_edit, render_still, and export_video can operate on the live scene and history.")]
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
                    "Open or create a project/scene in the Beutl editor and call attach_active_editor again, or call create_project/open_project to start a file-backed session."));
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
