using System.Diagnostics.CodeAnalysis;

using Beutl.Api.Services;

using Beutl.Configuration;
using Beutl.Framework;

using Microsoft.Extensions.DependencyInjection;

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
    public EditorTabItem(IEditorContext context, TabOpenMode tabOpenMode)
    {
        Context = new ReactiveProperty<IEditorContext>(context);
        FilePath = Context.Select(ctxt => ctxt?.EdittingFile!)
            .ToReadOnlyReactivePropertySlim()!;
        FileName = Context.Select(ctxt => Path.GetFileName(ctxt?.EdittingFile)!)
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

    public ICoreList<EditorTabItem> TabItems => _tabItems;

    public IReactiveProperty<EditorTabItem?> SelectedTabItem { get; } = new ReactiveProperty<EditorTabItem?>();

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
                ExtensionProvider extensionProvider = ServiceLocator.Current.GetRequiredService<ExtensionProvider>();
                OutputService output = ServiceLocator.Current.GetRequiredService<OutputService>();
                EditorExtension? ext = extensionProvider.MatchEditorExtension(file);

                if (ext?.TryCreateContext(file, out IEditorContext? context) == true)
                {
                    context.IsEnabled.Value = !output.Items.Any(x => x.Context.TargetFile == file && x.Context.IsEncoding.Value);
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
