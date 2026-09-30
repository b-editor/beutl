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
        "Call read_document_summary for element handles, or read_document for the editable scene data.",
        "Call get_schema(type=...) for the properties needed by the edit; catalog enumeration is optional. Supported building blocks can be combined without a named recipe.",
        "Use apply_edit for Id-based patches. Choose natural content-based names and compact ZIndex values; PortalObject.Count is a relative layer span.",
        "Use measure_object_bounds for coordinates and transforms, validate_shader for compilation, and render_still/render_storyboard for rendered evidence. Runtime editing does not require Beutl source code.",
        "Use export_video for the requested output. Tool success confirms the operation only; Beutl does not decide creative direction, visual quality, or task completion."
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
