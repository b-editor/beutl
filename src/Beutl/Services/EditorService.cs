using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using Beutl.Api.Services;
using Beutl.Configuration;
using Reactive.Bindings;

namespace Beutl.Services;

public sealed class EditorTabItem : IAsyncDisposable
{
    private readonly object _lifetimeGate = new();
    private static readonly AsyncLocal<IReadOnlySet<EditorTabItem>?> s_ownedDisposals = new();
    private Task? _disposeTask;
    private Task? _transitionTask;
    private bool _closing;
    private bool _contextDisposeAttempted;
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
            rejected = _closing || _transitionTask is not null;
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
            MutableContext.Value = null;
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
                MutableContext.Value = replacement;
                published = true;
            }
            catch (Exception ex)
            {
                RecordFailure(ref failures, ex);
            }
        }

        bool closeWonAfterPublication;
        lock (_lifetimeGate)
        {
            if (failures is not null)
            {
                _closing = true;
                _contextDisposeAttempted = true;
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
        if (failures is null && published && !closeWonAfterPublication)
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

    private static void RecordFailure(ref List<Exception>? failures, Exception exception)
        => (failures ??= []).Add(exception);

    private static Exception CreateFailure(List<Exception> failures)
        => failures.Count == 1 ? failures[0] : new AggregateException(failures);

    private async Task DisposeOwnedContextAsync(IEditorContext context)
    {
        IReadOnlySet<EditorTabItem>? previous = s_ownedDisposals.Value;
        var current = previous is null
            ? new HashSet<EditorTabItem>(ReferenceEqualityComparer.Instance)
            : new HashSet<EditorTabItem>(previous, ReferenceEqualityComparer.Instance);
        current.Add(this);
        s_ownedDisposals.Value = current;
        try { await context.DisposeAsync().ConfigureAwait(true); }
        finally { s_ownedDisposals.Value = previous; }
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
        TaskCompletionSource<object?>? completion = null;
        bool reentrant = s_ownedDisposals.Value?.Contains(this) == true;
        lock (_lifetimeGate)
        {
            if (_disposeTask is not null)
            {
                return reentrant ? ValueTask.CompletedTask : new ValueTask(_disposeTask);
            }

            _closing = true;
            transition = _transitionTask;
            completion = new TaskCompletionSource<object?>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            _disposeTask = completion.Task;
        }

        _ = DisposeCoreAsync(transition, completion);
        return reentrant ? ValueTask.CompletedTask : new ValueTask(completion.Task);
    }

    private async Task DisposeCoreAsync(
        Task? transition,
        TaskCompletionSource<object?> completion)
    {
        List<Exception>? failures = null;
        IReadOnlySet<EditorTabItem>? previous = s_ownedDisposals.Value;
        var current = previous is null
            ? new HashSet<EditorTabItem>(ReferenceEqualityComparer.Instance)
            : new HashSet<EditorTabItem>(previous, ReferenceEqualityComparer.Instance);
        current.Add(this);
        s_ownedDisposals.Value = current;
        try
        {
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
            s_ownedDisposals.Value = previous;
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

public sealed class EditorService
{
    private readonly CoreList<EditorTabItem> _tabItems;
    private readonly ExtensionProvider _extensionProvider;

    public EditorService(ExtensionProvider extensionProvider)
    {
        ArgumentNullException.ThrowIfNull(extensionProvider);

        _extensionProvider = extensionProvider;
        _tabItems = new() { ResetBehavior = ResetBehavior.Remove };
    }

    public ICoreReadOnlyList<EditorTabItem> TabItems => _tabItems;

    internal void ClearTabItems() => _tabItems.Clear();

    internal void AddTabItem(EditorTabItem item)
    {
        item.AttachOwner(RemoveFailedTabItem);
        _tabItems.Add(item);
    }

    internal bool RemoveTabItem(EditorTabItem item)
    {
        List<Exception>? failures = null;
        bool wasPresent = _tabItems.Contains(item);
        bool removed = false;
        try
        {
            removed = _tabItems.Remove(item);
        }
        catch (Exception ex)
        {
            failures = [ex];
            removed = wasPresent && !_tabItems.Contains(item);
        }
        if (removed && ReferenceEquals(SelectedTabItem.Value, item))
        {
            try { SelectedTabItem.Value = null; }
            catch (Exception ex) { (failures ??= []).Add(ex); }
        }
        if (failures is not null)
            throw failures.Count == 1 ? failures[0] : new AggregateException(failures);
        return removed;
    }

    private void RemoveFailedTabItem(EditorTabItem item)
        => RemoveTabItem(item);

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
            tabItem.IsSelected.Value = true;
            SelectedTabItem.Value = tabItem;
        }
        else
        {
            EditorExtension? ext = _extensionProvider.MatchEditorExtension(path);

            if (ext?.TryCreateContext(obj, new EditorContextServices(this, _extensionProvider), out IEditorContext? context) == true)
            {
                var tabItem2 = new EditorTabItem(context) { IsSelected = { Value = true } };
                AddTabItem(tabItem2);
                SelectedTabItem.Value = tabItem2;
            }
        }
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
}
