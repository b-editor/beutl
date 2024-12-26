using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;
using System.Security.Cryptography;

using Beutl.Api.Services;

using Beutl.Configuration;

using Reactive.Bindings;

namespace Beutl.Services;

public sealed class EditorTabItem : IDisposable
{
    private string? _hash;

    public EditorTabItem(IEditorContext context)
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

    public void ActivateTabItem(string? file)
    {
        if (File.Exists(file))
        {
            ViewConfig viewConfig = GlobalConfiguration.Instance.ViewConfig;
            viewConfig.UpdateRecentFile(file);

            if (TryGetTabItem(file, out EditorTabItem? tabItem))
            {
                tabItem.IsSelected.Value = true;
                SelectedTabItem.Value = tabItem;
            }
            else
            {
                EditorExtension? ext = ExtensionProvider.Current.MatchEditorExtension(file);

                if (ext?.TryCreateContext(file, out IEditorContext? context) == true)
                {
                    // TODO: エンコード中にファイルが変更される可能性
                    // context.IsEnabled.Value = !OutputService.Current.Items.Any(x => x.Context.TargetFile == file && x.Context.IsEncoding.Value);
                    var tabItem2 = new EditorTabItem(context)
                    {
                        IsSelected =
                        {
                            Value = true
                        }
                    };
                    TabItems.Add(tabItem2);
                    SelectedTabItem.Value = tabItem2;
                }
            }
        }
    }

    public void CloseTabItem(string? file)
    {
        if (TryGetTabItem(file, out EditorTabItem? item))
        {
            TabItems.Remove(item);
            item.Dispose();
        }
    }
}
