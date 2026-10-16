using System.Collections.ObjectModel;
using System.Windows.Input;
using Beutl.Services;
using DynamicData;
using DynamicData.Binding;
using FluentIcons.Common;
using Reactive.Bindings.Extensions;

namespace Beutl.ViewModels;

public sealed partial class MainViewModel
{
    public IReadOnlyList<MainMenuItem> MenuItems { get; }

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
                            Command = MenuBar.CreateNewProject, GestureCommand = "CreateNewProject"
                        },
                        new(Strings.CreateNewScene) { Command = MenuBar.NewScene, GestureCommand = "CreateNewFile" },
                    ]
                },
                MainMenuItem.Separator,
                new(Strings.Open)
                {
                    Icon = Icon.Open,
                    Items =
                    [
                        new(Strings.Project) { Command = MenuBar.OpenProject, GestureCommand = "OpenProject" },
                        new(Strings.File) { Command = MenuBar.OpenFile, GestureCommand = "OpenFile" },
                    ]
                },
                MainMenuItem.Separator,
                new(Strings.Close) { Command = MenuBar.CloseFile },
                new(Strings.CloseProject) { Command = MenuBar.CloseProject },
                MainMenuItem.Separator,
                new(Strings.Save) { Command = MenuBar.Save, GestureCommand = "Save", Icon = Icon.Save },
                new(Strings.SaveAll) { Command = MenuBar.SaveAll, GestureCommand = "SaveAll", Icon = Icon.SaveMultiple },
                MainMenuItem.Separator,
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
                new(Strings.Settings) { Command = MenuBar.ShowSettings, IsInWindowOnly = true },
                new(null) { IsInWindowOnly = true },
                new(Strings.Exit) { Command = MenuBar.Exit, GestureCommand = "Exit" },
            ]
        },
        new(Strings.Edit)
        {
            Items =
            [
                new(Strings.Undo) { Command = MenuBar.Undo, GestureCommand = "Undo", Icon = Icon.ArrowUndo },
                new(Strings.Redo) { Command = MenuBar.Redo, GestureCommand = "Redo", Icon = Icon.ArrowRedo },
            ]
        },
        new(Strings.View)
        {
            Items =
            [
                new(Strings.Editors)
                {
                    Items = ToMenuItems(
                        EditorExtensions.ToObservableChangeSet().ObserveOnUIDispatcher(), CreateEditorItem)
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
                MainMenuItem.Separator,
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
            ]
        },
        new(Strings.Tools)
        {
            Items = ToMenuItems(
                ToolWindowExtensions.ToObservableChangeSet().ObserveOnUIDispatcher(),
                extension => new MainMenuItem(extension.DisplayName)
                {
                    Command = MenuBar.OpenToolWindow, CommandParameter = extension, IconSource = extension.GetIcon()
                })
        },
    ];

    // Shown only while the selected tab's file is one the editor can open.
    private MainMenuItem CreateEditorItem(EditorExtension extension) => new(extension.DisplayName)
    {
        Command = MenuBar.SwitchEditor,
        CommandParameter = extension,
        IconSource = extension.GetIcon(),
        IsVisible = _editorService.SelectedTabItem
            .Select(tab => tab?.FilePath.Select(path => extension.IsSupported(path)) ?? Observable.Return(false))
            .Switch()
    };

    private static ReadOnlyObservableCollection<MainMenuItem> CreateRecentItems(CoreList<string> paths, ICommand open)
        => ToMenuItems(
            paths.ToObservableChangeSet<CoreList<string>, string>(),
            path => new MainMenuItem(path) { Command = open, CommandParameter = path });

    private static ReadOnlyObservableCollection<MainMenuItem> ToMenuItems<T>(
        IObservable<IChangeSet<T>> source, Func<T, MainMenuItem> create, bool transformOnRefresh = false)
        where T : notnull
    {
        source.Transform(create, transformOnRefresh)
            .Bind(out ReadOnlyObservableCollection<MainMenuItem> items)
            .Subscribe();
        return items;
    }

    private void OpenToolTab(ToolTabExtension extension)
    {
        if (_editorService.SelectedTabItem.Value?.Context.Value is IEditorContext editorContext
            && extension.TryCreateContext(editorContext, out IToolContext? toolContext)
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
            && !await editor.SaveAsync())
        {
            NotificationService.ShowError(MessageStrings.UnableToSaveFile, selectedTab.FileName.Value);
            return;
        }

        if (!ReferenceEquals(selectedTab.Context.Value, original)) return;

        if (editorExtension.TryCreateContext(
                selectedTab.Context.Value.Object,
                new EditorContextServices(_editorService, _extensionProvider),
                out IEditorContext? context))
        {
            selectedTab.Context.Value.Dispose();
            selectedTab.Context.Value = context;
        }
        else
        {
            NotificationService.ShowInformation(
                title: MessageStrings.ContextNotCreated,
                message: string.Format(
                    format: MessageStrings.FailedToOpenFileWithExtension,
                    arg0: editorExtension.DisplayName,
                    arg1: selectedTab.FileName.Value));
        }
    }
}
