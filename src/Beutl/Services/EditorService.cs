using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;
using System.Security.Cryptography;

using Beutl.Api.Services;

using Beutl.Configuration;

using Reactive.Bindings;

namespace Beutl.Services;

public enum TabOpenMode
{
    // プロジェクトを開いたときに開かれた
    FromProject,

    // 手動で開かれた。
    // File>OpenやCtrl+O、Ctrl+Shift+Oなど
    YourSelf,
}

public sealed class EditorTabItem : IDisposable
{
    private string? _hash;

    public EditorTabItem(IEditorContext context, TabOpenMode tabOpenMode)
    {
        Context = new ReactiveProperty<IEditorContext>(context);
        FilePath = Context.Select(ctxt => ctxt?.EdittingFile!)
            .ToReadOnlyReactivePropertySlim()!;
        FileName = Context.Select(ctxt => Path.GetFileName(ctxt?.EdittingFile)!)
            .Do(_ => _hash = null)
            .ToReadOnlyReactivePropertySlim()!;
        Extension = Context.Select(ctxt => ctxt?.Extension!)
            .ToReadOnlyReactivePropertySlim()!;
        Commands = Context.Select(ctxt => ctxt?.Commands)
            .ToReadOnlyReactivePropertySlim();
        TabOpenMode = tabOpenMode;
    }

    public IReactiveProperty<IEditorContext> Context { get; }

    public TabOpenMode TabOpenMode { get; }

    public int Order { get; set; } = -1;

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

    public void Dispose()
    {
        Context.Value.Dispose();
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

    public EditorService()
    {
        _tabItems = new()
        {
            ResetBehavior = ResetBehavior.Remove
        };
    }

    public static EditorService Current { get; } = new();

    public ICoreList<EditorTabItem> TabItems => _tabItems;

    public IReactiveProperty<EditorTabItem?> SelectedTabItem { get; } = new ReactivePropertySlim<EditorTabItem?>();

    public bool TryGetTabItem(string? file, [NotNullWhen(true)] out EditorTabItem? result)
    {
        result = TabItems.FirstOrDefault(i => i.FilePath.Value == file);

        return result != null;
    }

    public void ActivateTabItem(string? file, TabOpenMode tabOpenMode)
    {
        if (File.Exists(file))
        {
            ViewConfig viewConfig = GlobalConfiguration.Instance.ViewConfig;
            viewConfig.UpdateRecentFile(file);

            if (TryGetTabItem(file, out EditorTabItem? tabItem))
            {
                tabItem.IsSelected.Value = true;
            }
            else
            {
                EditorExtension? ext = ExtensionProvider.Current.MatchEditorExtension(file);

                if (ext?.TryCreateContext(file, out IEditorContext? context) == true)
                {
                    context.IsEnabled.Value = !OutputService.Current.Items.Any(x => x.Context.TargetFile == file && x.Context.IsEncoding.Value);
                    TabItems.Add(new EditorTabItem(context, tabOpenMode)
                    {
                        IsSelected =
                        {
                            Value = true
                        }
                    });
                }
            }
        }
    }

    public void CloseTabItem(string? file, TabOpenMode tabOpenMode)
    {
        if (TryGetTabItem(file, out EditorTabItem? item) && item.TabOpenMode == tabOpenMode)
        {
            TabItems.Remove(item);
            item.Dispose();
        }
    }
}
