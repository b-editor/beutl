using System.ComponentModel;
using Avalonia.Threading;
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
        "LiveEditor sessions edit the open scene; persist live edits with the Beutl editor UI. In this in-app host, open_project/create_project also open the project in the editor (single open project) and return a LiveEditor session, so save_project is not required.";

    public IReadOnlyList<string> NextSteps { get; } =
    [
        "Call read_document_summary to observe the scene without pulling the full document.",
        "Decide the intended visual result and motion from the brief first. list_creative_directions offers optional stimulus when useful.",
        "Choose existing features, compositions, or custom scripts to reproduce that intent; the user does not need to name a tool or language. Resolve each concrete implementation question with targeted discovery, then author and render the next edit.",
        "Effect, object, and recipe catalogs describe building blocks, not the limit of possible expressions. Use get_schema(type=...) for unfamiliar types; use a category or intent-filtered list only when the needed type is unknown, without enumerating catalogs before every edit.",
        "If no recipe matches, compose supported geometry, masks, transforms, keyframes, and effects, or author a custom script effect. Validate custom scripts with validate_shader and verify a small prototype with render_still before expanding it.",
        "Build original scenes with apply_edit; call list_compositions only when the user explicitly asks for a template, starter, or named composition style.",
        "Call get_examples with a known name for a targeted syntax snippet; use list_examples filtered by type or category only when you need to find one.",
        "Call get_schema with includeProperties/includeExamples filters for detailed discovery.",
        "Call read_document when you need the normalized declarative scene.",
        "Call apply_edit with a patch or desired document.",
        "Use apply_edit.createdIds or read_document to get new Ids before follow-up edits.",
        "Use evaluate_edit_quality(staticLayout:true) during authoring, final_preflight before export, and export_video only after critical/major quality blockers are resolved."
    ];
}

[McpServerToolType]
public sealed class AgentHostTools(
    EditorService editorService,
    LiveSessionSource liveSessions,
    AgentSessionManager sessions) : ToolBase
{
    [McpServerTool(Name = "attach_active_editor")]
    [Description("Attaches the toolkit to the active editor tab so read_document, apply_edit, render_still, and export_video can operate on the live scene and history.")]
    public ToolResult<AttachActiveEditorResponse> AttachActiveEditor()
    {
        return Execute(() =>
        {
            // SelectedTabItem, EditViewModel.Scene, and scene.Children are Avalonia/editor-owned; the
            // Kestrel request thread must not read them or it races a tab switch and trips thread
            // affinity. Run the whole attach + summary on the UI thread.
            return Dispatcher.UIThread.Invoke(() =>
            {
                if (editorService.SelectedTabItem.Value?.Context.Value is not EditViewModel editViewModel)
                {
                    throw new ReconcileException(new ToolError(
                        ErrorCode.NoActiveEditorSession,
                        "No active Beutl editor scene is available.",
                        null,
                        "Open or create a project/scene in the Beutl editor and call attach_active_editor again, or call create_project/open_project — in this host they open the project in the editor and return a live session."));
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
        });
    }
}
