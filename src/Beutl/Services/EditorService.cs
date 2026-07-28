using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using Beutl.Api.Services;
using Beutl.Configuration;
using Beutl.Editor.VersionControl;
using Reactive.Bindings;

namespace Beutl.Services;

public sealed class EditorTabItem : IAsyncDisposable
{
    private string? _hash;

    public EditorTabItem(IEditorContext context)
    {
        Context = new ReactiveProperty<IEditorContext>(context);
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

    public IReactiveProperty<IEditorContext> Context { get; }

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

    public async ValueTask DisposeAsync()
    {
        await Context.Value.DisposeAsync();
        Context.Value = null!;

        Context.Dispose();
        FilePath.Dispose();
        FileName.Dispose();
        Extension.Dispose();
        Commands.Dispose();
        IsSelected.Dispose();
    }
}

public sealed class EditorService
{
    private readonly CoreList<EditorTabItem> _tabItems;
    private readonly ExtensionProvider _extensionProvider;
    private int _activeOutputOperations;

    public EditorService(ExtensionProvider extensionProvider)
    {
        ArgumentNullException.ThrowIfNull(extensionProvider);

        _extensionProvider = extensionProvider;
        _tabItems = new() { ResetBehavior = ResetBehavior.Remove };
    }

    public ICoreList<EditorTabItem> TabItems => _tabItems;

    public IReactiveProperty<EditorTabItem?> SelectedTabItem { get; } = new ReactivePropertySlim<EditorTabItem?>();

    internal IProjectVersionControlService? ProjectVersionControlService { get; set; }

    internal IVersionControlRestoreCoordinator? VersionControlRestoreCoordinator { get; set; }

    internal bool IsExportRunning => Volatile.Read(ref _activeOutputOperations) > 0;

    internal void NotifyOutputStarted()
    {
        Interlocked.Increment(ref _activeOutputOperations);
    }

    internal void NotifyOutputFinished()
    {
        while (true)
        {
            int current = Volatile.Read(ref _activeOutputOperations);
            if (current == 0
                || Interlocked.CompareExchange(
                    ref _activeOutputOperations,
                    current - 1,
                    current) == current)
            {
                return;
            }
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
            tabItem.IsSelected.Value = true;
            SelectedTabItem.Value = tabItem;
        }
        else
        {
            EditorExtension? ext = _extensionProvider.MatchEditorExtension(path);

            if (ext?.TryCreateContext(obj, new EditorContextServices(this, _extensionProvider), out IEditorContext? context) == true)
            {
                var tabItem2 = new EditorTabItem(context) { IsSelected = { Value = true } };
                TabItems.Add(tabItem2);
                SelectedTabItem.Value = tabItem2;
            }
        }
    }

    public async ValueTask CloseTabItem(CoreObject obj)
    {
        if (TryGetTabItem(obj, out EditorTabItem? item))
        {
            TabItems.Remove(item);
            await item.DisposeAsync();
        }
    }

    public async ValueTask CloseTabItem(EditorTabItem item)
    {
        TabItems.Remove(item);
        await item.DisposeAsync();
    }
}
