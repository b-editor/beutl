using System.ComponentModel;
using Beutl.AgentToolkit.Common;
using Beutl.AgentToolkit.Sessions;
using Beutl.AgentToolkit.Tools;
using Beutl.ProjectSystem;
using Beutl.Services;
using Beutl.ViewModels;
using ModelContextProtocol.Server;

namespace Beutl.AgentHost;

public sealed record AgentHostSceneSummary(string SceneId, string Name, int Width, int Height, string Duration, int Elements);

public sealed record AttachActiveEditorResponse(string Session, string Source, AgentHostSceneSummary Summary);

[McpServerToolType]
public sealed class AgentHostTools(
    EditorService editorService,
    LiveSessionSource liveSessions,
    AgentSessionManager sessions) : ToolBase
{
    [McpServerTool(Name = "attach_active_editor")]
    [Description("Attaches the toolkit to the active editor tab so edits apply to the live scene and history.")]
    public ToolResult<AttachActiveEditorResponse> AttachActiveEditor()
    {
        return Execute(() =>
        {
            if (editorService.SelectedTabItem.Value?.Context.Value is not EditViewModel editViewModel)
            {
                throw new SessionUnavailableException();
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
