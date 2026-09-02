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

internal readonly record struct EditorContextRegistration(
    IEditorContext Context,
    long Generation);

public sealed class EditorTabItem : IAsyncDisposable
{
    private readonly object _lifetimeGate = new();
    private Task? _disposeTask;
    private Task? _transitionTask;
    private TaskCompletionSource? _publicationDrain;
    private TaskCompletionSource? _removalCompletion;
    private bool _closing;
    private bool _contextDisposeAttempted;
    private bool _publicationActive;
    private MembershipState _membershipState;
    private Task? _hostCloseTask;
    private Action<EditorTabItem>? _terminalFailureHandler;
    private Func<EditorTabItem, IEditorContext, EditorContextRegistration?>? _claimContextHandler;
    private Func<EditorTabItem, EditorContextRegistration, bool>? _publishContextHandler;
    private Action<EditorTabItem, EditorContextRegistration>? _releaseContextHandler;
    private EditorContextRegistration? _contextRegistration;
    private readonly HashSet<IEditorContext> _hostDisposingContexts =
        new(ReferenceEqualityComparer.Instance);
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

    internal bool TryAttachOwner(
        Action<EditorTabItem> terminalFailureHandler,
        EditorContextRegistration registration,
        Func<EditorTabItem, IEditorContext, EditorContextRegistration?> claimContextHandler,
        Func<EditorTabItem, EditorContextRegistration, bool> publishContextHandler,
        Action<EditorTabItem, EditorContextRegistration> releaseContextHandler)
    {
        lock (_lifetimeGate)
        {
            if (_closing || _disposeTask is not null || _claimContextHandler is not null)
                return false;

            _terminalFailureHandler = terminalFailureHandler;
            _contextRegistration = registration;
            _claimContextHandler = claimContextHandler;
            _publishContextHandler = publishContextHandler;
            _releaseContextHandler = releaseContextHandler;
            return true;
        }
    }

    internal bool IsHostOwned
    {
        get
        {
            lock (_lifetimeGate)
                return _claimContextHandler is not null;
        }
    }

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
                _publicationActive = false;
                if (ReferenceEquals(_publicationDrain, drain))
                    _publicationDrain = null;
            }
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

    internal bool TryBeginHostClose(
        out Task completion,
        out TaskCompletionSource<object?>? completionSource)
    {
        lock (_lifetimeGate)
        {
            if (_hostCloseTask is not null)
            {
                completion = _hostCloseTask;
                completionSource = null;
                return false;
            }

            completionSource = new TaskCompletionSource<object?>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            completion = completionSource.Task;
            _hostCloseTask = completion;
            return true;
        }
    }

    internal bool TryBeginRemoval(out Task completion, out Task? publicationDrain)
    {
        lock (_lifetimeGate)
        {
            _closing = true;
            publicationDrain = _publicationDrain?.Task;
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

    internal void CompleteRemoval(Exception? failure = null)
    {
        TaskCompletionSource? completion;
        lock (_lifetimeGate)
        {
            _membershipState = MembershipState.Removed;
            completion = _removalCompletion;
        }
        if (failure is null)
            completion?.TrySetResult();
        else
            completion?.TrySetException(failure);
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

    internal bool IsHostDisposingContext(IEditorContext context)
    {
        lock (_lifetimeGate)
            return _hostDisposingContexts.Contains(context);
    }

    internal void ReleaseContextRegistration()
    {
        EditorContextRegistration? registration;
        lock (_lifetimeGate)
        {
            registration = _contextRegistration;
            _contextRegistration = null;
        }
        if (registration is { } owned)
            _releaseContextHandler?.Invoke(this, owned);
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
    /// </summary>
    /// <remarks>
    /// A context already claimed by this or another tab is rejected without being consumed.
    /// All other supplied contexts are consumed for both return values.
    /// </remarks>
    public ValueTask<bool> ReplaceContextAsync(IEditorContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        IEditorContext? oldContext = null;
        EditorContextRegistration? oldRegistration = null;
        TaskCompletionSource? transition = null;
        TaskCompletionSource<bool>? result = null;
        Exception? publicationFailure = null;
        bool sameInstance;
        Func<EditorTabItem, IEditorContext, EditorContextRegistration?>? claimContextHandler;
        lock (_lifetimeGate)
        {
            sameInstance = ReferenceEquals(MutableContext.Value, context);
            claimContextHandler = _claimContextHandler;
        }

        if (sameInstance)
            return new ValueTask<bool>(false);

        EditorContextRegistration? replacementRegistration = claimContextHandler?.Invoke(this, context);
        if (claimContextHandler is not null && replacementRegistration is null)
            return new ValueTask<bool>(false);

        bool rejected;
        lock (_lifetimeGate)
        {
            rejected = _closing || _transitionTask is not null || _publicationActive;
            if (!rejected)
            {
                oldContext = MutableContext.Value;
                oldRegistration = _contextRegistration;
                transition = new TaskCompletionSource(
                    TaskCreationOptions.RunContinuationsAsynchronously);
                result = new TaskCompletionSource<bool>(
                    TaskCreationOptions.RunContinuationsAsynchronously);
                _transitionTask = transition.Task;
            }
        }

        if (rejected)
        {
            return replacementRegistration is { } reserved
                ? new ValueTask<bool>(DisposeRejectedAndReleaseContextAsync(context, reserved))
                : new ValueTask<bool>(DisposeRejectedAndReturnFalseAsync(context));
        }

        if (replacementRegistration is { } claimed
            && _publishContextHandler?.Invoke(this, claimed) != true)
        {
            lock (_lifetimeGate)
                _transitionTask = null;
            transition!.TrySetResult();
            return new ValueTask<bool>(DisposeRejectedAndReleaseContextAsync(context, claimed));
        }

        try
        {
            _ = TryPublish(() =>
            {
                MutableContext.Value = null;
            }, requireContext: false);
        }
        catch (Exception ex)
        {
            publicationFailure = ex;
        }

        _ = CompleteReplacementAsync(
            oldContext!,
            context,
            oldRegistration,
            replacementRegistration,
            transition!,
            result!,
            publicationFailure);
        return new ValueTask<bool>(result!.Task);
    }

    private async Task CompleteReplacementAsync(
        IEditorContext oldContext,
        IEditorContext replacement,
        EditorContextRegistration? oldRegistration,
        EditorContextRegistration? replacementRegistration,
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
                (published, accepted) = TryPublishReplacementContext(
                    replacement,
                    replacementRegistration);
            }
            catch (Exception ex)
            {
                published = ReferenceEquals(MutableContext.Value, replacement);
                if (published)
                {
                    lock (_lifetimeGate)
                        _contextRegistration = replacementRegistration;
                }
                RecordFailure(ref failures, ex);
            }
        }

        if (published)
        {
            if (oldRegistration is { } old)
                _releaseContextHandler?.Invoke(this, old);
        }
        else if (replacementRegistration is { } reserved)
        {
            _releaseContextHandler?.Invoke(this, reserved);
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

    private (bool Published, bool Accepted) TryPublishReplacementContext(
        IEditorContext replacement,
        EditorContextRegistration? replacementRegistration)
    {
        bool contextPublished = true;
        bool itemPublished = false;

        void Publish()
        {
            MutableContext.Value = replacement;
            lock (_lifetimeGate)
                _contextRegistration = replacementRegistration;
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
        lock (_lifetimeGate)
            _hostDisposingContexts.Add(context);
        try { await context.DisposeAsync().ConfigureAwait(true); }
        finally
        {
            lock (_lifetimeGate)
                _hostDisposingContexts.Remove(context);
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

    private async Task<bool> DisposeRejectedAndReleaseContextAsync(
        IEditorContext context,
        EditorContextRegistration registration)
    {
        try
        {
            return await DisposeRejectedAndReturnFalseAsync(context).ConfigureAwait(true);
        }
        finally
        {
            _releaseContextHandler?.Invoke(this, registration);
        }
    }

    /// <summary>Requests terminal disposal of the tab and its owned context.</summary>
    public ValueTask DisposeAsync()
    {
        Action<EditorTabItem>? requestHostClose;
        lock (_lifetimeGate)
        {
            if (_hostCloseTask is not null)
                return new ValueTask(_hostCloseTask);
            requestHostClose = _terminalFailureHandler;
        }

        if (requestHostClose is not null)
        {
            requestHostClose(this);
            lock (_lifetimeGate)
            {
                if (_hostCloseTask is not null)
                    return new ValueTask(_hostCloseTask);
            }
        }

        return DisposeResourcesAsync();
    }

    internal ValueTask DisposeResourcesAsync()
    {
        Task? transition;
        Task? publicationDrain;
        Task? removalCompletion;
        TaskCompletionSource<object?>? completion = null;
        lock (_lifetimeGate)
        {
            if (_disposeTask is not null)
                return new ValueTask(_disposeTask);

            _closing = true;
            transition = _transitionTask;
            publicationDrain = _publicationDrain?.Task;
            removalCompletion = _removalCompletion?.Task;
            completion = new TaskCompletionSource<object?>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            _disposeTask = completion.Task;
        }

        _ = DisposeCoreAsync(
            transition,
            publicationDrain,
            removalCompletion,
            completion);
        return new ValueTask(completion.Task);
    }

    private async Task DisposeCoreAsync(
        Task? transition,
        Task? publicationDrain,
        Task? removalCompletion,
        TaskCompletionSource<object?> completion)
    {
        List<Exception>? failures = null;
        try
        {
            if (publicationDrain is not null)
            {
                try { await publicationDrain.ConfigureAwait(true); }
                catch (Exception ex) { RecordFailure(ref failures, ex); }
            }

            if (removalCompletion is not null)
            {
                try { await removalCompletion.ConfigureAwait(true); }
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
            if (failures is null)
                completion.TrySetResult(null);
            else
                completion.TrySetException(CreateFailure(failures));
        }
    }

    private static void DisposeSurface(IDisposable surface, ref List<Exception>? failures)
    {
        try { surface.Dispose(); }
        catch (Exception ex) { RecordFailure(ref failures, ex); }
    }
}

public sealed class EditorService : IOutputOperationLeaseProvider, IEditorContextCloseService
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
    private readonly object _contextRegistryGate = new();
    private readonly Dictionary<IEditorContext, (EditorTabItem Item, long Generation)> _contextItems =
        new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<IEditorContext, (EditorTabItem Item, long Generation)> _reservedContextItems =
        new(ReferenceEqualityComparer.Instance);
    private long _contextRegistrationGeneration;

    internal Action? BeforeInitialOwnerAttach { get; set; }

    internal Action? BeforeInitialContextClaimPublish { get; set; }


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
        if (!TryAddTabItem(item) && item.IsHostOwned && !ContainsTabItem(item))
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
        if (!item.IsHostOwned && !TryAttachOwner(item))
            return false;
        if (!item.TryBeginAttachment())
            return false;

        bool published = false;
        void Publish()
        {
            void Mutate()
            {
                beforeAdd?.Invoke();
                if (!item.TryCompleteAttachment())
                    return;

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
                && ContainsTabItem(item);
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
        _tabItems.Add(item);
    }

    internal bool ContainsTabItem(EditorTabItem item)
        => _tabItems.Contains(item);

    internal bool RemoveTabItem(EditorTabItem item)
    {
        // Reserve terminal removal without running any collection or reactive observers while
        // the lifetime gate is held.
        if (!item.TryBeginRemoval(out Task completion, out Task? publicationDrain))
            return false;

        if (publicationDrain is not null)
        {
            _ = CompleteRemovalAfterPublicationAsync(item, publicationDrain);
            ObserveDeferredTaskFailure(completion);
            return true;
        }

        try
        {
            return RemoveTabItemFacade(item);
        }
        finally
        {
            item.CompleteRemoval();
        }
    }

    private async Task CompleteRemovalAfterPublicationAsync(
        EditorTabItem item,
        Task publicationDrain)
    {
        Exception? failure = null;
        try
        {
            await publicationDrain.ConfigureAwait(false);
            _ = RemoveTabItemFacade(item);
        }
        catch (Exception ex)
        {
            failure = ex;
        }
        finally
        {
            item.CompleteRemoval(failure);
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
        if (!RemoveTabItem(item))
            ObserveDeferredTaskFailure(item.GetRemovalCompletion());
    }

    private void RemoveFailedTabItem(EditorTabItem item)
    {
        EditorContextCloseRequest request = RequestClose(item);
        ObserveDeferredTaskFailure(request.Completion);
    }

    private bool TryAttachOwner(EditorTabItem item)
    {
        IEditorContext? context = item.Context.Value;
        if (context is null)
            return false;

        lock (_contextRegistryGate)
        {
            if (_contextItems.ContainsKey(context) || _reservedContextItems.ContainsKey(context))
                return false;

            long generation = ++_contextRegistrationGeneration;
            var registration = new EditorContextRegistration(context, generation);
            BeforeInitialOwnerAttach?.Invoke();
            if (!item.TryAttachOwner(
                    RemoveFailedTabItem,
                    registration,
                    TryClaimContext,
                    PublishContextClaim,
                    ReleaseContext))
            {
                return false;
            }

            BeforeInitialContextClaimPublish?.Invoke();
            _contextItems.Add(context, (item, generation));
            return true;
        }
    }

    private EditorContextRegistration? TryClaimContext(
        EditorTabItem item,
        IEditorContext context)
    {
        lock (_contextRegistryGate)
        {
            if (_contextItems.ContainsKey(context) || _reservedContextItems.ContainsKey(context))
                return null;

            long generation = ++_contextRegistrationGeneration;
            _reservedContextItems.Add(context, (item, generation));
            return new EditorContextRegistration(context, generation);
        }
    }

    private bool PublishContextClaim(
        EditorTabItem item,
        EditorContextRegistration registration)
    {
        lock (_contextRegistryGate)
        {
            if (!_reservedContextItems.TryGetValue(registration.Context, out var reserved)
                || !ReferenceEquals(reserved.Item, item)
                || reserved.Generation != registration.Generation
                || _contextItems.ContainsKey(registration.Context))
            {
                return false;
            }

            _reservedContextItems.Remove(registration.Context);
            _contextItems.Add(registration.Context, (item, registration.Generation));
            return true;
        }
    }

    private void ReleaseContext(
        EditorTabItem item,
        EditorContextRegistration registration)
    {
        lock (_contextRegistryGate)
        {
            if (_contextItems.TryGetValue(registration.Context, out var current)
                && ReferenceEquals(current.Item, item)
                && current.Generation == registration.Generation)
            {
                _contextItems.Remove(registration.Context);
            }
            if (_reservedContextItems.TryGetValue(registration.Context, out var reserved)
                && ReferenceEquals(reserved.Item, item)
                && reserved.Generation == registration.Generation)
            {
                _reservedContextItems.Remove(registration.Context);
            }
        }
    }

    internal ExtensionProvider ExtensionProvider => _extensionProvider;

    internal void RequestContextShutdown(IEditorContext context)
    {
        EditorTabItem? item = GetRegisteredItem(context);
        if (item is null || item.IsHostDisposingContext(context))
            return;

        EditorContextCloseRequest request = RequestClose(item);
        if (request.Status != EditorContextCloseRequestStatus.NotOwned)
            ObserveDeferredTaskFailure(request.Completion);
    }

    public EditorContextCloseRequest RequestClose(IEditorContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        EditorTabItem? item = GetRegisteredItem(context);
        if (item is null)
        {
            return new EditorContextCloseRequest(
                EditorContextCloseRequestStatus.NotOwned,
                Task.CompletedTask);
        }

        return RequestClose(item);
    }

    private EditorTabItem? GetRegisteredItem(IEditorContext context)
    {
        lock (_contextRegistryGate)
        {
            return _contextItems.TryGetValue(context, out var registration)
                ? registration.Item
                : null;
        }
    }

    private EditorContextCloseRequest RequestClose(EditorTabItem item)
    {
        bool accepted = item.TryBeginHostClose(
            out Task completion,
            out TaskCompletionSource<object?>? completionSource);
        if (accepted)
        {
            _ = CompleteHostCloseAsync(item, completionSource!);
            ObserveDeferredTaskFailure(completion);
        }

        return new EditorContextCloseRequest(
            accepted
                ? EditorContextCloseRequestStatus.Accepted
                : EditorContextCloseRequestStatus.AlreadyClosing,
            completion);
    }

    private async Task CompleteHostCloseAsync(
        EditorTabItem item,
        TaskCompletionSource<object?> completion)
    {
        List<Exception>? failures = null;
        try { RemoveTabItem(item); }
        catch (Exception ex) { failures = [ex]; }

        try { await item.GetRemovalCompletion().ConfigureAwait(false); }
        catch (Exception ex) { (failures ??= []).Add(ex); }

        try { await item.DisposeResourcesAsync().ConfigureAwait(false); }
        catch (Exception ex) { (failures ??= []).Add(ex); }

        try { item.ReleaseContextRegistration(); }
        catch (Exception ex) { (failures ??= []).Add(ex); }

        if (failures is null)
            completion.TrySetResult(null);
        else
            completion.TrySetException(failures.Count == 1 ? failures[0] : new AggregateException(failures));
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
                if (!TryAddAndSelectTabItem(tabItem2) && tabItem2.IsHostOwned)
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

    private static void ObserveDeferredTaskFailure(Task task)
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
        EditorContextCloseRequest request = RequestClose(item);
        await request.Completion.ConfigureAwait(false);
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
