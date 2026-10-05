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
    private readonly EditorService _editorService;
    private readonly ExtensionProvider _extensionProvider;
    private readonly IEditorContextServices _contextServices;
    private readonly Dictionary<string, string> _categoryNameCache = new(StringComparer.Ordinal);

    public CommandPaletteService(
        ContextCommandManager? commandManager,
        ICommandPaletteHandlerProvider handlerProvider,
        Func<MenuBarViewModel?> menuBarAccessor,
        EditorService editorService,
        ExtensionProvider extensionProvider)
    {
        _commandManager = commandManager;
        _handlerProvider = handlerProvider;
        _menuBarAccessor = menuBarAccessor;
        _editorService = editorService;
        _extensionProvider = extensionProvider;
        _contextServices = new EditorContextServices(editorService, extensionProvider);
    }

    public IReadOnlyList<PaletteCommand> EnumerateCommands()
    {
        var result = new List<PaletteCommand>();

        if (_commandManager != null)
        {
            EditorTabItem? activeTab = _editorService.SelectedTabItem.Value;
            IEditorContext? activeContext = activeTab?.Context.Value;
            Type? activeEditorExtensionType = activeTab?.Extension.Value?.GetType();
            EditViewModel? activeEditor = activeTab?.Context.Value as EditViewModel;
            var extensionStateChanges = new Dictionary<ExtensionId, IObservable<Unit>>();
            IReadOnlyList<ExtensionDescriptor> extensionDescriptors = _extensionProvider.GetDescriptors<ViewExtension>();

            foreach (ContextCommandEntry entry in _commandManager.GetDefinitions())
            {
                string commandName = entry.Definition.Name;
                if (entry.Definition.Name == MainViewExtension.ShowCommandPaletteCommandName)
                {
                    continue;
                }

                // 編集中のタブと一致しないエディタ拡張のコマンドは表示しない。
                if (entry.Definition.Scope == ContextCommandScope.Context
                    && typeof(EditorExtension).IsAssignableFrom(entry.ExtensionType)
                    && entry.ExtensionType != activeEditorExtensionType)
                {
                    continue;
                }

                // 開いていない ToolTab のコマンドはハンドラーを解決できないため除外する。
                if (entry.Definition.Scope == ContextCommandScope.Context
                    && typeof(ToolTabExtension).IsAssignableFrom(entry.ExtensionType)
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

                if (entry.Definition.Scope == ContextCommandScope.Extension)
                {
                    string extensionTypeName = entry.ExtensionType.AssemblyQualifiedName
                        ?? entry.ExtensionType.FullName ?? entry.ExtensionType.Name;
                    ExtensionDescriptor? descriptor = extensionDescriptors
                        .FirstOrDefault(i => i.TypeName == extensionTypeName);
                    if (descriptor is null
                        || !_extensionProvider.TryAcquire<ViewExtension>(descriptor.Id, out var lease))
                    {
                        continue;
                    }

                    bool hasStateNotifier;
                    using (lease)
                    {
                        if (lease.Extension is not IContextCommandHandler)
                            continue;
                        hasStateNotifier = lease.Extension is IContextCommandStateNotifier;
                    }

                    ExtensionId id = descriptor.Id;
                    IObservable<Unit>? extensionStateChanged = null;
                    if (hasStateNotifier && !extensionStateChanges.TryGetValue(id, out extensionStateChanged))
                    {
                        extensionStateChanged = ObserveExtensionState(id);
                        extensionStateChanges.Add(id, extensionStateChanged);
                    }
                    IEditorContext? editorContext = activeContext;
                    result.Add(new PaletteCommand(
                        Id: $"{entry.ExtensionType.FullName}.{entry.Definition.Name}",
                        DisplayName: displayName,
                        Description: entry.Definition.Description,
                        CategoryName: category,
                        KeyGesture: gesture,
                        CanExecute: () => CanExecuteExtension(id, commandName, editorContext),
                        ExecuteAsync: () => ExecuteExtensionAsync(id, commandName, editorContext, null))
                    {
                        StateChanged = extensionStateChanged,
                        ExecuteWithInteractionAsync = interaction =>
                            ExecuteExtensionAsync(id, commandName, editorContext, interaction)
                    });
                    continue;
                }

                // スナップショット時に解決したハンドラーを Execute / CanExecute / StateChanged 全てで共有し、
                // 列挙時と実行時で別インスタンスへ向くケースを防ぐ。タブ切替時は RebuildSnapshot で再列挙される。
                // ランタイムでハンドラーを解決できないコマンド（例: ContextCommandAttribute 方式のみで
                // 実装されたもの）はパレットから実行する手段がないため列挙対象から除外する。
                IContextCommandHandler? snapshotHandler = _handlerProvider.Resolve(entry.ExtensionType);
                if (snapshotHandler is null)
                {
                    continue;
                }

                IContextCommandHandler handler = snapshotHandler;
                IObservable<System.Reactive.Unit>? stateChanged =
                    (handler as IContextCommandStateNotifier)?.CanExecuteChanged;

                result.Add(new PaletteCommand(
                    Id: $"{entry.ExtensionType.FullName}.{entry.Definition.Name}",
                    DisplayName: displayName,
                    Description: entry.Definition.Description,
                    CategoryName: category,
                    KeyGesture: gesture,
                    CanExecute: () => handler.CanExecute(CreateExecution(commandName, activeContext, null)),
                    ExecuteAsync: () =>
                    {
                        // スロットル窓や状態変化で表示と実行可否がずれる可能性があるため、
                        // 実行直前にもう一度 CanExecute を確認してから Execute する。
                        var execution = CreateExecution(commandName, activeContext, null);
                        return handler.CanExecute(execution)
                            ? handler.ExecuteAsync(execution)
                            : Task.CompletedTask;
                    })
                {
                    StateChanged = stateChanged,
                    ExecuteWithInteractionAsync = interaction =>
                    {
                        var execution = CreateExecution(commandName, activeContext, interaction);
                        return handler.CanExecute(execution)
                            ? handler.ExecuteAsync(execution)
                            : Task.CompletedTask;
                    }
                });
            }
        }

        AppendMenuCommands(result);

        return result;
    }

    private IObservable<Unit> ObserveExtensionState(ExtensionId id)
    {
        return Observable.Create<Unit>(observer =>
        {
            if (!_extensionProvider.TryAcquire<ViewExtension>(id, out var lease))
                return Disposable.Empty;

            try
            {
                if (lease.Extension is IContextCommandStateNotifier notifier)
                    return new CompositeDisposable(notifier.CanExecuteChanged.Subscribe(observer), lease);
            }
            catch
            {
                lease.Dispose();
                throw;
            }

            lease.Dispose();
            return Disposable.Empty;
        });
    }

    private ContextCommandExecution CreateExecution(
        string commandName, IEditorContext? editorContext, IContextCommandInteraction? interaction)
    {
        return new ContextCommandExecution(commandName)
        {
            EditorContext = editorContext,
            Services = _contextServices,
            Interaction = interaction,
            CancellationToken = interaction?.CancellationToken ?? default
        };
    }

    private bool CanExecuteExtension(ExtensionId id, string commandName, IEditorContext? editorContext)
    {
        if (!_extensionProvider.TryAcquire<ViewExtension>(id, out var lease))
            return false;

        using (lease)
        {
            return lease.Extension is IContextCommandHandler handler
                && handler.CanExecute(CreateExecution(commandName, editorContext, null));
        }
    }

    private async Task ExecuteExtensionAsync(
        ExtensionId id, string commandName, IEditorContext? editorContext,
        IContextCommandInteraction? interaction)
    {
        if (!_extensionProvider.TryAcquire<ViewExtension>(id, out var lease))
            return;

        // Keep the extension alive until its entire asynchronous operation (including prompts) ends.
        using (lease)
        {
            if (lease.Extension is IContextCommandHandler handler)
            {
                var execution = CreateExecution(commandName, editorContext, interaction);
                if (handler.CanExecute(execution))
                    await handler.ExecuteAsync(execution);
            }
        }
    }

    private string ResolveCategoryName(Type extensionType)
    {
        string typeName = extensionType.AssemblyQualifiedName ?? extensionType.FullName ?? extensionType.Name;
        if (_categoryNameCache.TryGetValue(typeName, out string? cached))
        {
            return cached;
        }

        string name = extensionType.Name;
        Extension? matched = _extensionProvider.AllExtensions
            .FirstOrDefault(i => i.GetType() == extensionType);
        if (matched != null && !string.IsNullOrEmpty(matched.DisplayName))
        {
            name = matched.DisplayName;
        }

        _categoryNameCache[typeName] = name;
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
                ExecuteAsync: () => MenuBarViewModel.ExecuteCommandAsync(command)));
        }
    }
}
