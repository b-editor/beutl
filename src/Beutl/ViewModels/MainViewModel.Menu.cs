using System.Collections.ObjectModel;
using System.Windows.Input;
using Beutl.Api.Services;
using Beutl.Editor.Components.DockLayoutTab;
using Beutl.Services;
using Beutl.Services.PrimitiveImpls;
using Beutl.ViewModels.Dock;
using Dock.Model.Core;
using DynamicData;
using DynamicData.Binding;
using FluentIcons.Common;
using Microsoft.Extensions.DependencyInjection;
using Reactive.Bindings;
using Reactive.Bindings.Extensions;

namespace Beutl.ViewModels;

public sealed partial class MainViewModel : IContextCommandStateNotifier
{
    // The followed lists subscribe to app-wide sources such as the dock layout presets, so disposal ends them.
    private readonly CompositeDisposable _menuSubscriptions = [];
    private IObservable<Unit>? _editorAvailabilityChanges;

    public IReadOnlyList<MainMenuItem> MenuItems { get; }

    internal EditViewModel? EnabledSceneEditor =>
        _editorService.SelectedTabItem.Value?.Context.Value is EditViewModel { IsEnabled.Value: true } editor
            ? editor
            : null;

    IObservable<Unit> IContextCommandStateNotifier.CanExecuteChanged => EditorAvailabilityChanges;

    private IObservable<Unit> EditorAvailabilityChanges =>
        _editorAvailabilityChanges ??= ObserveSelectedEditor()
            .Select(editor => editor?.IsEnabled.Select(_ => Unit.Default) ?? Observable.Return(Unit.Default))
            .Switch()
            .ObserveOnUIDispatcher();

    private IObservable<IEditorContext?> ObserveSelectedEditor() => _editorService.SelectedTabItem
        .Select(tab => tab?.Context.Select(context => (IEditorContext?)context) ?? Observable.Return<IEditorContext?>(null))
        .Switch();

    private MainMenuItem[] CreateMenuItems() =>
    [
        new(Strings.File)
        {
            Items =
            [
                new(Strings.CreateNew)
                {
                    Icon = Icon.New,
                    Items =
                    [
                        new(Strings.CreateNewProject)
                        {
                            Command = MenuBar.CreateNewProject, GestureCommand = "main.createNewProject"
                        },
                        new(Strings.CreateNewScene) { Command = MenuBar.NewScene, GestureCommand = "main.createNewFile" },
                    ]
                },
                MainMenuItem.Separator,
                new(Strings.Open)
                {
                    Icon = Icon.Open,
                    Items =
                    [
                        new(Strings.Project) { Command = MenuBar.OpenProject, GestureCommand = "main.openProject" },
                        new(Strings.File) { Command = MenuBar.OpenFile, GestureCommand = "main.openFile" },
                    ]
                },
                new(Strings.ImportMedia) { Command = MenuBar.ImportMedia, Icon = Icon.ArrowImport },
                new(Strings.HostedGit_CloneRepositoryMenu) { Command = MenuBar.CloneRepository },
                MainMenuItem.Separator,
                new(Strings.CloseTab) { Command = MenuBar.CloseTab, GestureCommand = MainViewExtension.CloseTabCommandId },
                new(Strings.Close) { Command = MenuBar.CloseFile },
                new(Strings.CloseProject) { Command = MenuBar.CloseProject, GestureCommand = "main.closeProject" },
                MainMenuItem.Separator,
                new(Strings.Save) { Command = MenuBar.Save, GestureCommand = "main.save", Icon = Icon.Save },
                new(Strings.SaveAll) { Command = MenuBar.SaveAll, GestureCommand = "main.saveAll", Icon = Icon.SaveMultiple },
                MainMenuItem.Separator,
                new(ProjectAssetsStrings.MenuLabel) { Command = MenuBar.OrganizeProjectAssets },
                new(Strings.ExportProject) { Command = MenuBar.ExportProject, Icon = Icon.ArrowExport },
                new(Strings.ImportProject) { Command = MenuBar.ImportProject, Icon = Icon.ArrowImport },
                MainMenuItem.Separator,
                new(Strings.RecentFiles)
                {
                    Items = CreateRecentItems(MenuBar.RecentFileItems, MenuBar.OpenRecentFile)
                },
                new(Strings.RecentProjects)
                {
                    Items = CreateRecentItems(MenuBar.RecentProjectItems, MenuBar.OpenRecentProject)
                },
                MainMenuItem.Separator,
                new(Strings.Settings)
                {
                    Command = MenuBar.ShowSettings, GestureCommand = MainViewExtension.ShowSettingsCommandId, IsInWindowOnly = true
                },
                new(null) { IsInWindowOnly = true },
                new(Strings.Exit) { Command = MenuBar.Exit, GestureCommand = "main.exit" },
            ]
        },
        new(Strings.Edit)
        {
            Items =
            [
                new(Strings.Undo)
                {
                    Command = MenuBar.Undo, GestureCommand = "main.undo", Icon = Icon.ArrowUndo, EditAction = StandardEditAction.Undo
                },
                new(Strings.Redo)
                {
                    Command = MenuBar.Redo, GestureCommand = "main.redo", Icon = Icon.ArrowRedo, EditAction = StandardEditAction.Redo
                },
                new(null) { IsInNativeMenuOnly = true },
                new(Strings.Cut) { Command = MenuBar.CutLayer, EditAction = StandardEditAction.Cut, IsInNativeMenuOnly = true },
                new(Strings.Copy) { Command = MenuBar.CopyLayer, EditAction = StandardEditAction.Copy, IsInNativeMenuOnly = true },
                new(Strings.Paste) { Command = MenuBar.PasteLayer, EditAction = StandardEditAction.Paste, IsInNativeMenuOnly = true },
                new(Strings.Delete)
                {
                    Command = MenuBar.DeleteLayer,
                    EditAction = StandardEditAction.Delete,
                    IsInNativeMenuOnly = true,
                    EditActionAvailability = (
                        () => _editorService.SelectedTabItem.Value?.Context.Value is not { IsEnabled.Value: false },
                        EditorAvailabilityChanges)
                },
                new(Strings.SelectAll) { Command = MenuBar.SelectAll, EditAction = StandardEditAction.SelectAll, IsInNativeMenuOnly = true },
            ]
        },
        new(Strings.View) { Items = CreateViewMenuItems() },
        new(Strings.Scene)
        {
            Items =
            [
                new(Strings.CreateNew) { Command = MenuBar.NewScene, GestureCommand = "main.createNewFile" },
                new(Strings.Remove) { Command = MenuBar.RemoveFromProject },
                new(Strings.SceneSettings) { Command = MenuBar.ShowSceneSettings },
                new(MissingMediaStrings.Title) { Command = MenuBar.RelinkMedia },
                new(UndisplayableDataStrings.Title) { Command = MenuBar.ShowUndisplayableData },
                MainMenuItem.Separator,
                .. CreateSceneMarkerAndPlaybackMenus(),
                new(null) { IsInWindowOnly = true },
                new(Strings.Element)
                {
                    IsInWindowOnly = true,
                    Items =
                    [
                        new(Strings.Delete) { Command = MenuBar.DeleteLayer },
                        new(Strings.Cut) { Command = MenuBar.CutLayer },
                        new(Strings.Copy) { Command = MenuBar.CopyLayer },
                        new(Strings.Paste) { Command = MenuBar.PasteLayer },
                    ]
                },
            ]
        },
        new(Strings.Window)
        {
            IsInNativeMenuOnly = true,
            Items =
            [
                new(Strings.MinimizeWindow) { Command = MenuBar.MinimizeWindow, GestureCommand = "main.minimizeWindow" },
                new(Strings.ZoomWindow) { Command = MenuBar.ZoomWindow },
            ]
        },
        new(Strings.Help)
        {
            Items =
            [
                new(Strings.Tutorials) { Command = MenuBar.ShowTutorials, GestureCommand = "main.showTutorials" },
                MainMenuItem.Separator,
                new("GC.Collect") { Command = MenuBar.CollectGarbage, IsVisible = UnderDebugger },
                new("Monitor KeyModifier") { Command = MenuBar.MonitorKeyModifiers, IsVisible = UnderDebugger },
                new("Throw Unhandled Exception") { Command = MenuBar.ThrowUnhandledException, IsVisible = UnderDebugger },
                new("Start Window Capture") { Command = MenuBar.StartWindowCapture, IsVisible = UnderDebugger },
                new("Stop Window Capture") { Command = MenuBar.StopWindowCapture, IsVisible = UnderDebugger },
                new(Strings.Info) { Command = MenuBar.ShowInformation },
            ]
        },
    ];

    private IObservable<bool> UnderDebugger => Observable.Return(IsDebuggerAttached);

    private ReadOnlyObservableCollection<MainMenuItem> CreateViewMenuItems()
    {
        MainMenuItem[] leading =
        [
            new(Strings.Editors)
            {
                Items = ToMenuItems(EditorExtensions.ToObservableChangeSet().ObserveOnUIDispatcher(), CreateEditorItem)
            },
            new(Strings.Tools)
            {
                Items = ToMenuItems(
                    ToolTabExtensions.ToObservableChangeSet().ObserveOnUIDispatcher()
                        .Filter(extension => extension.Header != null),
                    extension => new MainMenuItem(extension.Header)
                    {
                        Command = MenuBar.OpenToolTab, CommandParameter = extension
                    })
            },
        ];
        var toolWindowSeparator = new MainMenuItem(null);
        var layoutSeparator = new MainMenuItem(null);
        MainMenuItem[] trailing =
        [
            layoutSeparator,
            new(Strings.ResetDockLayout) { Command = MenuBar.ResetDockLayout },
            new(Strings.ApplyDockLayout)
            {
                // An empty submenu would be a dead end.
                IsVisible = MenuBar.DockLayoutPresets.ObserveProperty(presets => presets.Count)
                    .Select(count => count > 0)
                    .ObserveOnUIDispatcher(),
                // A rename replaces the entry.
                Items = ToMenuItems(
                    MenuBar.DockLayoutPresets
                        .ToObservableChangeSet<ICoreReadOnlyList<DockLayoutPresetItem>, DockLayoutPresetItem>()
                        .AutoRefreshOnObservable(preset => preset.Name.Skip(1))
                        .ObserveOnUIDispatcher(),
                    preset => new MainMenuItem(preset.Name.Value)
                    {
                        Command = MenuBar.ApplyDockLayout, CommandParameter = preset
                    },
                    transformOnRefresh: true)
            },
            new(Strings.ManageDockLayouts) { Command = MenuBar.OpenDockLayoutTab },
            MainMenuItem.Separator,
            new(Strings.ToggleFullScreen) { Command = MenuBar.ToggleFullScreen, GestureCommand = "main.toggleFullScreen" },
        ];

        var items = new ObservableCollection<MainMenuItem>([.. leading, .. trailing]);
        _menuSubscriptions.Add(ToolWindowExtensions.ToObservableChangeSet()
            .ObserveOnUIDispatcher()
            .Transform(extension => new MainMenuItem(extension.DisplayName)
            {
                Command = MenuBar.OpenToolWindow,
                CommandParameter = extension,
                IconSource = ExtensionGuard.Call(extension, AppServices.GetService<INotificationService>(), extension.GetIcon)
            })
            .ToCollection()
            .Subscribe(toolWindows =>
            {
                MainMenuItem[] section = toolWindows.Count == 0 ? [] : [toolWindowSeparator, .. toolWindows];
                // Update this section in place. The editors, tool tabs and layout controls keep their items.
                for (int i = items.IndexOf(layoutSeparator) - 1; i >= leading.Length; i--)
                {
                    if (!section.Contains(items[i]))
                        items.RemoveAt(i);
                }

                for (int i = 0; i < section.Length; i++)
                {
                    int target = leading.Length + i;
                    int current = items.IndexOf(section[i]);
                    if (current < 0)
                        items.Insert(target, section[i]);
                    else if (current != target)
                        items.Move(current, target);
                }
            }));
        return new ReadOnlyObservableCollection<MainMenuItem>(items);
    }

    private MainMenuItem[] CreateSceneMarkerAndPlaybackMenus()
    {
        var definitions = SceneEditorExtension.Instance.ContextCommands.ToDictionary(command => command.Id);
        IObservable<EditViewModel?> selectedEditor = ObserveSelectedEditor().Select(editor => editor as EditViewModel);
        return
        [
            new(Strings.Markers)
            {
                Items = [CreateItem("editor.toggleMarker"), CreateItem("editor.previousMarker"), CreateItem("editor.nextMarker")]
            },
            new(Strings.Playback)
            {
                Items =
                [
                    CreateItem("editor.playPause"),
                    CreateItem("editor.toggleLoop"),
                    MainMenuItem.Separator,
                    CreateItem("editor.seekStart"),
                    CreateItem("editor.previous"),
                    CreateItem("editor.next"),
                    CreateItem("editor.seekEnd"),
                    MainMenuItem.Separator,
                    CreateItem("editor.shuttleBackward"),
                    CreateItem("editor.shuttleBackwardFine"),
                    CreateItem("editor.shuttleStop"),
                    CreateItem("editor.shuttleForward"),
                    CreateItem("editor.shuttleForwardFine"),
                ]
            },
        ];

        MainMenuItem CreateItem(string id)
        {
            ContextCommandDefinition definition = definitions[id];
            IObservable<bool> canExecute = selectedEditor
                .Select(editor => editor is null
                    ? Observable.Return(false)
                    : editor.IsEnabled.CombineLatest(editor.CanExecuteChanged.StartWith(Unit.Default),
                        (enabled, _) => enabled && definition.CanRun(editor, new ContextCommandExecution(id) { EditorContext = editor })))
                .Switch()
                .DistinctUntilChanged()
                .ObserveOnUIDispatcher();
            var command = new AsyncReactiveCommand(canExecute).WithSubscribe(Execute);
            _menuSubscriptions.Add(command);
            return new MainMenuItem(definition.DisplayName ?? id)
            {
                Command = command,
                GestureCommand = id,
                InWindowGestureExtensionType = typeof(SceneEditorExtension),
                BlockNativeCommandWhileDialogOpen = true
            };

            Task Execute()
            {
                if (EnabledSceneEditor is not { } editor)
                    return Task.CompletedTask;

                var execution = new ContextCommandExecution(id) { EditorContext = editor };
                return definition.CanRun(editor, execution) ? definition.RunAsync(editor, execution) : Task.CompletedTask;
            }
        }
    }

    // Shown only while the selected tab's file is one the editor can open.
    private MainMenuItem CreateEditorItem(EditorExtension extension) => new(extension.DisplayName)
    {
        Command = MenuBar.SwitchEditor,
        CommandParameter = extension,
        IconSource = ExtensionGuard.Call(extension, AppServices.GetService<INotificationService>(), extension.GetIcon),
        IsVisible = _editorService.SelectedTabItem
            .Select(tab => tab?.FilePath.Select(path => extension.IsSupported(path)) ?? Observable.Return(false))
            .Switch()
    };

    private ReadOnlyObservableCollection<MainMenuItem> CreateRecentItems(CoreList<string> paths, ICommand open)
        => ToMenuItems(
            paths.ToObservableChangeSet<CoreList<string>, string>(),
            path => new MainMenuItem(path) { Command = open, CommandParameter = path });

    private ReadOnlyObservableCollection<MainMenuItem> ToMenuItems<T>(
        IObservable<IChangeSet<T>> source, Func<T, MainMenuItem> create, bool transformOnRefresh = false)
        where T : notnull
    {
        _menuSubscriptions.Add(source.Transform(create, transformOnRefresh)
            .Bind(out ReadOnlyObservableCollection<MainMenuItem> items)
            .Subscribe());
        return items;
    }

    // The counterpart of Ctrl+T. A scene editor closes its tool tab with the focus, unless that tab cannot close, as the
    // preview cannot. Another editor closes its file, and with no editor open the window is what is left to close.
    internal void CloseTab(Action closeWindow)
    {
        if (_editorService.SelectedTabItem.Value is not { } selected)
        {
            closeWindow();
        }
        else if (TabSwitcher.CanOpen)
        {
            if (selected.Context.Value is EditViewModel editor)
            {
                BeutlDockFactory factory = editor.DockHost.Factory;
                IDockable? focused = factory.CurrentDockable ?? editor.DockHost.Layout.Value.FocusedDockable;
                if (focused is BeutlToolDockable or NewToolTabDockable && focused.CanClose)
                    factory.CloseDockable(focused);
            }
            else
            {
                MenuBar.CloseFile.Execute();
            }
        }
    }

    private void OpenToolTab(ToolTabExtension extension)
    {
        if (_editorService.SelectedTabItem.Value?.Context.Value is IEditorContext editorContext
            && ExtensionGuard.Call(extension, AppServices.GetService<INotificationService>(),
                () => extension.TryCreateContext(editorContext, out IToolContext? created) ? created : null)
                is { } toolContext
            && !editorContext.OpenToolTab(toolContext))
        {
            toolContext.Dispose();
        }
    }

    private async void SwitchEditor(EditorExtension extension)
    {
        if (_editorService.SelectedTabItem.Value is { } selectedTab)
            await SwitchEditorAsync(selectedTab, extension);
    }

    internal async Task SwitchEditorAsync(EditorTabItem selectedTab, EditorExtension editorExtension)
    {
        using var write = await _editorService.BeginProjectFileWriteAsync(CancellationToken.None);
        if (!_editorService.TabItems.Contains(selectedTab)) return;
        IEditorContext original = selectedTab.Context.Value;
        using var suspension = _editorService.SuspendEditor(original);
        if (original is EditViewModel sceneEditor)
            await sceneEditor.FlushMediaFingerprintsAsync();
        if (original is ISavableEditorContext editor
            && !await Beutl.Services.EditorService.SaveEditorInReservedWorkspaceAsync(editor))
        {
            AppServices.GetRequiredService<INotificationService>().ShowError(MessageStrings.UnableToSaveFile, selectedTab.FileName.Value);
            return;
        }

        if (!ReferenceEquals(selectedTab.Context.Value, original)) return;

        if (ExtensionGuard.Call(editorExtension, AppServices.GetService<INotificationService>(), () => editorExtension.TryCreateContext(
                selectedTab.Context.Value.Object,
                new EditorContextServices(_editorService, _extensionProvider),
                out IEditorContext? created) ? created : null, out bool declined)
            is { } context)
        {
            selectedTab.Context.Value.Dispose();
            selectedTab.Context.Value = context;
        }
        else if (declined)
        {
            AppServices.GetRequiredService<INotificationService>().ShowInformation(
                title: MessageStrings.ContextNotCreated,
                message: string.Format(
                    format: MessageStrings.FailedToOpenFileWithExtension,
                    arg0: editorExtension.DisplayName,
                    arg1: selectedTab.FileName.Value));
        }
    }
}
