using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using Avalonia.Threading;
using Beutl.Api.Services;
using Beutl.Configuration;
using Beutl.Editor;
using Beutl.Editor.VersionControl;
using Beutl.Serialization;
using Beutl.ViewModels;
using Reactive.Bindings;

namespace Beutl.Services;

internal interface IEditorContextPublicationGate
{
    bool TryPublish(Action publish);
}

public sealed class EditorTabItem : IAsyncDisposable
{
    private readonly object _lifetimeGate = new();
    private static readonly AsyncLocal<OwnedDisposalScope?> s_ownedDisposals = new();
    private static readonly AsyncLocal<PublicationScope?> s_publicationScope = new();
    private Task? _disposeTask;
    private Task? _transitionTask;
    private TaskCompletionSource? _publicationDrain;
    private TaskCompletionSource? _removalCompletion;
    private bool _closing;
    private bool _contextDisposeAttempted;
    private bool _publicationActive;
    private MembershipState _membershipState;
    private Action<EditorTabItem>? _terminalFailureHandler;
    private string? _hash;

    public EditorTabItem(IEditorContext context)
    {
        MutableContext = new ReactiveProperty<IEditorContext?>(context);
        FilePath = Context
            .Where(static ctxt => ctxt is not null)
            .Select(static ctxt => ctxt!.Object.Uri?.LocalPath)
            .Where(static path => path is not null)
            .Select(static path => path!)
            .ToReadOnlyReactivePropertySlim()!;
        FileName = FilePath.Select(Path.GetFileName)
            .Do(_ => _hash = null)
            .ToReadOnlyReactivePropertySlim()!;
        Extension = Context
            .Where(static ctxt => ctxt is not null)
            .Select(static ctxt => ctxt!.Extension)
            .ToReadOnlyReactivePropertySlim()!;
        Commands = Context.Select(ctxt => ctxt?.Commands)
            .ToReadOnlyReactivePropertySlim();
    }

    private IReactiveProperty<IEditorContext?> MutableContext { get; }

    /// <summary>The active editor context, or <see langword="null"/> while replacement or closure is in progress.</summary>
    public IReadOnlyReactiveProperty<IEditorContext?> Context => MutableContext;

    internal void AttachOwner(Action<EditorTabItem> terminalFailureHandler)
        => _terminalFailureHandler = terminalFailureHandler;

    // Serialize publication admission with disposal, then run observer callbacks outside the
    // lifetime gate. Admitted callbacks are drained before teardown touches reactive surfaces.
    internal bool TryPublish(Action publish, bool requireContext = true)
    {
        ArgumentNullException.ThrowIfNull(publish);
        TaskCompletionSource drain;
        lock (_lifetimeGate)
        {
            if (_closing
                || _disposeTask is not null
                || (requireContext && MutableContext.Value is null)
                || (requireContext && _transitionTask is not null)
                || _publicationActive)
            {
                return false;
            }

            _publicationActive = true;
            drain = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            _publicationDrain = drain;
        }

        PublicationScope? previous = s_publicationScope.Value;
        var scope = new PublicationScope(this, previous);
        s_publicationScope.Value = scope;
        try
        {
            publish();
            lock (_lifetimeGate)
            {
                return !_closing
                    && _disposeTask is null
                    && (!requireContext || MutableContext.Value is not null);
            }
        }
        finally
        {
            lock (_lifetimeGate)
            {
                scope.Deactivate();
                _publicationActive = false;
                if (ReferenceEquals(_publicationDrain, drain))
                    _publicationDrain = null;
            }
            s_publicationScope.Value = previous;
            drain.TrySetResult();
        }
    }

    internal bool TryBeginAttachment()
    {
        lock (_lifetimeGate)
        {
            if (_closing || _disposeTask is not null || _membershipState != MembershipState.Fresh)
                return false;

            _membershipState = MembershipState.Adding;
            return true;
        }
    }

    internal bool TryCompleteAttachment()
    {
        lock (_lifetimeGate)
        {
            if (_closing || _membershipState != MembershipState.Adding)
                return false;

            _membershipState = MembershipState.Attached;
            return true;
        }
    }

    internal bool TryBeginRemoval(out Task completion)
    {
        lock (_lifetimeGate)
        {
            _closing = true;
            if (_membershipState is MembershipState.Removing or MembershipState.Removed)
            {
                completion = _removalCompletion?.Task ?? Task.CompletedTask;
                return false;
            }

            _membershipState = MembershipState.Removing;
            _removalCompletion = new TaskCompletionSource(
                TaskCreationOptions.RunContinuationsAsynchronously);
            completion = _removalCompletion.Task;
            return true;
        }
    }

    internal void CompleteRemoval()
    {
        TaskCompletionSource? completion;
        lock (_lifetimeGate)
        {
            _membershipState = MembershipState.Removed;
            completion = _removalCompletion;
        }
        completion?.TrySetResult();
    }

    internal Task GetRemovalCompletion()
    {
        lock (_lifetimeGate)
            return _removalCompletion?.Task ?? Task.CompletedTask;
    }

    internal bool IsPublicationCurrent()
    {
        lock (_lifetimeGate)
            return _publicationActive && !_closing && _disposeTask is null && MutableContext.Value is not null;
    }

    private static bool IsActivePublicationScope(EditorTabItem item)
    {
        for (PublicationScope? scope = s_publicationScope.Value; scope is not null; scope = scope.Parent)
        {
            if (scope.IsActive && ReferenceEquals(scope.Owner, item))
                return true;
        }

        return false;
    }

    private static bool IsActiveOwnedDisposalScope(EditorTabItem item)
    {
        for (OwnedDisposalScope? scope = s_ownedDisposals.Value; scope is not null; scope = scope.Parent)
        {
            if (scope.IsActive && ReferenceEquals(scope.Owner, item))
                return true;
        }

        return false;
    }

    private sealed class PublicationScope(EditorTabItem owner, PublicationScope? parent)
    {
        private int _active = 1;

        public EditorTabItem Owner { get; } = owner;

        public PublicationScope? Parent { get; } = parent;

        public bool IsActive => Volatile.Read(ref _active) != 0;

        public void Deactivate() => Interlocked.Exchange(ref _active, 0);
    }

    private sealed class OwnedDisposalScope(EditorTabItem owner, OwnedDisposalScope? parent)
    {
        private int _active = 1;

        public EditorTabItem Owner { get; } = owner;

        public OwnedDisposalScope? Parent { get; } = parent;

        public bool IsActive => Volatile.Read(ref _active) != 0;

        public void Deactivate() => Interlocked.Exchange(ref _active, 0);
    }

    private enum MembershipState
    {
        Fresh,
        Adding,
        Attached,
        Removing,
        Removed
    }

    public IReadOnlyReactiveProperty<string> FilePath { get; }

    public IReadOnlyReactiveProperty<string> FileName { get; }

    public IReadOnlyReactiveProperty<EditorExtension> Extension { get; }

    public IReadOnlyReactiveProperty<IKnownEditorCommands?> Commands { get; }

    public IReactiveProperty<bool> IsSelected { get; } = new ReactivePropertySlim<bool>();

    public string GetFileNameHash()
    {
        if (_hash == null)
        {
            string name = FileName.Value;
            ReadOnlySpan<char> span = name.AsSpan();

            // UTF-8を得たいわけではないので
            byte[] hash = MD5.HashData(MemoryMarshal.Cast<char, byte>(span));

            _hash = Convert.ToHexString(hash);
        }

        return _hash;
    }

    /// <summary>
    /// Replaces the owned editor context after the previous context has fully torn down.
    /// The supplied context is consumed even when replacement is rejected.
    /// </summary>
    public ValueTask<bool> ReplaceContextAsync(IEditorContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        IEditorContext? oldContext = null;
        TaskCompletionSource? transition = null;
        TaskCompletionSource<bool>? result = null;
        Exception? publicationFailure = null;
        bool rejected;
        lock (_lifetimeGate)
        {
            rejected = _closing || _transitionTask is not null || _publicationActive;
            if (!rejected)
            {
                oldContext = MutableContext.Value;
                transition = new TaskCompletionSource(
                    TaskCreationOptions.RunContinuationsAsynchronously);
                result = new TaskCompletionSource<bool>(
                    TaskCreationOptions.RunContinuationsAsynchronously);
                _transitionTask = transition.Task;
            }
        }

        if (rejected)
            return new ValueTask<bool>(DisposeRejectedAndReturnFalseAsync(context));

        try
        {
            _ = TryPublish(() => MutableContext.Value = null, requireContext: false);
        }
        catch (Exception ex)
        {
            publicationFailure = ex;
        }

        _ = CompleteReplacementAsync(
            oldContext!,
            context,
            transition!,
            result!,
            publicationFailure);
        return new ValueTask<bool>(result!.Task);
    }

    private async Task CompleteReplacementAsync(
        IEditorContext oldContext,
        IEditorContext replacement,
        TaskCompletionSource transition,
        TaskCompletionSource<bool> result,
        Exception? publicationFailure)
    {
        bool published = false;
        bool accepted = false;
        List<Exception>? failures = publicationFailure is null ? null : [publicationFailure];

        lock (_lifetimeGate)
            _contextDisposeAttempted = true;
        try
        {
            await DisposeOwnedContextAsync(oldContext).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            RecordFailure(ref failures, ex);
        }

        bool shouldPublish;
        lock (_lifetimeGate)
        {
            shouldPublish = failures is null && !_closing;
            if (shouldPublish)
            {
                _contextDisposeAttempted = false;
            }
            else if (failures is not null)
            {
                _closing = true;
                _contextDisposeAttempted = true;
            }
        }

        if (shouldPublish)
        {
            try
            {
                (published, accepted) = TryPublishReplacementContext(replacement);
            }
            catch (Exception ex)
            {
                published = ReferenceEquals(MutableContext.Value, replacement);
                RecordFailure(ref failures, ex);
            }
        }

        bool closeWonAfterPublication;
        lock (_lifetimeGate)
        {
            if (failures is not null)
            {
                _closing = true;
                // A published replacement is now owned by the tab even when a subscriber
                // throws. Leave its disposal to the terminal tab close; only rejected
                // replacements are disposed directly below.
                _contextDisposeAttempted = !published;
            }
            closeWonAfterPublication = published && _closing;
            _transitionTask = null;
        }

        if (!published)
        {
            Exception? rejectionFailure = await TryDisposeRejectedContextOwnedAsync(replacement)
                .ConfigureAwait(true);
            if (rejectionFailure is not null)
                RecordFailure(ref failures, rejectionFailure);
        }

        transition.TrySetResult();
        if (failures is null && accepted && !closeWonAfterPublication)
        {
            result.TrySetResult(true);
            return;
        }

        if (failures is not null)
        {
            try
            {
                _terminalFailureHandler?.Invoke(this);
            }
            catch (Exception ex)
            {
                RecordFailure(ref failures, ex);
            }
        }

        try
        {
            await DisposeAsync().ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            RecordFailure(ref failures, ex);
        }
        if (failures is null)
            result.TrySetResult(false);
        else
            result.TrySetException(CreateFailure(failures));
    }

    private (bool Published, bool Accepted) TryPublishReplacementContext(IEditorContext replacement)
    {
        bool contextPublished = true;
        bool itemPublished = false;

        void Publish()
        {
            MutableContext.Value = replacement;
            itemPublished = true;
        }

        bool itemAccepted = TryPublish(() =>
        {
            if (replacement is IEditorContextPublicationGate gate)
                contextPublished = gate.TryPublish(Publish);
            else
                Publish();
        }, requireContext: false);

        lock (_lifetimeGate)
        {
            bool accepted = itemAccepted && itemPublished && contextPublished && !_closing && _disposeTask is null
                && ReferenceEquals(MutableContext.Value, replacement);
            return (itemPublished, accepted);
        }
    }

    private static void RecordFailure(ref List<Exception>? failures, Exception exception)
        => (failures ??= []).Add(exception);

    private static Exception CreateFailure(List<Exception> failures)
        => failures.Count == 1 ? failures[0] : new AggregateException(failures);

    private async Task DisposeOwnedContextAsync(IEditorContext context)
    {
        OwnedDisposalScope? previous = s_ownedDisposals.Value;
        var scope = new OwnedDisposalScope(this, previous);
        s_ownedDisposals.Value = scope;
        try { await context.DisposeAsync().ConfigureAwait(true); }
        finally
        {
            lock (_lifetimeGate)
                scope.Deactivate();
            s_ownedDisposals.Value = previous;
        }
    }

    private async Task<Exception?> TryDisposeRejectedContextOwnedAsync(IEditorContext context)
    {
        try
        {
            await DisposeOwnedContextAsync(context).ConfigureAwait(true);
            return null;
        }
        catch (Exception ex)
        {
            return ex;
        }
    }

    private async Task<bool> DisposeRejectedAndReturnFalseAsync(IEditorContext context)
    {
        Exception? failure = await TryDisposeRejectedContextOwnedAsync(context).ConfigureAwait(true);
        if (failure is not null)
            throw failure;
        return false;
    }

    public ValueTask DisposeAsync()
    {
        Task? transition;
        Task? publicationDrain;
        TaskCompletionSource<object?>? completion = null;
        bool reentrant;
        lock (_lifetimeGate)
        {
            reentrant = IsActiveOwnedDisposalScope(this)
                || IsActivePublicationScope(this);
            if (_disposeTask is not null)
            {
                return reentrant ? ValueTask.CompletedTask : new ValueTask(_disposeTask);
            }

            _closing = true;
            transition = _transitionTask;
            publicationDrain = _publicationDrain?.Task;
            completion = new TaskCompletionSource<object?>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            _disposeTask = completion.Task;
        }

        _ = DisposeCoreAsync(transition, publicationDrain, completion);
        if (reentrant)
            ObserveDeferredDisposalFailure(completion.Task);
        return reentrant ? ValueTask.CompletedTask : new ValueTask(completion.Task);
    }

    private async Task DisposeCoreAsync(
        Task? transition,
        Task? publicationDrain,
        TaskCompletionSource<object?> completion)
    {
        List<Exception>? failures = null;
        OwnedDisposalScope? previous = s_ownedDisposals.Value;
        var scope = new OwnedDisposalScope(this, previous);
        s_ownedDisposals.Value = scope;
        try
        {
            if (publicationDrain is not null)
            {
                try { await publicationDrain.ConfigureAwait(true); }
                catch (Exception ex) { RecordFailure(ref failures, ex); }
            }

            if (transition is not null)
            {
                try { await transition.ConfigureAwait(true); }
                catch (Exception ex) { RecordFailure(ref failures, ex); }
            }

            IEditorContext? context;
            bool disposeContext;
            lock (_lifetimeGate)
            {
                context = MutableContext.Value;
                disposeContext = context is not null && !_contextDisposeAttempted;
                if (disposeContext)
                    _contextDisposeAttempted = true;
            }
            try
            {
                MutableContext.Value = null;
            }
            catch (Exception ex)
            {
                RecordFailure(ref failures, ex);
            }
            if (disposeContext && context is not null)
            {
                try
                {
                    await DisposeOwnedContextAsync(context).ConfigureAwait(true);
                }
                catch (Exception ex)
                {
                    RecordFailure(ref failures, ex);
                }
            }
        }
        catch (Exception ex)
        {
            RecordFailure(ref failures, ex);
        }
        finally
        {
            DisposeSurface(MutableContext, ref failures);
            DisposeSurface(FilePath, ref failures);
            DisposeSurface(FileName, ref failures);
            DisposeSurface(Extension, ref failures);
            DisposeSurface(Commands, ref failures);
            DisposeSurface(IsSelected, ref failures);
            lock (_lifetimeGate)
                scope.Deactivate();
            s_ownedDisposals.Value = previous;
            if (failures is null)
                completion.TrySetResult(null);
            else
                completion.TrySetException(CreateFailure(failures));
        }
    }

    private static void ObserveDeferredDisposalFailure(Task task)
    {
        _ = task.ContinueWith(
            static completed => _ = completed.Exception,
            CancellationToken.None,
            TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
    }

    private static void DisposeSurface(IDisposable surface, ref List<Exception>? failures)
    {
        try { surface.Dispose(); }
        catch (Exception ex) { RecordFailure(ref failures, ex); }
    }
}

public sealed class EditorService : IOutputOperationLeaseProvider
{
    private readonly CoreList<EditorTabItem> _tabItems;
    private readonly ExtensionProvider _extensionProvider;
    private readonly Action<Project, Uri> _serializeProject;
    private readonly ReactivePropertySlim<IProjectVersionControlService?>
        _projectVersionControlService = new();
    private readonly object _workspaceOperationSync = new();
    private readonly object _editorSuspensionSync = new();
    private readonly Dictionary<IEditorContext, (int Count, bool WasEnabled, bool WasTrackedByTab)>
        _editorSuspensions
        = new(ReferenceEqualityComparer.Instance);
    private readonly SemaphoreSlim _projectFileWriteGate = new(1, 1);
    private TaskCompletionSource? _worktreeMutationCompletion;
    private int _activeOutputOperations;
    private int _activeProjectFileWrites;
    private bool _worktreeMutationActive;

    public EditorService(ExtensionProvider extensionProvider)
        : this(
            extensionProvider,
            static (project, uri) => CoreSerializer.StoreToUri(project, uri))
    {
    }

    internal EditorService(
        ExtensionProvider extensionProvider,
        Action<Project, Uri> serializeProject)
    {
        ArgumentNullException.ThrowIfNull(extensionProvider);
        ArgumentNullException.ThrowIfNull(serializeProject);

        _extensionProvider = extensionProvider;
        _serializeProject = serializeProject;
        _tabItems = new() { ResetBehavior = ResetBehavior.Remove };
        ProjectVersionControlService = _projectVersionControlService
            .ToReadOnlyReactivePropertySlim();
    }

    public ICoreReadOnlyList<EditorTabItem> TabItems => _tabItems;

    internal void ClearTabItems()
    {
        List<Exception>? failures = null;
        foreach (EditorTabItem item in _tabItems.ToArray())
        {
            try { RemoveTabItem(item); }
            catch (Exception ex) { (failures ??= []).Add(ex); }
        }

        if (failures is not null)
            throw failures.Count == 1 ? failures[0] : new AggregateException(failures);
    }

    internal void AddTabItem(EditorTabItem item)
    {
        if (!TryAddTabItem(item) && !ContainsTabItem(item))
            ObserveDeferredTabDisposal(item.DisposeAsync().AsTask());
    }

    internal bool TryAddTabItem(EditorTabItem item)
        => TryAddTabItem(item, select: false, beforeAdd: null, beforeSelection: null);

    internal bool TryAddTabItem(EditorTabItem item, Action beforeAdd)
        => TryAddTabItem(item, select: false, beforeAdd: beforeAdd, beforeSelection: null);

    internal bool TryAddAndSelectTabItem(EditorTabItem item, Action? beforeSelection = null)
        => TryAddTabItem(item, select: true, beforeAdd: null, beforeSelection: beforeSelection);

    private bool TryAddTabItem(
        EditorTabItem item,
        bool select,
        Action? beforeAdd,
        Action? beforeSelection)
    {
        if (!item.TryBeginAttachment())
            return false;

        bool published = false;
        void Publish()
        {
            void Mutate()
            {
                beforeAdd?.Invoke();
                AddTabItemCore(item);
                if (select && _tabItems.Contains(item))
                {
                    try
                    {
                        beforeSelection?.Invoke();
                        if (!CanContinuePublication(item))
                        {
                            RollbackSelection(item);
                            return;
                        }

                        item.IsSelected.Value = true;
                        if (!CanContinuePublication(item))
                        {
                            RollbackSelection(item);
                            return;
                        }

                        SelectedTabItem.Value = item;
                        if (!CanContinuePublication(item)
                            || !ReferenceEquals(SelectedTabItem.Value, item))
                        {
                            RollbackSelection(item);
                        }
                    }
                    catch (Exception publicationFailure)
                    {
                        try
                        {
                            RollbackSelection(item);
                        }
                        catch (Exception cleanupFailure)
                        {
                            throw new AggregateException(publicationFailure, cleanupFailure);
                        }

                        throw;
                    }
                }
            }

            bool contextPublished = true;
            bool itemPublished = item.TryPublish(() =>
            {
                if (item.Context.Value is IEditorContextPublicationGate gate)
                    contextPublished = gate.TryPublish(() =>
                    {
                        if (item.IsPublicationCurrent())
                            Mutate();
                    });
                else if (item.IsPublicationCurrent())
                    Mutate();
            });
            published = itemPublished && contextPublished;
        }

        try
        {
            Publish();
        }
        catch (Exception publicationFailure)
        {
            Exception? cleanupFailure = null;
            try
            {
                ReconcileRejectedAttachment(item);
            }
            catch (Exception ex)
            {
                cleanupFailure = ex;
            }
            ObserveDeferredTabDisposal(item.DisposeAsync().AsTask());

            if (cleanupFailure is not null)
                throw new AggregateException(publicationFailure, cleanupFailure);
            throw;
        }
        try
        {
            bool accepted = published
                && ContainsTabItem(item)
                && item.TryCompleteAttachment();
            if (!accepted)
                ReconcileRejectedAttachment(item);
            return accepted;
        }
        catch
        {
            ObserveDeferredTabDisposal(item.DisposeAsync().AsTask());
            throw;
        }
    }

    internal void AddTabItemCore(EditorTabItem item)
    {
        item.AttachOwner(RemoveFailedTabItem);
        _tabItems.Add(item);
    }

    internal bool ContainsTabItem(EditorTabItem item)
        => _tabItems.Contains(item);

    internal bool RemoveTabItem(EditorTabItem item)
    {
        // Reserve terminal removal without running any collection or reactive observers while
        // the lifetime gate is held.
        if (!item.TryBeginRemoval(out _))
            return false;

        try
        {
            return RemoveTabItemFacade(item);
        }
        finally
        {
            item.CompleteRemoval();
        }
    }

    private bool RemoveTabItemFacade(EditorTabItem item)
    {
        List<Exception>? failures = null;
        bool wasPresent = _tabItems.Contains(item);
        bool removed = false;
        try
        {
            removed = _tabItems.Remove(item);
        }
        catch (Exception ex) { (failures ??= []).Add(ex); }
        if (wasPresent && !_tabItems.Contains(item))
            removed = true;

        // Clear selection even when the collection removal lost a race with another remover.
        // Otherwise a tab that is no longer live can be re-exposed as the selected item.
        if (ReferenceEquals(SelectedTabItem.Value, item))
        {
            try { SelectedTabItem.Value = null; }
            catch (Exception ex) { (failures ??= []).Add(ex); }
        }
        try { item.IsSelected.Value = false; }
        catch (Exception ex) { (failures ??= []).Add(ex); }

        if (failures is not null)
            throw failures.Count == 1 ? failures[0] : new AggregateException(failures);
        return removed;
    }

    private void ReconcileRejectedAttachment(EditorTabItem item)
    {
        bool removed = RemoveTabItem(item);
        if (removed || !ContainsTabItem(item))
            return;

        item.GetRemovalCompletion().GetAwaiter().GetResult();
        if (ContainsTabItem(item))
            _ = RemoveTabItemFacade(item);
    }

    private void RemoveFailedTabItem(EditorTabItem item)
        => RemoveTabItem(item);

    internal ExtensionProvider ExtensionProvider => _extensionProvider;

    internal void RequestContextShutdown(IEditorContext context)
    {
        EditorTabItem? item = _tabItems.FirstOrDefault(tab =>
            ReferenceEquals(tab.Context.Value, context));
        if (item is null)
            return;

        try { RemoveTabItem(item); }
        catch { }
        _ = item.DisposeAsync().AsTask().ContinueWith(
            static task => _ = task.Exception,
            CancellationToken.None,
            TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
    }

    public IReactiveProperty<EditorTabItem?> SelectedTabItem { get; } = new ReactivePropertySlim<EditorTabItem?>();

    internal IReadOnlyReactiveProperty<IProjectVersionControlService?>
        ProjectVersionControlService
    { get; }

    internal IProjectVersionControlCoordinator? ProjectVersionControlCoordinator { get; set; }

    internal IProjectVersionControlSession? ProjectVersionControlSession
        => ProjectVersionControlCoordinator as IProjectVersionControlSession;

    internal bool IsWorktreeMutationActive
    {
        get
        {
            lock (_workspaceOperationSync)
            {
                return _worktreeMutationActive;
            }
        }
    }

    internal void PublishProjectVersionControlService(
        IProjectVersionControlService? service)
    {
        _projectVersionControlService.Value = service;
    }

    internal IDisposable? TryBeginOutputOperation()
    {
        lock (_workspaceOperationSync)
        {
            if (_worktreeMutationActive)
            {
                return null;
            }

            _activeOutputOperations++;
            return new WorkspaceOperationLease(this, WorkspaceOperationKind.Output);
        }
    }

    IDisposable? IOutputOperationLeaseProvider.TryBeginOutputOperation()
    {
        return TryBeginOutputOperation();
    }

    internal IDisposable BeginObservedOutputOperation(IEditorContext? context = null)
    {
        IDisposable operation = TryBeginOutputOperation()
                                ?? throw new InvalidOperationException(
                                    "Output cannot start while the project workspace is being replaced.");

        if (context is null)
        {
            return operation;
        }

        try
        {
            return new OutputOperationLease(operation, SuspendEditor(context));
        }
        catch
        {
            operation.Dispose();
            throw;
        }
    }

    internal IDisposable SuspendEditor(IEditorContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        lock (_editorSuspensionSync)
        {
            bool isTrackedByTab = TabItems.Any(
                item => ReferenceEquals(item.Context.Value, context));
            if (_editorSuspensions.TryGetValue(context, out var state))
            {
                _editorSuspensions[context] = (
                    state.Count + 1,
                    state.WasEnabled,
                    state.WasTrackedByTab || isTrackedByTab);
            }
            else
            {
                bool wasEnabled = context.IsEnabled.Value;
                _editorSuspensions.Add(context, (1, wasEnabled, isTrackedByTab));
                try
                {
                    context.IsEnabled.Value = false;
                }
                catch (Exception disableFailure)
                {
                    _editorSuspensions.Remove(context);
                    try
                    {
                        context.IsEnabled.Value = wasEnabled;
                    }
                    catch (Exception restoreFailure)
                    {
                        throw new AggregateException(disableFailure, restoreFailure);
                    }

                    throw;
                }
            }
        }

        return new EditorContextSuspensionLease(this, context);
    }

    private void EndEditorSuspension(IEditorContext context)
    {
        lock (_editorSuspensionSync)
        {
            if (!_editorSuspensions.TryGetValue(context, out var state))
            {
                return;
            }

            if (state.Count > 1)
            {
                _editorSuspensions[context] = (
                    state.Count - 1,
                    state.WasEnabled,
                    state.WasTrackedByTab);
            }
            else
            {
                _editorSuspensions.Remove(context);
                if (!state.WasTrackedByTab
                    || TabItems.Any(item => ReferenceEquals(item.Context.Value, context)))
                {
                    context.IsEnabled.Value = state.WasEnabled;
                }
            }
        }
    }

    internal async ValueTask<IProjectFileWriteLease> BeginProjectFileWriteAsync(
        CancellationToken cancellationToken)
    {
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Task waitForWorktreeMutation;
            lock (_workspaceOperationSync)
            {
                waitForWorktreeMutation = _worktreeMutationActive
                    ? _worktreeMutationCompletion?.Task ?? Task.CompletedTask
                    : Task.CompletedTask;
            }

            // The gate is taken only once the workspace already looks free. Waiting for a worktree
            // mutation while holding it would park auto-save and editor teardown behind this writer.
            await waitForWorktreeMutation.WaitAsync(cancellationToken);
            await _projectFileWriteGate.WaitAsync(cancellationToken);
            lock (_workspaceOperationSync)
            {
                if (!_worktreeMutationActive)
                {
                    _activeProjectFileWrites++;
                    return new WorkspaceOperationLease(
                        this,
                        WorkspaceOperationKind.ProjectFileWrite);
                }
            }

            _projectFileWriteGate.Release();
        }
    }

    internal IProjectFileWriteLease? TryBeginProjectFileWrite()
    {
        if (!_projectFileWriteGate.Wait(0))
        {
            return null;
        }

        lock (_workspaceOperationSync)
        {
            if (!_worktreeMutationActive)
            {
                _activeProjectFileWrites++;
                return new WorkspaceOperationLease(
                    this,
                    WorkspaceOperationKind.ProjectFileWrite);
            }
        }

        _projectFileWriteGate.Release();
        return null;
    }

    /// <summary>
    /// Reserves the workspace for a worktree mutation, optionally taking over a finished
    /// project-file write so the workspace is never left unreserved between the two.
    /// </summary>
    /// <param name="completedWrite">
    /// A project-file write to fold into this mutation. It is released whether or not the mutation
    /// starts, so the caller must have finished writing. The caller still owns it and must dispose
    /// it, which is a no-op once it has been taken over.
    /// </param>
    internal IDisposable? TryBeginWorktreeMutation(IProjectFileWriteLease? completedWrite = null)
    {
        WorkspaceOperationLease? handoff = null;
        if (completedWrite is not null)
        {
            if (completedWrite is not WorkspaceOperationLease
                {
                    Kind: WorkspaceOperationKind.ProjectFileWrite
                } lease
                || !ReferenceEquals(lease.Owner, this))
            {
                throw new ArgumentException(
                    "The lease was not issued by this service for a project-file write.",
                    nameof(completedWrite));
            }

            handoff = lease;
        }

        IDisposable? mutation = null;
        lock (_workspaceOperationSync)
        {
            if (handoff is not null && handoff.TryTakeOver())
            {
                _activeProjectFileWrites--;
                // Released under the lock so the count and the gate never disagree about whether the
                // workspace is reserved.
                _projectFileWriteGate.Release();
            }

            if (!_worktreeMutationActive
                && _activeOutputOperations == 0
                && _activeProjectFileWrites == 0)
            {
                _worktreeMutationActive = true;
                _worktreeMutationCompletion = new TaskCompletionSource(
                    TaskCreationOptions.RunContinuationsAsynchronously);
                mutation = new WorkspaceOperationLease(
                    this,
                    WorkspaceOperationKind.WorktreeMutation);
            }
        }

        return mutation;
    }

    internal async Task<bool> SaveProjectFilesAsync(
        Project project,
        CancellationToken cancellationToken)
    {
        if (!Dispatcher.UIThread.CheckAccess())
        {
            return await Dispatcher.UIThread.InvokeAsync(
                () => SaveProjectFilesCoreAsync(project, cancellationToken));
        }

        return await SaveProjectFilesCoreAsync(project, cancellationToken);
    }

    private async Task<bool> SaveProjectFilesCoreAsync(
        Project project,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(project);
        cancellationToken.ThrowIfCancellationRequested();
        Uri projectUri = project.Uri
                         ?? throw new InvalidOperationException(
                             "The project must have a file URI before it can be saved.");
        EditorTabItem[] tabItems = TabItems.ToArray();
        using IDisposable suspension = SuspendEditors();
        await Task.Run(
            () => _serializeProject(project, projectUri),
            cancellationToken);

        foreach (EditorTabItem item in tabItems)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (item.Commands.Value is { } commands && !await commands.OnSave())
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Disables every open editor until the returned handle is disposed.
    /// </summary>
    /// <remarks>
    /// A version-control transition holds this from before its pre-transition save until the
    /// project is closed. Releasing it earlier would let the user edit while the cycle awaits Git,
    /// and those edits would land after the safety snapshot and be discarded by the close.
    /// Suspensions share a per-context reference count with output execution, so they can complete
    /// in any order without re-enabling an editor still owned by another operation.
    /// </remarks>
    internal IDisposable SuspendEditors()
    {
        EditorTabItem[] tabItems = TabItems.ToArray();
        var suspensions = new List<IDisposable>(tabItems.Length);
        try
        {
            foreach (EditorTabItem item in tabItems)
            {
                if (item.Context.Value is { } context)
                {
                    suspensions.Add(SuspendEditor(context));
                }
            }

            return new EditorSuspension(suspensions.ToArray());
        }
        catch (Exception acquisitionFailure)
        {
            var failures = new List<Exception> { acquisitionFailure };
            foreach (IDisposable suspension in suspensions)
            {
                try
                {
                    suspension.Dispose();
                }
                catch (Exception cleanupFailure)
                {
                    failures.Add(cleanupFailure);
                }
            }

            throw new AggregateException(failures);
        }
    }

    private void EndWorkspaceOperation(WorkspaceOperationKind kind)
    {
        TaskCompletionSource? completedWorktreeMutation = null;
        bool releaseProjectFileWrite = false;
        lock (_workspaceOperationSync)
        {
            switch (kind)
            {
                case WorkspaceOperationKind.Output when _activeOutputOperations > 0:
                    _activeOutputOperations--;
                    break;
                case WorkspaceOperationKind.ProjectFileWrite when _activeProjectFileWrites > 0:
                    _activeProjectFileWrites--;
                    releaseProjectFileWrite = true;
                    break;
                case WorkspaceOperationKind.WorktreeMutation:
                    _worktreeMutationActive = false;
                    completedWorktreeMutation = _worktreeMutationCompletion;
                    _worktreeMutationCompletion = null;
                    break;
            }
        }

        completedWorktreeMutation?.TrySetResult();
        if (releaseProjectFileWrite)
        {
            _projectFileWriteGate.Release();
        }
    }

    public bool TryGetTabItem(CoreObject obj, [NotNullWhen(true)] out EditorTabItem? result)
    {
        result = TabItems.FirstOrDefault(i => i.Context.Value?.Object == obj);

        return result != null;
    }

    public void ActivateTabItem(CoreObject obj)
    {
        ViewConfig viewConfig = GlobalConfiguration.Instance.ViewConfig;
        string path = Uri.UnescapeDataString(obj.Uri!.LocalPath);
        viewConfig.UpdateRecentFile(path);

        if (TryGetTabItem(obj, out EditorTabItem? tabItem))
        {
            TrySelectTabItem(tabItem);
        }
        else
        {
            EditorExtension? ext = _extensionProvider.MatchEditorExtension(path);

            if (ext?.TryCreateContext(obj, new EditorContextServices(this, _extensionProvider), out IEditorContext? context) == true)
            {
                var tabItem2 = new EditorTabItem(context) { IsSelected = { Value = true } };
                if (!TryAddAndSelectTabItem(tabItem2))
                    ObserveDeferredTabDisposal(tabItem2.DisposeAsync().AsTask());
            }
        }
    }

    private bool TrySelectTabItem(EditorTabItem item)
    {
        bool selected = false;
        void Publish()
        {
            void Mutate()
            {
                try
                {
                    if (!CanContinuePublication(item))
                        return;

                    item.IsSelected.Value = true;
                    if (!CanContinuePublication(item))
                    {
                        RollbackSelection(item);
                        return;
                    }

                    SelectedTabItem.Value = item;
                    if (!CanContinuePublication(item)
                        || !ReferenceEquals(SelectedTabItem.Value, item))
                    {
                        RollbackSelection(item);
                        return;
                    }

                    selected = true;
                }
                catch (Exception publicationFailure)
                {
                    try
                    {
                        RollbackSelection(item);
                    }
                    catch (Exception cleanupFailure)
                    {
                        throw new AggregateException(publicationFailure, cleanupFailure);
                    }

                    throw;
                }
            }

            bool contextPublished = true;
            bool itemPublished = item.TryPublish(() =>
            {
                if (item.Context.Value is IEditorContextPublicationGate gate)
                    contextPublished = gate.TryPublish(() =>
                    {
                        if (CanContinuePublication(item))
                            Mutate();
                    });
                else if (CanContinuePublication(item))
                    Mutate();
            });
            selected = selected && itemPublished && contextPublished;
        }

        Publish();
        return selected && ReferenceEquals(SelectedTabItem.Value, item);
    }

    private bool CanContinuePublication(EditorTabItem item)
        => item.IsPublicationCurrent()
            && _tabItems.Contains(item)
            && item.Context.Value is not null;

    private void RollbackSelection(EditorTabItem item)
    {
        List<Exception>? failures = null;
        if (ReferenceEquals(SelectedTabItem.Value, item))
        {
            try { SelectedTabItem.Value = null; }
            catch (Exception ex) { (failures ??= []).Add(ex); }
        }
        try { item.IsSelected.Value = false; }
        catch (Exception ex) { (failures ??= []).Add(ex); }

        if (failures is not null)
            throw failures.Count == 1 ? failures[0] : new AggregateException(failures);
    }

    private static void ObserveDeferredTabDisposal(Task task)
    {
        _ = task.ContinueWith(
            static completed => _ = completed.Exception,
            CancellationToken.None,
            TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
    }

    public async ValueTask CloseTabItem(CoreObject obj)
    {
        if (TryGetTabItem(obj, out EditorTabItem? item))
            await CloseTabItem(item);
    }

    public async ValueTask CloseTabItem(EditorTabItem item)
    {
        List<Exception>? failures = null;
        try { RemoveTabItem(item); }
        catch (Exception ex) { failures = [ex]; }
        try { await item.DisposeAsync(); }
        catch (Exception ex) { (failures ??= []).Add(ex); }
        if (failures is not null)
            throw failures.Count == 1 ? failures[0] : new AggregateException(failures);
    }

    private sealed class EditorSuspension(IDisposable[] suspensions) : IDisposable
    {
        private int _disposed;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
            {
                return;
            }

            List<Exception>? failures = null;
            foreach (IDisposable suspension in suspensions)
            {
                try
                {
                    suspension.Dispose();
                }
                catch (Exception ex)
                {
                    (failures ??= []).Add(ex);
                }
            }

            if (failures is not null)
            {
                throw new AggregateException(failures);
            }
        }
    }

    private sealed class WorkspaceOperationLease(
        EditorService owner,
        WorkspaceOperationKind kind) : IProjectFileWriteLease
    {
        private int _disposed;

        public EditorService Owner => owner;

        public WorkspaceOperationKind Kind => kind;

        // Retires the lease without ending the operation it reserves, so the caller can transfer
        // that reservation to another lease instead of releasing and racing to reacquire it.
        public bool TryTakeOver()
        {
            return Interlocked.Exchange(ref _disposed, 1) == 0;
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
            {
                owner.EndWorkspaceOperation(kind);
            }
        }
    }

    private sealed class EditorContextSuspensionLease(
        EditorService owner,
        IEditorContext context) : IDisposable
    {
        private int _disposed;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
            {
                owner.EndEditorSuspension(context);
            }
        }
    }

    private sealed class OutputOperationLease(
        IDisposable operation,
        IDisposable suspension) : IDisposable
    {
        private int _disposed;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
            {
                return;
            }

            try
            {
                suspension.Dispose();
            }
            finally
            {
                operation.Dispose();
            }
        }
    }

    private enum WorkspaceOperationKind
    {
        Output,
        ProjectFileWrite,
        WorktreeMutation,
    }
}
