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
    private string? _hash;

    public EditorTabItem(IEditorContext context)
    {
        MutableContext = new ReactiveProperty<IEditorContext>(context);
        FilePath = Context.Select(ctxt => ctxt?.Object.Uri?.LocalPath)
            .ToReadOnlyReactivePropertySlim()!;
        FileName = FilePath.Select(Path.GetFileName)
            .Do(_ => _hash = null)
            .ToReadOnlyReactivePropertySlim()!;
        Extension = Context.Select(ctxt => ctxt?.Extension!)
            .ToReadOnlyReactivePropertySlim()!;
        Commands = Context.Select(ctxt => ctxt?.Commands)
            .ToReadOnlyReactivePropertySlim();
    }

    private IReactiveProperty<IEditorContext> MutableContext { get; }

    public IReadOnlyReactiveProperty<IEditorContext> Context => MutableContext;

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
        TaskCompletionSource<bool>? completion = null;
        bool rejected;
        lock (_lifetimeGate)
        {
            rejected = _closing || _transitionTask is not null;
            if (!rejected)
            {
                oldContext = MutableContext.Value;
                MutableContext.Value = null!;
                completion = new TaskCompletionSource<bool>(
                    TaskCreationOptions.RunContinuationsAsynchronously);
                _transitionTask = completion.Task;
            }
        }

        if (rejected)
            return new ValueTask<bool>(DisposeRejectedAndReturnFalseAsync(context));

        _ = CompleteReplacementAsync(oldContext!, context, completion!);
        return new ValueTask<bool>(completion!.Task);
    }

    private async Task CompleteReplacementAsync(
        IEditorContext oldContext,
        IEditorContext replacement,
        TaskCompletionSource<bool> completion)
    {
        bool published = false;
        try
        {
            await DisposeOwnedContextAsync(oldContext).ConfigureAwait(true);
            lock (_lifetimeGate)
            {
                if (!_closing)
                {
                    MutableContext.Value = replacement;
                    published = true;
                }
                _transitionTask = null;
            }
            if (!published)
                await DisposeRejectedContextAsync(replacement).ConfigureAwait(true);
            completion.TrySetResult(published);
        }
        catch (Exception ex)
        {
            lock (_lifetimeGate)
                _transitionTask = null;
            await DisposeRejectedContextAsync(replacement).ConfigureAwait(true);
            completion.TrySetException(ex);
        }
    }

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

    private static async Task DisposeRejectedContextAsync(IEditorContext context)
    {
        try
        {
            await context.DisposeAsync().ConfigureAwait(false);
        }
        catch
        {
            // The caller cannot retain ownership of a rejected context. Preserve the
            // original replacement/close outcome while ensuring it is not leaked.
        }
    }

    private static async Task<bool> DisposeRejectedAndReturnFalseAsync(IEditorContext context)
    {
        await DisposeRejectedContextAsync(context).ConfigureAwait(false);
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
        try
        {
            if (transition is not null)
            {
                try { await transition.ConfigureAwait(true); }
                catch { }
            }

            IEditorContext? context;
            lock (_lifetimeGate)
            {
                context = MutableContext.Value;
                MutableContext.Value = null!;
            }
            if (context is not null)
                await DisposeOwnedContextAsync(context).ConfigureAwait(true);

            MutableContext.Dispose();
            FilePath.Dispose();
            FileName.Dispose();
            Extension.Dispose();
            Commands.Dispose();
            IsSelected.Dispose();
        }
        catch (Exception ex)
        {
            completion.TrySetException(ex);
            return;
        }

        completion.TrySetResult(null);
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

    internal void AddTabItem(EditorTabItem item) => _tabItems.Add(item);

    internal bool RemoveTabItem(EditorTabItem item) => _tabItems.Remove(item);

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
        {
            RemoveTabItem(item);
            await item.DisposeAsync();
        }
    }

    public async ValueTask CloseTabItem(EditorTabItem item)
    {
        RemoveTabItem(item);
        await item.DisposeAsync();
    }
}
