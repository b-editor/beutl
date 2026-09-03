using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using Beutl.Api.Services;
using Beutl.Configuration;
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
    private TaskCompletionSource? _membershipDrain;
    private TaskCompletionSource? _removalCompletion;
    private bool _closing;
    private bool _contextDisposeAttempted;
    private bool _publicationActive;
    private MembershipState _membershipState;
    private Task? _hostCloseTask;
    private EditorContextHostToken? _ownerHostToken;
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
        EditorContextHostToken hostToken,
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

            _ownerHostToken = hostToken;
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

    internal bool IsOwnedBy(EditorContextHostToken hostToken)
    {
        lock (_lifetimeGate)
            return ReferenceEquals(_ownerHostToken, hostToken);
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

            _membershipState = MembershipState.AttachmentCommitted;
            return true;
        }
    }

    // Keep attachment commit distinct from physical insertion so terminal removal can cancel a
    // pending add; collection observers still run outside the lifetime gate.
    internal bool TryBeginPhysicalAdd()
    {
        lock (_lifetimeGate)
        {
            if (_closing || _membershipState != MembershipState.AttachmentCommitted)
                return false;

            _membershipState = MembershipState.PhysicalAdding;
            var drain = new TaskCompletionSource(
                TaskCreationOptions.RunContinuationsAsynchronously);
            _membershipDrain = drain;
            return true;
        }
    }

    internal void CompletePhysicalAdd(bool added)
    {
        TaskCompletionSource? drain;
        lock (_lifetimeGate)
        {
            if (_membershipState == MembershipState.PhysicalAdding)
            {
                _membershipState = added
                    ? MembershipState.Attached
                    : MembershipState.AttachmentCommitted;
            }
            drain = _membershipDrain;
            _membershipDrain = null;
        }
        drain?.TrySetResult();
    }

    internal EditorContextCloseRequestStatus TryBeginHostClose(
        EditorContextHostToken expectedHostToken,
        out Task completion,
        out TaskCompletionSource<object?>? completionSource)
    {
        lock (_lifetimeGate)
        {
            if (!ReferenceEquals(_ownerHostToken, expectedHostToken))
            {
                completion = Task.CompletedTask;
                completionSource = null;
                return EditorContextCloseRequestStatus.NotOwned;
            }

            if (_hostCloseTask is not null)
            {
                completion = _hostCloseTask;
                completionSource = null;
                return EditorContextCloseRequestStatus.AlreadyClosing;
            }

            completionSource = new TaskCompletionSource<object?>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            completion = completionSource.Task;
            _hostCloseTask = completion;
            _closing = true;
            return EditorContextCloseRequestStatus.Accepted;
        }
    }

    internal bool TryBeginRemoval(
        out Task completion,
        out Task? publicationDrain,
        out Task? membershipDrain)
    {
        lock (_lifetimeGate)
        {
            _closing = true;
            publicationDrain = _publicationDrain?.Task;
            membershipDrain = _membershipDrain?.Task;
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
        AttachmentCommitted,
        PhysicalAdding,
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
    /// A context already active in this tab, claimed by this or another tab, or bound to another
    /// editor host is rejected without being consumed. All other supplied contexts are consumed
    /// for both return values.
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
        Task? membershipDrain;
        Task? removalCompletion;
        TaskCompletionSource<object?>? completion = null;
        lock (_lifetimeGate)
        {
            if (_disposeTask is not null)
                return new ValueTask(_disposeTask);

            _closing = true;
            transition = _transitionTask;
            publicationDrain = _publicationDrain?.Task;
            membershipDrain = _membershipDrain?.Task;
            removalCompletion = _removalCompletion?.Task;
            completion = new TaskCompletionSource<object?>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            _disposeTask = completion.Task;
        }

        _ = DisposeCoreAsync(
            transition,
            publicationDrain,
            membershipDrain,
            removalCompletion,
            completion);
        return new ValueTask(completion.Task);
    }

    private async Task DisposeCoreAsync(
        Task? transition,
        Task? publicationDrain,
        Task? membershipDrain,
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

            if (membershipDrain is not null)
            {
                try { await membershipDrain.ConfigureAwait(true); }
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

public sealed class EditorService : IEditorContextCloseService
{
    private readonly EditorContextHostToken _hostToken = new();
    private readonly CoreList<EditorTabItem> _tabItems;
    private readonly ExtensionProvider _extensionProvider;
    private readonly object _tabAdmissionGate = new();
    private readonly SemaphoreSlim _tabReconciliationGate = new(1, 1);
    private TaskCompletionSource? _tabAdmissionDrain;
    private int _activeTabAdmissions;
    private bool _tabAdmissionClosed;
    private readonly object _contextRegistryGate = new();
    private readonly Dictionary<IEditorContext, (EditorTabItem Item, long Generation)> _contextItems =
        new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<IEditorContext, (EditorTabItem Item, long Generation)> _reservedContextItems =
        new(ReferenceEqualityComparer.Instance);
    private long _contextRegistrationGeneration;

    internal Action? BeforeInitialOwnerAttach { get; set; }

    internal Action? BeforeInitialContextClaimPublish { get; set; }

    internal Action? BeforeContextCloseAdmission { get; set; }

    internal Action? BeforeHostCloseStart { get; set; }

    internal Action? BeforePhysicalAdd { get; set; }

    internal Action<IEditorContext>? BeforeActivationTabConstruction { get; set; }

    public EditorService(ExtensionProvider extensionProvider)
    {
        ArgumentNullException.ThrowIfNull(extensionProvider);

        _extensionProvider = extensionProvider;
        _tabItems = new() { ResetBehavior = ResetBehavior.Remove };
    }

    /// <summary>Gets the opaque identity retained by contexts created for this host.</summary>
    public EditorContextHostToken HostToken => _hostToken;

    public ICoreReadOnlyList<EditorTabItem> TabItems => _tabItems;

    internal async ValueTask ClearTabItemsAsync()
        => await ReconcileTabItemsAsync([]);

    internal async ValueTask ReconcileTabItemsAsync(IReadOnlyList<CoreObject> items)
    {
        ArgumentNullException.ThrowIfNull(items);
        await _tabReconciliationGate.WaitAsync();
        Task admissionDrain = CloseTabAdmission();
        try
        {
            await admissionDrain;
            List<Exception>? failures = null;
            foreach (EditorTabItem item in _tabItems.ToArray())
            {
                try { await CloseTabItem(item); }
                catch (Exception ex) { (failures ??= []).Add(ex); }
            }

            foreach (CoreObject item in items)
            {
                try { ActivateTabItemCore(item); }
                catch (Exception ex) { (failures ??= []).Add(ex); }
            }

            if (failures is not null)
                throw failures.Count == 1 ? failures[0] : new AggregateException(failures);
        }
        finally
        {
            OpenTabAdmission();
            _tabReconciliationGate.Release();
        }
    }

    private bool TryEnterTabAdmission()
    {
        lock (_tabAdmissionGate)
        {
            if (_tabAdmissionClosed)
                return false;

            _activeTabAdmissions++;
            return true;
        }
    }

    private void ExitTabAdmission()
    {
        TaskCompletionSource? drain = null;
        lock (_tabAdmissionGate)
        {
            _activeTabAdmissions--;
            if (_tabAdmissionClosed && _activeTabAdmissions == 0)
            {
                drain = _tabAdmissionDrain;
                _tabAdmissionDrain = null;
            }
        }

        drain?.TrySetResult();
    }

    private Task CloseTabAdmission()
    {
        lock (_tabAdmissionGate)
        {
            _tabAdmissionClosed = true;
            if (_activeTabAdmissions == 0)
                return Task.CompletedTask;

            _tabAdmissionDrain ??= new TaskCompletionSource(
                TaskCreationOptions.RunContinuationsAsynchronously);
            return _tabAdmissionDrain.Task;
        }
    }

    private void OpenTabAdmission()
    {
        lock (_tabAdmissionGate)
        {
            _tabAdmissionClosed = false;
        }
    }

    internal void AddTabItem(EditorTabItem item)
    {
        if (!TryAddTabItem(item)
            && item.IsOwnedBy(_hostToken)
            && !ContainsTabItem(item))
            ObserveDeferredTabDisposal(item.DisposeAsync().AsTask());
    }

    internal bool TryAddTabItem(EditorTabItem item)
        => TryAddTabItemWithAdmission(
            item,
            select: false,
            beforeAdd: null,
            beforeSelection: null);

    internal bool TryAddTabItem(EditorTabItem item, Action beforeAdd)
        => TryAddTabItemWithAdmission(
            item,
            select: false,
            beforeAdd: beforeAdd,
            beforeSelection: null);

    internal bool TryAddAndSelectTabItem(EditorTabItem item, Action? beforeSelection = null)
        => TryAddTabItemWithAdmission(
            item,
            select: true,
            beforeAdd: null,
            beforeSelection: beforeSelection);

    private bool TryAddTabItemWithAdmission(
        EditorTabItem item,
        bool select,
        Action? beforeAdd,
        Action? beforeSelection)
    {
        if (!TryEnterTabAdmission())
            return false;

        try
        {
            return TryAddTabItemCore(item, select, beforeAdd, beforeSelection);
        }
        finally
        {
            ExitTabAdmission();
        }
    }

    private bool TryAddTabItemCore(
        EditorTabItem item,
        bool select,
        Action? beforeAdd,
        Action? beforeSelection)
    {
        if (item.Context.Value is not { } context || !HasMatchingHostToken(context))
            return false;

        if (!item.IsHostOwned && !TryAttachOwner(item))
            return false;
        if (!item.TryBeginAttachment())
            return false;

        bool published = false;
        bool itemAdded = false;
        void Publish()
        {
            void Mutate()
            {
                beforeAdd?.Invoke();
                if (!item.TryCompleteAttachment())
                    return;

                BeforePhysicalAdd?.Invoke();
                if (!AddTabItemCore(item))
                    return;

                itemAdded = true;
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
            published = itemPublished && contextPublished && itemAdded;
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

    internal bool AddTabItemCore(EditorTabItem item)
    {
        if (!item.TryBeginPhysicalAdd())
            return false;

        bool added = false;
        try
        {
            _tabItems.Add(item);
            added = _tabItems.Contains(item);
            return added;
        }
        finally
        {
            item.CompletePhysicalAdd(added);
        }
    }

    internal bool ContainsTabItem(EditorTabItem item)
        => _tabItems.Contains(item);

    internal bool RequestTabRemoval(EditorTabItem item)
    {
        // Reserve terminal removal without running any collection or reactive observers while
        // the lifetime gate is held.
        if (!item.TryBeginRemoval(
                out Task completion,
                out Task? publicationDrain,
                out Task? membershipDrain))
            return false;

        if (publicationDrain is not null || membershipDrain is not null)
        {
            _ = CompleteRemovalAfterDrainsAsync(item, publicationDrain, membershipDrain);
            ObserveDeferredTaskFailure(completion);
            return false;
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

    internal async ValueTask<bool> RemoveTabItemAsync(EditorTabItem item)
    {
        bool wasPresent = ContainsTabItem(item);
        bool removed = RequestTabRemoval(item);
        if (!removed)
            await item.GetRemovalCompletion().ConfigureAwait(false);
        return wasPresent && (removed || !ContainsTabItem(item));
    }

    private async Task CompleteRemovalAfterDrainsAsync(
        EditorTabItem item,
        Task? publicationDrain,
        Task? membershipDrain)
    {
        Exception? failure = null;
        try
        {
            if (publicationDrain is not null)
                await publicationDrain.ConfigureAwait(false);
            if (membershipDrain is not null)
                await membershipDrain.ConfigureAwait(false);
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
        if (!RequestTabRemoval(item))
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
        if (context is null || !HasMatchingHostToken(context))
            return false;

        lock (_contextRegistryGate)
        {
            if (_contextItems.ContainsKey(context) || _reservedContextItems.ContainsKey(context))
                return false;

            long generation = ++_contextRegistrationGeneration;
            var registration = new EditorContextRegistration(context, generation);
            BeforeInitialOwnerAttach?.Invoke();
            if (!item.TryAttachOwner(
                    _hostToken,
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
        if (!HasMatchingHostToken(context))
            return null;

        lock (_contextRegistryGate)
        {
            if (_contextItems.ContainsKey(context) || _reservedContextItems.ContainsKey(context))
                return null;

            long generation = ++_contextRegistrationGeneration;
            _reservedContextItems.Add(context, (item, generation));
            return new EditorContextRegistration(context, generation);
        }
    }

    private bool HasMatchingHostToken(IEditorContext context)
    {
        return ReferenceEquals(context.CloseService.HostToken, _hostToken);
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

    internal void RequestContextShutdown(IEditorContext context)
    {
        var registration = GetRegisteredItem(context);
        if (registration is null || registration.Value.Item.IsHostDisposingContext(context))
            return;

        BeforeContextCloseAdmission?.Invoke();
        EditorContextCloseRequest request = RequestClose(context, registration.Value);
        if (request.Status != EditorContextCloseRequestStatus.NotOwned)
            ObserveDeferredTaskFailure(request.Completion);
    }

    public EditorContextCloseRequest RequestClose(IEditorContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        var registration = GetRegisteredItem(context);
        if (registration is null)
        {
            return new EditorContextCloseRequest(
                EditorContextCloseRequestStatus.NotOwned,
                Task.CompletedTask);
        }

        BeforeContextCloseAdmission?.Invoke();
        return RequestClose(context, registration.Value);
    }

    private (EditorTabItem Item, long Generation)? GetRegisteredItem(IEditorContext context)
    {
        lock (_contextRegistryGate)
        {
            return _contextItems.TryGetValue(context, out var registration)
                ? registration
                : null;
        }
    }

    private EditorContextCloseRequest RequestClose(
        IEditorContext context,
        (EditorTabItem Item, long Generation) registration)
    {
        EditorContextCloseRequestStatus status;
        Task completion;
        TaskCompletionSource<object?>? completionSource;
        lock (_contextRegistryGate)
        {
            if (!_contextItems.TryGetValue(context, out var current)
                || !ReferenceEquals(current.Item, registration.Item)
                || current.Generation != registration.Generation)
            {
                return new EditorContextCloseRequest(
                    EditorContextCloseRequestStatus.NotOwned,
                    Task.CompletedTask);
            }

            status = registration.Item.TryBeginHostClose(
                _hostToken,
                out completion,
                out completionSource);
        }

        return StartHostClose(
            registration.Item,
            status,
            completion,
            completionSource);
    }

    private EditorContextCloseRequest RequestClose(EditorTabItem item)
    {
        EditorContextCloseRequestStatus status = item.TryBeginHostClose(
            _hostToken,
            out Task completion,
            out TaskCompletionSource<object?>? completionSource);
        return StartHostClose(item, status, completion, completionSource);
    }

    private EditorContextCloseRequest StartHostClose(
        EditorTabItem item,
        EditorContextCloseRequestStatus status,
        Task completion,
        TaskCompletionSource<object?>? completionSource)
    {
        if (status == EditorContextCloseRequestStatus.Accepted)
        {
            BeforeHostCloseStart?.Invoke();
            _ = CompleteHostCloseAsync(item, completionSource!);
            ObserveDeferredTaskFailure(completion);
        }

        return new EditorContextCloseRequest(
            status,
            completion);
    }

    private async Task CompleteHostCloseAsync(
        EditorTabItem item,
        TaskCompletionSource<object?> completion)
    {
        List<Exception>? failures = null;
        try { await RemoveTabItemAsync(item).ConfigureAwait(false); }
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

    public bool TryGetTabItem(CoreObject obj, [NotNullWhen(true)] out EditorTabItem? result)
    {
        result = TabItems.FirstOrDefault(i => i.Context.Value?.Object == obj);

        return result != null;
    }

    public void ActivateTabItem(CoreObject obj)
    {
        if (!TryEnterTabAdmission())
            return;

        try
        {
            ActivateTabItemCore(obj);
        }
        finally
        {
            ExitTabAdmission();
        }
    }

    private void ActivateTabItemCore(CoreObject obj)
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
                EditorTabItem? tabItem2 = null;
                bool added = false;
                try
                {
                    BeforeActivationTabConstruction?.Invoke(context);
                    tabItem2 = new EditorTabItem(context);
                    tabItem2.IsSelected.Value = true;
                    added = TryAddTabItemCore(
                        tabItem2,
                        select: true,
                        beforeAdd: null,
                        beforeSelection: null);
                }
                finally
                {
                    if (!added)
                    {
                        if (tabItem2 is not null)
                            ObserveDeferredTabDisposal(tabItem2.DisposeAsync().AsTask());
                        else
                            ObserveDeferredContextDisposal(context);
                    }
                }
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

    private static void ObserveDeferredContextDisposal(IEditorContext context)
    {
        Task disposal;
        try
        {
            disposal = context.DisposeAsync().AsTask();
        }
        catch (Exception ex)
        {
            disposal = Task.FromException(ex);
        }

        ObserveDeferredTabDisposal(disposal);
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
}
