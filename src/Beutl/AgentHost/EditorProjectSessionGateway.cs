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
                if (FilePathComparison.AreSameCanonicalPath(currentPath, fullPath))
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

        using (await editorService.BeginProjectFileWriteAsync(cancellationToken))
        {
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (projectService.CurrentProject.Value is not null)
                    throw new SessionUnavailableException();
            });
            Project project = ProjectOperations.CreateProject(options);
            ProjectOperations.Save(project);
        }
        return await OpenProjectAsync(fullPath, cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask<ProjectSceneResult> AddSceneAsync(IEditingSession activeSession, SceneCreateOptions options, CancellationToken cancellationToken = default)
    {
        return await Dispatcher.UIThread.InvokeAsync(async () =>
        {
            using var fileWrite = await editorService.BeginProjectFileWriteAsync(cancellationToken);
            if (activeSession is LiveEditingSession liveSession && !liveSession.ProbeIsAlive())
            {
                throw new SessionUnavailableException();
            }

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
            Scene scene;
            using (editorService.SuppressProjectItemActivation())
            {
                scene = ProjectOperations.AddScene(project, options);
            }
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
            // Bind the new scene without selecting it. Subsequent calls still name their own
            // sceneId rather than inheriting this operation's target or the visible editor.
            LiveEditingSession session = BindScene(scene);
            return new ProjectSceneResult(scene, project, session);
        });
    }

    private static void RequireSameProject(Project current, string requestedFullPath)
    {
        string currentPath = Path.GetFullPath(current.Uri!.LocalPath);
        if (!FilePathComparison.AreSameCanonicalPath(currentPath, requestedFullPath))
        {
            throw new ReconcileException(new ToolError(
                ErrorCode.ValidationRejected,
                $"The Beutl editor already has '{currentPath}' open, and the in-app toolkit edits that single open project.",
                requestedFullPath,
                "Call list_scenes and pass sceneId to edit the open project, or close it in the Beutl editor before opening or creating another project."));
        }
    }

    private ProjectSessionResult AttachToOpenProject(Project project)
    {
        Scene scene = project.Items.OfType<Scene>().FirstOrDefault()
                      ?? throw new ReconcileException(new ToolError(
                          ErrorCode.ValidationRejected,
                          "The project does not contain a scene.",
                          project.Uri?.LocalPath));

        return new ProjectSessionResult(BindScene(scene), project);
    }

    private LiveEditingSession BindScene(Scene scene)
    {
        // Closing keeps the project published while its captured tabs are being disposed.
        // Do not bind a new context that would escape that transition's close snapshot.
        if (projectService.CurrentTransition is not null)
            throw new SessionUnavailableException();

        EditorTabItem? tab = editorService.GetOrCreateBackgroundTabItem(scene);
        if (tab?.Context.Value is not EditViewModel editViewModel
            || !ReferenceEquals(editViewModel.Scene, scene))
        {
            throw new ReconcileException(new ToolError(
                ErrorCode.NoActiveEditorSession,
                "The Beutl editor could not obtain an editor context for the project's scene.",
                scene.Id.ToString()));
        }

        return LiveEditingSession.Create(new EditViewModelLiveBinding(editViewModel));
    }

    // Call only on the UI thread. An open project also exposes scenes whose tabs are closed;
    // without a project, standalone open scene tabs remain addressable.
    internal IReadOnlyList<Scene> GetScenes()
    {
        Scene[] scenes = projectService.CurrentProject.Value is { } project
            ? project.Items.OfType<Scene>().ToArray()
            : editorService.TabItems.Select(tab => tab.Context.Value).OfType<EditViewModel>()
                .Where(editor => !editor.IsDisposingOrDisposed && editor.Scene is not null)
                .Select(editor => editor.Scene).Distinct().ToArray();

        // Copied scene files keep their persisted IDs. Do not publish ambiguous targets or
        // let a request for one of them silently bind the first matching document.
        var sceneIds = new HashSet<Guid>();
        foreach (Scene scene in scenes)
        {
            if (!sceneIds.Add(scene.Id))
            {
                throw new ReconcileException(new ToolError(
                    ErrorCode.ValidationRejected,
                    $"Multiple available scenes share sceneId '{scene.Id}'; live MCP cannot select a unique target.",
                    "sceneId",
                    "Ensure all available scenes have unique persisted IDs, then call list_scenes again."));
            }
        }

        return scenes;
    }

    public LiveEditingSession ResolveScene(Guid sceneId)
        => Dispatcher.UIThread.Invoke(() =>
        {
            Scene scene = GetScenes().FirstOrDefault(scene => scene.Id == sceneId)
                ?? throw new ReconcileException(new ToolError(
                    ErrorCode.StaleHandle,
                    $"Scene '{sceneId}' is not available in this Beutl instance.",
                    sceneId.ToString(),
                    "Call list_scenes on the target instance and pass one of its sceneIds."));
            return BindScene(scene);
        });
}
