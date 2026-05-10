using System.Runtime.InteropServices;
using Avalonia.Input;
using Beutl.Api.Services;
using Beutl.Services.PrimitiveImpls;
using Beutl.ViewModels;

namespace Beutl.Services;

public sealed class CommandPaletteService
{
    private static readonly OSPlatform s_currentPlatform =
        OperatingSystem.IsWindows() ? OSPlatform.Windows :
        OperatingSystem.IsLinux() ? OSPlatform.Linux :
        OSPlatform.OSX;

    private readonly ContextCommandManager? _commandManager;
    private readonly ICommandPaletteHandlerProvider _handlerProvider;
    private readonly Func<MenuBarViewModel?> _menuBarAccessor;
    private readonly Dictionary<Type, string> _categoryNameCache = new();

    public CommandPaletteService(
        ContextCommandManager? commandManager,
        ICommandPaletteHandlerProvider handlerProvider,
        Func<MenuBarViewModel?> menuBarAccessor)
    {
        _commandManager = commandManager;
        _handlerProvider = handlerProvider;
        _menuBarAccessor = menuBarAccessor;
    }

    public IReadOnlyList<PaletteCommand> EnumerateCommands()
    {
        var result = new List<PaletteCommand>();

        if (_commandManager != null)
        {
            EditorTabItem? activeTab = EditorService.Current.SelectedTabItem.Value;
            Type? activeEditorExtensionType = activeTab?.Extension.Value?.GetType();
            EditViewModel? activeEditor = activeTab?.Context.Value as EditViewModel;

            foreach (ContextCommandEntry entry in _commandManager.GetDefinitions())
            {
                if (entry.Definition.Name == MainViewExtension.ShowCommandPaletteCommandName)
                {
                    continue;
                }

                // 編集中のタブと一致しないエディタ拡張のコマンドは表示しない。
                if (typeof(EditorExtension).IsAssignableFrom(entry.ExtensionType)
                    && entry.ExtensionType != activeEditorExtensionType)
                {
                    continue;
                }

                // 開いていない ToolTab のコマンドはハンドラーを解決できないため除外する。
                if (typeof(ToolTabExtension).IsAssignableFrom(entry.ExtensionType)
                    && (activeEditor is null || activeEditor.DockHost.FindToolContext(entry.ExtensionType) is null))
                {
                    continue;
                }

                string displayName = string.IsNullOrEmpty(entry.Definition.DisplayName)
                    ? entry.Definition.Name
                    : entry.Definition.DisplayName!;
                string category = ResolveCategoryName(entry.ExtensionType);
                KeyGesture? gesture = entry.KeyGestures
                    .FirstOrDefault(i => i.Platform == s_currentPlatform)?.KeyGesture;

                // スナップショット時に解決したハンドラーを Execute / CanExecute / StateChanged 全てで共有し、
                // 列挙時と実行時で別インスタンスへ向くケースを防ぐ。タブ切替時は RebuildSnapshot で再列挙される。
                // ランタイムでハンドラーを解決できないコマンド（例: ContextCommandAttribute 方式のみで
                // 実装されたもの）はパレットから実行する手段がないため列挙対象から除外する。
                IContextCommandHandler? snapshotHandler = _handlerProvider.Resolve(entry.ExtensionType);
                if (snapshotHandler is null)
                {
                    continue;
                }

                ContextCommandEntry capturedEntry = entry;
                IContextCommandHandler handler = snapshotHandler;
                IObservable<System.Reactive.Unit>? stateChanged =
                    (handler as IContextCommandStateNotifier)?.CanExecuteChanged;

                result.Add(new PaletteCommand(
                    Id: $"{entry.ExtensionType.FullName}.{entry.Definition.Name}",
                    DisplayName: displayName,
                    Description: entry.Definition.Description,
                    CategoryName: category,
                    KeyGesture: gesture,
                    CanExecute: () => handler.CanExecute(new ContextCommandExecution(capturedEntry.Definition.Name)),
                    Execute: () =>
                    {
                        // スロットル窓や状態変化で表示と実行可否がずれる可能性があるため、
                        // 実行直前にもう一度 CanExecute を確認してから Execute する。
                        var execution = new ContextCommandExecution(capturedEntry.Definition.Name);
                        if (handler.CanExecute(execution))
                        {
                            handler.Execute(execution);
                        }
                    })
                {
                    StateChanged = stateChanged
                });
            }
        }

        AppendMenuCommands(result);

        return result;
    }

    private string ResolveCategoryName(Type extensionType)
    {
        if (_categoryNameCache.TryGetValue(extensionType, out string? cached))
        {
            return cached;
        }

        string name = extensionType.Name;
        Extension? matched = ExtensionProvider.Current.AllExtensions
            .FirstOrDefault(i => i.GetType() == extensionType);
        if (matched != null && !string.IsNullOrEmpty(matched.DisplayName))
        {
            name = matched.DisplayName;
        }

        _categoryNameCache[extensionType] = name;
        return name;
    }

    private void AppendMenuCommands(List<PaletteCommand> commands)
    {
        MenuBarViewModel? menuBar = _menuBarAccessor();
        if (menuBar == null)
        {
            return;
        }

        string category = Strings.CommandPalette_MenuCategory;

        foreach (MenuBarViewModel.PaletteMenuCommand menuCommand in menuBar.EnumeratePaletteCommands())
        {
            System.Windows.Input.ICommand command = menuCommand.Command;
            commands.Add(new PaletteCommand(
                Id: menuCommand.Id,
                DisplayName: menuCommand.DisplayName,
                Description: null,
                CategoryName: category,
                KeyGesture: null,
                CanExecute: () => command.CanExecute(null),
                Execute: () =>
                {
                    if (command.CanExecute(null))
                        command.Execute(null);
                }));
        }
    }
}
