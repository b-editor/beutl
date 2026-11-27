using System.Globalization;
using Avalonia.Input;
using Avalonia.Threading;
using Beutl.Api.Services;
using Beutl.Services.PrimitiveImpls;
using Beutl.Services.StartupTasks;
using Beutl.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace Beutl.Services;

public sealed class CommandPaletteService
{
    private static readonly Lazy<Dictionary<(Type ExtensionType, string CommandId), PaletteSearchText>> s_englishCommands =
        new(() => ReadEnglish(BuildEnglishCommandLookup));

    private readonly ContextCommandManager _commandManager;
    private readonly ICommandPaletteHandlerProvider _handlerProvider;
    private readonly Func<MenuBarViewModel?> _menuBarAccessor;
    private readonly EditorService _editorService;
    private readonly ExtensionProvider _extensionProvider;
    private readonly IEditorContextServices _contextServices;
    private readonly Dictionary<string, string> _categoryNameCache = new(StringComparer.Ordinal);

    public CommandPaletteService(
        ContextCommandManager commandManager,
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

    private INotificationService? Notifications => _editorService.AppServices.GetService<INotificationService>();

    public IReadOnlyList<PaletteCommand> EnumerateCommands()
    {
        var result = new List<PaletteCommand>();

        EditorTabItem? activeTab = _editorService.SelectedTabItem.Value;
        IEditorContext? activeContext = activeTab?.Context.Value;
        Type? activeEditorExtensionType = activeTab?.Extension.Value?.GetType();
        EditViewModel? activeEditor = activeContext as EditViewModel;
        var extensionStateChanges = new Dictionary<ExtensionId, IObservable<Unit>>();
        IReadOnlyList<ExtensionDescriptor> extensionDescriptors = _extensionProvider.GetDescriptors<ViewExtension>();

        foreach (ContextCommandEntry entry in _commandManager.GetDefinitions())
        {
            string commandId = entry.Definition.Id;
            if (commandId == MainViewExtension.ShowCommandPaletteCommandId)
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

            string displayName = string.IsNullOrEmpty(entry.Definition.DisplayName)
                ? entry.Definition.Id
                : entry.Definition.DisplayName!;
            string category = ResolveCategoryName(entry.ExtensionType);
            PaletteSearchText englishSearchText = s_englishCommands.Value.GetValueOrDefault((entry.ExtensionType, commandId));
            KeyGesture? gesture = ContextCommandManager.CurrentGestures(entry).FirstOrDefault();

            if (entry.Definition.Scope == ContextCommandScope.Extension)
            {
                ExtensionDescriptor? descriptor = FindDescriptor(extensionDescriptors, entry.ExtensionType);
                if (descriptor is null
                    || !_extensionProvider.TryAcquire<ViewExtension>(descriptor.Id, out var lease))
                {
                    continue;
                }

                bool hasStateNotifier;
                using (lease)
                {
                    if (!entry.Definition.HasHandler)
                        continue;
                    hasStateNotifier = lease.Extension is IContextCommandStateNotifier
                        || (activeContext is not null && lease.Extension is IContextCommandEditorStateNotifier);
                }

                ExtensionId id = descriptor.Id;
                IObservable<Unit>? extensionStateChanged = null;
                if (hasStateNotifier && !extensionStateChanges.TryGetValue(id, out extensionStateChanged))
                {
                    extensionStateChanged = ObserveExtensionState(id, activeContext);
                    extensionStateChanges.Add(id, extensionStateChanged);
                }
                IEditorContext? editorContext = activeContext;
                ContextCommandDefinition extensionDefinition = entry.Definition;
                result.Add(new PaletteCommand(
                    Id: entry.Definition.Id,
                    DisplayName: displayName,
                    Description: entry.Definition.Description,
                    CategoryName: category,
                    KeyGesture: gesture,
                    CanExecute: () => CanExecuteExtension(id, extensionDefinition, editorContext),
                    ExecuteAsync: interaction => ExecuteExtensionAsync(id, extensionDefinition, editorContext, interaction))
                {
                    EnglishSearchText = englishSearchText,
                    StateChanged = extensionStateChanged
                });
                continue;
            }

            ContextCommandDefinition definition = entry.Definition;
            if (!definition.HasHandler)
            {
                continue;
            }

            bool isToolCommand = typeof(ToolTabExtension).IsAssignableFrom(entry.ExtensionType);
            if (isToolCommand && activeEditor is null)
            {
                continue;
            }

            // An open tab's handler is shared by execution, enablement and state notifications.
            object? context = _handlerProvider.Resolve(entry.ExtensionType);
            if (context is null)
            {
                if (isToolCommand && definition.CanOpenToolTab && activeEditor is not null
                    && FindDescriptor(extensionDescriptors, entry.ExtensionType) is { } descriptor)
                {
                    ExtensionId id = descriptor.Id;
                    result.Add(new PaletteCommand(
                        Id: commandId,
                        DisplayName: displayName,
                        Description: definition.Description,
                        CategoryName: string.Format(Strings.CommandPalette_OpensToolTab, category),
                        KeyGesture: gesture,
                        CanExecute: () => IsActiveEditor(activeEditor),
                        ExecuteAsync: interaction => ExecuteClosedToolAsync(id, definition, activeEditor, interaction))
                    {
                        EnglishSearchText = englishSearchText
                    });
                }

                continue;
            }

            Extension? owner = FindExtension(entry.ExtensionType);
            IObservable<Unit>? stateChanged = (context as IContextCommandStateNotifier)?.CanExecuteChanged;

            result.Add(new PaletteCommand(
                Id: entry.Definition.Id,
                DisplayName: displayName,
                Description: entry.Definition.Description,
                CategoryName: category,
                KeyGesture: gesture,
                CanExecute: () => Guard(owner, () => definition.CanRun(context, CreateExecution(commandId, activeContext, null))),
                ExecuteAsync: interaction => Guard(owner, () =>
                    RunIfEnabledAsync(definition, context, CreateExecution(commandId, activeContext, interaction))))
            {
                EnglishSearchText = englishSearchText,
                StateChanged = stateChanged
            });
        }

        AppendMenuCommands(result);

        return result;
    }

    private static Dictionary<(Type ExtensionType, string CommandId), PaletteSearchText> BuildEnglishCommandLookup()
    {
        // Built-in catalogs read their labels when enumerated, so cache their English text once.
        var texts = new Dictionary<(Type, string), PaletteSearchText>();
        foreach (Extension extension in LoadPrimitiveExtensionTask.PrimitiveExtensions)
        {
            IEnumerable<ContextCommandDefinition> definitions = extension switch
            {
                ViewExtension view => view.ContextCommands,
                ToolWindowExtension toolWindow => toolWindow.ContextCommands,
                _ => []
            };

            foreach (ContextCommandDefinition definition in definitions)
            {
                texts[(extension.GetType(), definition.Id)] = new PaletteSearchText(
                    definition.DisplayName, definition.Description, extension.DisplayName);
            }
        }

        return texts;
    }

    private static T ReadEnglish<T>(Func<T> read)
    {
        CultureInfo previousCulture = CultureInfo.CurrentUICulture;
        try
        {
            // Beutl's neutral resources are in English.
            CultureInfo.CurrentUICulture = CultureInfo.InvariantCulture;
            return read();
        }
        finally
        {
            CultureInfo.CurrentUICulture = previousCulture;
        }
    }

    private static ExtensionDescriptor? FindDescriptor(IReadOnlyList<ExtensionDescriptor> descriptors, Type extensionType)
    {
        string typeName = extensionType.AssemblyQualifiedName ?? extensionType.FullName ?? extensionType.Name;
        return descriptors.FirstOrDefault(descriptor => descriptor.TypeName == typeName);
    }

    private bool IsActiveEditor(EditViewModel editor)
        => !editor.IsDisposingOrDisposed && ReferenceEquals(_editorService.SelectedTabItem.Value?.Context.Value, editor);

    private async Task ExecuteClosedToolAsync(
        ExtensionId id, ContextCommandDefinition definition, EditViewModel editor, IContextCommandInteraction? interaction)
    {
        if (!IsActiveEditor(editor) || !_extensionProvider.TryAcquire<ToolTabExtension>(id, out var lease))
            return;

        using (lease)
        {
            ToolTabExtension extension = lease.Extension;
            await ExtensionGuard.CallAsync(extension, Notifications, async () =>
            {
                Type extensionType = extension.GetType();
                IToolContext? context = editor.DockHost.FindToolContext(extensionType);
                if (context is null)
                {
                    if (!editor.DockHost.OpenToolTabFromExtension(extension, target: null))
                        return;

                    // Let the new view attach before commands that ask it to scroll or fit.
                    await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Loaded);
                    if (!IsActiveEditor(editor) || interaction?.CancellationToken.IsCancellationRequested == true)
                        return;

                    context = editor.DockHost.FindToolContext(extensionType);
                }

                if (context is not null)
                    await RunIfEnabledAsync(definition, context, CreateExecution(definition.Id, editor, interaction));
            });
        }
    }

    // The palette's snapshot can lag behind state changes, so check again before running the handler.
    private static Task RunIfEnabledAsync(ContextCommandDefinition definition, object? context, ContextCommandExecution execution)
        => definition.CanRun(context, execution) ? definition.RunAsync(context, execution) : Task.CompletedTask;

    private IObservable<Unit> ObserveExtensionState(ExtensionId id, IEditorContext? editorContext)
    {
        return Observable.Create<Unit>(observer =>
        {
            if (!_extensionProvider.TryAcquire<ViewExtension>(id, out var lease))
                return Disposable.Empty;

            try
            {
                IObservable<Unit>? stateChanged = (lease.Extension as IContextCommandStateNotifier)?.CanExecuteChanged;
                // 拡張機能は全エディタで共有されるため、コマンドを評価するエディタを渡してその状態変化を受け取る。
                if (editorContext is not null && lease.Extension is IContextCommandEditorStateNotifier editorNotifier)
                {
                    IObservable<Unit> editorStateChanged = editorNotifier.GetCanExecuteChanged(editorContext);
                    stateChanged = stateChanged?.Merge(editorStateChanged) ?? editorStateChanged;
                }

                if (stateChanged is not null)
                    return new CompositeDisposable(stateChanged.Subscribe(observer), lease);
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
        string commandId, IEditorContext? editorContext, IContextCommandInteraction? interaction)
    {
        return new ContextCommandExecution(commandId)
        {
            EditorContext = editorContext,
            Services = _contextServices,
            Interaction = interaction,
            CancellationToken = interaction?.CancellationToken ?? default
        };
    }

    private bool CanExecuteExtension(ExtensionId id, ContextCommandDefinition definition, IEditorContext? editorContext)
    {
        if (!_extensionProvider.TryAcquire<ViewExtension>(id, out var lease))
            return false;

        using (lease)
        {
            return ExtensionGuard.Call(lease.Extension, Notifications, () => definition.CanRun(null, CreateExecution(definition.Id, editorContext, null)));
        }
    }

    private async Task ExecuteExtensionAsync(
        ExtensionId id, ContextCommandDefinition definition, IEditorContext? editorContext,
        IContextCommandInteraction? interaction)
    {
        if (!_extensionProvider.TryAcquire<ViewExtension>(id, out var lease))
            return;

        // Keep the extension alive until its entire asynchronous operation (including prompts) ends.
        using (lease)
        {
            var execution = CreateExecution(definition.Id, editorContext, interaction);
            await ExtensionGuard.CallAsync(lease.Extension, Notifications, () => RunIfEnabledAsync(definition, null, execution));
        }
    }

    // A lambda here would hoist the entry into the snapshot's closure and keep a collectible extension type alive.
    private Extension? FindExtension(Type type)
        => _extensionProvider.AllExtensions.FirstOrDefault(extension => extension.GetType() == type);

    // A handler that no registered extension owns is the host's own, and runs unguarded.
    private bool Guard(Extension? owner, Func<bool> canExecute)
        => owner is null ? canExecute() : ExtensionGuard.Call(owner, Notifications, canExecute);

    private Task Guard(Extension? owner, Func<Task> execute)
        => owner is null ? execute() : ExtensionGuard.CallAsync(owner, Notifications, execute);

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
        Dictionary<string, PaletteSearchText> englishCommands = ReadEnglish(() =>
        {
            string englishCategory = Strings.CommandPalette_MenuCategory;
            return menuBar.EnumeratePaletteCommands().ToDictionary(
                command => command.Id,
                command => new PaletteSearchText(command.DisplayName, null, englishCategory));
        });

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
                ExecuteAsync: _ => MenuBarViewModel.ExecuteCommandAsync(command))
            {
                EnglishSearchText = englishCommands.GetValueOrDefault(menuCommand.Id)
            });
        }
    }
}
