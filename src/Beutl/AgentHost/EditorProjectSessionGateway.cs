using Avalonia.Threading;
using Beutl.AgentToolkit.Common;
using Beutl.AgentToolkit.Reconciliation;
using Beutl.AgentToolkit.Sessions;
using Beutl.AgentToolkit.Workspace;
using Beutl.ProjectSystem;
using Beutl.Services;
using Beutl.ViewModels;

namespace Beutl.AgentHost;

public sealed class EditorProjectSessionGateway(
    ProjectService projectService,
    EditorService editorService,
    LiveSessionSource liveSessions,
    AgentSessionManager sessions,
    IWorkspaceGuard workspace) : IProjectSessionGateway
{
    public async ValueTask<ProjectSessionResult> OpenProjectAsync(string fullPath, CancellationToken cancellationToken = default)
    {
        return await Dispatcher.UIThread.InvokeAsync(async () =>
        {
            if (projectService.CurrentProject.Value is { } current)
            {
                RequireSameProject(current, fullPath);
                return AttachToOpenProject(current);
            }

            await projectService.OpenProject(fullPath);
            if (projectService.CurrentProject.Value is not { } opened)
            {
                throw new ReconcileException(new ToolError(
                    ErrorCode.ValidationRejected,
                    $"The Beutl editor could not open '{fullPath}'.",
                    fullPath,
                    "Check the editor notification for the failure reason (unreadable file, version mismatch, ...)."));
            }

            return AttachToOpenProject(opened);
        });
    }

    public async ValueTask<ProjectSessionResult> CreateProjectAsync(ProjectCreateOptions options, CancellationToken cancellationToken = default)
    {
        string fullPath = Path.GetFullPath(options.ProjectPath);
        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            if (projectService.CurrentProject.Value is { } current)
            {
                // create_project cannot overwrite the project already open in the editor: saving over
                // it would leave the live session on the stale in-memory scene while a new file sits on
                // disk. A different path is likewise rejected (the in-app host edits one open project).
                string currentPath = Path.GetFullPath(current.Uri!.LocalPath);
                if (string.Equals(currentPath, fullPath, PathComparison.ForCurrentPlatform))
                {
                    throw new ReconcileException(new ToolError(
                        ErrorCode.ValidationRejected,
                        $"'{currentPath}' is already open in the Beutl editor; close it before recreating that project.",
                        fullPath,
                        "Close the open project first, or create the new project at a different path."));
                }

                RequireSameProject(current, fullPath);
            }
        });

        Project project = ProjectOperations.CreateProject(options);
        ProjectOperations.Save(project);
        return await OpenProjectAsync(fullPath, cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask<ProjectSceneResult> AddSceneAsync(IEditingSession activeSession, SceneCreateOptions options, CancellationToken cancellationToken = default)
    {
        return await Dispatcher.UIThread.InvokeAsync(() =>
        {
            if (projectService.CurrentProject.Value is not { } project)
            {
                throw new SessionUnavailableException();
            }

            // The caller's session must still be the one bound to the open editor project. If the user
            // switched projects (or closed and reopened one) after the session was captured, its Root
            // scene is no longer in the live project, so adding a scene to `project` would mutate a
            // different document than the client is editing.
            if (activeSession.Root is not Scene sessionScene || !project.Items.Contains(sessionScene))
            {
                throw new SessionUnavailableException();
            }

            // The editor may hold a project opened from outside the workspace (open_project reads
            // anywhere); saving its sidecars there would let a live MCP client write outside the
            // configured root. Enforce the boundary before mutating the live project so a rejected
            // write leaves no unsaved scene behind in the UI.
            workspace.ResolveForWrite(project.Uri!.LocalPath);
            ProjectUriState uriState = ProjectOperations.CaptureUriState(project);
            Scene scene = ProjectOperations.AddScene(project, options);
            try
            {
                ProjectOperations.Save(project);
            }
            catch
            {
                // A failed save (permissions, disk, serialization) must not leave the unsaved scene
                // behind in the live editor, nor the sidecar URIs Save rewrote before the fallible
                // project write.
                project.Items.Remove(scene);
                ProjectOperations.RestoreUriState(project, uriState);
                throw;
            }
            // add_scene activates the new scene's tab, so rebind the live session to it; otherwise
            // read_document/apply_edit would keep operating on the previously attached EditViewModel.
            LiveEditingSession session = AttachScene(scene);
            return new ProjectSceneResult(scene, project, session);
        });
    }

    private static void RequireSameProject(Project current, string requestedFullPath)
    {
        string currentPath = Path.GetFullPath(current.Uri!.LocalPath);
        if (!string.Equals(currentPath, requestedFullPath, PathComparison.ForCurrentPlatform))
        {
            throw new ReconcileException(new ToolError(
                ErrorCode.ValidationRejected,
                $"The Beutl editor already has '{currentPath}' open, and the in-app toolkit edits that single open project.",
                requestedFullPath,
                "Call attach_active_editor to edit the open project, or close it in the Beutl editor before opening or creating another project."));
        }
    }

    private ProjectSessionResult AttachToOpenProject(Project project)
    {
        Scene scene = project.Items.OfType<Scene>().FirstOrDefault()
                      ?? throw new ReconcileException(new ToolError(
                          ErrorCode.ValidationRejected,
                          "The project does not contain a scene.",
                          project.Uri?.LocalPath));

        return new ProjectSessionResult(AttachScene(scene), project);
    }

    private LiveEditingSession AttachScene(Scene scene)
    {
        editorService.ActivateTabItem(scene);
        if (editorService.SelectedTabItem.Value?.Context.Value is not EditViewModel editViewModel)
        {
            throw new ReconcileException(new ToolError(
                ErrorCode.NoActiveEditorSession,
                "The Beutl editor could not open an editor tab for the project's scene.",
                scene.Id.ToString()));
        }

        LiveEditingSession session = liveSessions.Attach(new EditViewModelLiveBinding(editViewModel));
        sessions.UseSource(liveSessions);
        return session;
    }
}
