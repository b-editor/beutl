using System.Collections.Immutable;
using System.Collections.Specialized;
using Avalonia.Threading;
using Beutl.Editor;
using Beutl.Editor.Components.OutputTab;
using Beutl.Editor.VersionControl;
using Reactive.Bindings;

namespace Beutl.Services;

// The workspaces of the projects this service edits, one per project file, and the members that act on the one the
// editor shows. Code that works on a particular project, such as a scene editor or the version control tracking a
// project, uses that project's workspace (WorkspaceFor) instead.
public sealed partial class EditorService
{
    private readonly object _workspacesSync = new();

    // Guards the reservations of every workspace, so a workspace checks the ones whose files it overlaps atomically.
    // Never held while taking _workspacesSync.
    internal object WorkspaceOperationSync { get; } = new();
    private readonly List<EditorWorkspace> _workspaces = [];
    private readonly object _allRunningOutputsSync = new();
    private readonly ReactivePropertySlim<ImmutableArray<OutputProfileItem>> _allRunningOutputs = new([]);
    private readonly ProjectSessionRegistry _sessions;
    // The work that concerns no loaded project, such as creating one; also the workspace of the editor while no
    // project is active.
    private readonly EditorWorkspace _shellWorkspace;
    private readonly ReactivePropertySlim<EditorWorkspace> _activeWorkspace;

    // The service's own workspace, for work that concerns no loaded project.
    internal EditorWorkspace ShellWorkspace => _shellWorkspace;

    // The workspace of the active session, or the service's own one while no session is active.
    internal IReadOnlyReactiveProperty<EditorWorkspace> ActiveWorkspace => _activeWorkspace;

    // The workspace of a loaded project. A project no session holds, such as a model a test made, has its editors
    // among the open tabs, so it shares the active workspace.
    internal EditorWorkspace WorkspaceFor(Project project)
    {
        ArgumentNullException.ThrowIfNull(project);
        return _sessions.Find(project) is { } session ? WorkspaceFor(session) : ActiveWorkspace.Value;
    }

    // The workspace of the project's file whether or not the project is still loaded, for the tabs of a session that
    // outlive its unloading until they are disposed.
    private EditorWorkspace WorkspaceOfFile(Project project)
        => project.Uri is { IsFile: true } uri ? WorkspaceForFile(uri.LocalPath) : WorkspaceForUnfiled(project);

    internal EditorWorkspace WorkspaceFor(ProjectSession session)
    {
        ArgumentNullException.ThrowIfNull(session);
        return session.FilePath is { } file ? WorkspaceForFile(file) : WorkspaceForUnfiled(session.Project);
    }

    // A loaded project that is not in a file has a workspace of its own, keyed by the model, so two of them never
    // share tabs put aside.
    private EditorWorkspace WorkspaceForUnfiled(Project project)
    {
        lock (_workspacesSync)
        {
            foreach (EditorWorkspace workspace in _workspaces)
            {
                if (ReferenceEquals(workspace.UnfiledProject, project))
                    return workspace;
            }

            var created = new EditorWorkspace(this, projectFile: null) { UnfiledProject = project };
            created.RefreshProject(_sessions);
            _workspaces.Add(created);
            return created;
        }
    }

    // The workspace of the project an editor context edits, or the active one for an object outside any project.
    internal EditorWorkspace WorkspaceFor(CoreObject? obj)
        => (obj as Project ?? (obj as IHierarchical)?.FindHierarchicalParent<Project>()) is { } project
            ? WorkspaceFor(project)
            : ActiveWorkspace.Value;

    private EditorWorkspace WorkspaceForFile(string file)
    {
        lock (_workspacesSync)
        {
            foreach (EditorWorkspace workspace in _workspaces)
            {
                if (string.Equals(workspace.ProjectFile, file, StringComparison.Ordinal))
                    return workspace;
            }

            foreach (EditorWorkspace workspace in _workspaces)
            {
                if (workspace.IsAt(file))
                    return workspace;
            }

            var created = new EditorWorkspace(this, file);
            created.RefreshProject(_sessions);
            _workspaces.Add(created);
            return created;
        }
    }

    // The activity of the session of the project at file, for work keyed by the project file rather than by a loaded
    // model, such as an agent thread. A lease keeps the workspace, so the project finds it again if it is reloaded
    // while the work runs.
    internal SessionActivityTracker ActivityOfFile(string file)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(file);
        return WorkspaceForFile(file).Activity;
    }

    // The workspaces of the projects this service has edited and still holds.
    internal EditorWorkspace[] Workspaces
    {
        get
        {
            lock (_workspacesSync)
            {
                return [_shellWorkspace, .. _workspaces];
            }
        }
    }

    private static void FollowSessions(ProjectSessionRegistry sessions, EditorService service)
    {
        var target = new WeakReference<EditorService>(service);
        IDisposable? subscription = null;
        bool collected = false;
        subscription = sessions.ActiveSession.Subscribe(session =>
        {
            if (target.TryGetTarget(out EditorService? alive))
            {
                alive.OnActiveSessionChanged(session);
            }
            else
            {
                collected = true;
                subscription?.Dispose();
            }
        });
        if (collected)
            subscription.Dispose();

        var loaded = (INotifyCollectionChanged)sessions.Sessions;
        NotifyCollectionChangedEventHandler? handler = null;
        handler = (_, _) =>
        {
            if (target.TryGetTarget(out EditorService? alive))
                alive.OnSessionsChanged();
            else
                loaded.CollectionChanged -= handler;
        };
        loaded.CollectionChanged += handler;
    }

    // A project was loaded or unloaded, so the workspace of its file has a model again or none. An unloaded
    // project's workspace can go now, the session having left the registry after the active one changed.
    private void OnSessionsChanged()
    {
        foreach (EditorWorkspace workspace in Workspaces)
            workspace.RefreshProject(_sessions);
        ReleaseUnusedWorkspaces();
    }

    private int _workspaceReleaseScheduled;

    // Something that held a workspace let go of it, possibly from another thread: the tabs put aside, a reservation,
    // an activity or the version control tracking the project. The release runs on the UI thread, where the
    // sessions are.
    internal void ScheduleWorkspaceRelease()
    {
        if (Interlocked.Exchange(ref _workspaceReleaseScheduled, 1) != 0)
            return;

        Dispatcher.UIThread.Post(() =>
        {
            Volatile.Write(ref _workspaceReleaseScheduled, 0);
            ReleaseUnusedWorkspaces();
        }, DispatcherPriority.Background);
    }

    private void OnActiveSessionChanged(ProjectSession? session)
    {
        EditorWorkspace workspace = session is not null ? WorkspaceFor(session) : _shellWorkspace;
        _activeWorkspace.Value = workspace;
        RefreshProjectVersionControlService(workspace);
        if (Dispatcher.UIThread.CheckAccess())
            RefreshLifecycleActivity();
        else
            Dispatcher.UIThread.Post(RefreshLifecycleActivity);
        ReleaseUnusedWorkspaces();
    }

    // Lets go of the workspaces of unloaded projects that nothing holds any more.
    private void ReleaseUnusedWorkspaces()
    {
        lock (_workspacesSync)
        {
            _workspaces.RemoveAll(workspace =>
                !ReferenceEquals(workspace, _activeWorkspace.Value)
                && (workspace.ProjectFile is { } file
                    ? _sessions.Find(file) is null
                    : workspace.UnfiledProject is { } project && _sessions.Find(project) is null)
                && workspace.IsIdle);
        }
    }

    // The tabs of workspace: those put aside for it, and the open tabs while it is the active one.
    internal EditorTabItem[] GetTabItems(EditorWorkspace workspace)
    {
        EditorTabItem[] inactive = workspace.InactiveTabs?.Items ?? [];
        return ReferenceEquals(workspace, ActiveWorkspace.Value) ? [.. _tabItems, .. inactive] : inactive;
    }

    // Every output running in an editor of this service, whichever project it belongs to. Published from whichever
    // thread the output runs on, so subscribers must marshal to the UI thread themselves.
    internal IReadOnlyReactiveProperty<ImmutableArray<OutputProfileItem>> RunningOutputs => _allRunningOutputs;

    internal IDisposable TrackRunningOutputOfAnyProject(OutputProfileItem item)
    {
        lock (_allRunningOutputsSync)
        {
            _allRunningOutputs.Value = _allRunningOutputs.Value.Add(item);
        }

        return Disposable.Create(() =>
        {
            lock (_allRunningOutputsSync)
            {
                _allRunningOutputs.Value = _allRunningOutputs.Value.Remove(item);
            }
        });
    }

    /// <summary>
    /// The slow work shown in place of the editor area, or <see cref="ProjectLifecycleActivity.None"/> while the
    /// editor area is available: work that concerns no loaded project, such as creating one, else the work on the
    /// active project. Work on a project in the background covers nothing.
    /// </summary>
    internal IReadOnlyReactiveProperty<ProjectLifecycleActivity> LifecycleActivity { get; }

    private readonly ReactivePropertySlim<ProjectLifecycleActivity> _lifecycleActivity = new();
    private readonly ReactivePropertySlim<IProjectVersionControlService?> _projectVersionControlService = new();

    // Called on the UI thread whenever the activity of a workspace or the active workspace changes. Plain properties
    // rather than an operator chain, so a subscriber that throws cannot detach the editor from its workspaces.
    internal void RefreshLifecycleActivity()
    {
        Dispatcher.UIThread.VerifyAccess();
        _lifecycleActivity.Value = GetDisplayedLifecycleActivity();
    }

    private ProjectLifecycleActivity GetDisplayedLifecycleActivity()
    {
        if (_shellWorkspace.LifecycleActivity.Value is var shell and not ProjectLifecycleActivity.None)
            return shell;

        EditorWorkspace active = ActiveWorkspace.Value;
        if (!ReferenceEquals(active, _shellWorkspace))
            return active.LifecycleActivity.Value;

        // No project is active, as while an operation closes a project to reopen it: the editor area shows that
        // operation rather than an editor area that looks free.
        foreach (EditorWorkspace workspace in Workspaces)
        {
            if (workspace.LifecycleActivity.Value is var activity and not ProjectLifecycleActivity.None)
                return activity;
        }

        return ProjectLifecycleActivity.None;
    }

    internal void RefreshProjectVersionControlService(EditorWorkspace workspace)
    {
        if (ReferenceEquals(workspace, ActiveWorkspace.Value))
            _projectVersionControlService.Value = workspace.ProjectVersionControlService.Value;
    }

    // Shows activity in place of the editor area, whichever project is active, until the returned handle is disposed.
    // Work on a particular project begins its activity in that project's workspace instead.
    internal IDisposable BeginLifecycleActivity(ProjectLifecycleActivity activity)
        => _shellWorkspace.BeginLifecycleActivity(activity);

    // The members below reserve the files of the project the editor shows.
    internal bool IsWorktreeMutationActive => ActiveWorkspace.Value.IsWorktreeMutationActive;

    internal IDisposable? TryBeginOutputOperation() => ActiveWorkspace.Value.TryBeginOutputOperation();

    IDisposable? IOutputOperationLeaseProvider.TryBeginOutputOperation() => TryBeginOutputOperation();

    internal IDisposable? TryBeginEditorFileOpen() => ActiveWorkspace.Value.TryBeginEditorFileOpen();

    internal IDisposable BeginObservedOutputOperation() => ActiveWorkspace.Value.BeginObservedOutputOperation();

    // The wait can span a switch to another project, so the lease is taken again on the workspace the editor shows
    // once it is held; a version-control operation that closes and reopens the project keeps its workspace.
    internal async ValueTask<IProjectFileWriteLease> BeginProjectFileWriteAsync(CancellationToken cancellationToken)
    {
        while (true)
        {
            EditorWorkspace workspace = ActiveWorkspace.Value;
            IProjectFileWriteLease lease = await workspace.BeginProjectFileWriteAsync(cancellationToken);
            if (ReferenceEquals(workspace, ActiveWorkspace.Value))
                return lease;

            lease.Dispose();
        }
    }

    internal IProjectFileWriteLease? TryBeginProjectFileWrite() => ActiveWorkspace.Value.TryBeginProjectFileWrite();

    internal ValueTask<IProjectFileWriteLease?> TryBeginProjectFileWriteAsync(CancellationToken cancellationToken)
        => ActiveWorkspace.Value.TryBeginProjectFileWriteAsync(cancellationToken);

    internal IDisposable? TryBeginWorktreeMutation(IProjectFileWriteLease? completedWrite = null)
        => ActiveWorkspace.Value.TryBeginWorktreeMutation(completedWrite);

    // Runs an output of the active project.
    internal IDisposable TrackRunningOutput(OutputProfileItem item) => ActiveWorkspace.Value.TrackRunningOutput(item);

    IDisposable IOutputRunHost.TrackRunningOutput(OutputProfileItem item) => TrackRunningOutput(item);

    // Saves the scene editors of items, open tabs of the active project, before their tabs close.
    internal Task<bool> SaveSceneEditorsBeforeCloseAsync(IEnumerable<EditorTabItem> items)
    {
        if (!Dispatcher.UIThread.CheckAccess())
            return Dispatcher.UIThread.InvokeAsync(() => SaveSceneEditorsBeforeCloseAsync(items));

        return ActiveWorkspace.Value.SaveSceneEditorsBeforeCloseAsync(items);
    }
}
