using System.Collections.ObjectModel;
using System.Windows.Input;
using Beutl.Api.Services;
using Beutl.Editor.Components.DockLayoutTab;
using Beutl.Services;
using DynamicData;
using DynamicData.Binding;
using FluentIcons.Common;
using Reactive.Bindings.Extensions;

namespace Beutl.ViewModels;

public sealed partial class MainViewModel
{
    // The followed lists subscribe to app-wide sources such as the dock layout presets, so disposal ends them.
    private readonly CompositeDisposable _menuSubscriptions = [];

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
                MainMenuItem.Separator,
                new(Strings.Close) { Command = MenuBar.CloseFile },
                new(Strings.CloseProject) { Command = MenuBar.CloseProject },
                MainMenuItem.Separator,
                new(Strings.Save) { Command = MenuBar.Save, GestureCommand = "main.save", Icon = Icon.Save },
                new(Strings.SaveAll) { Command = MenuBar.SaveAll, GestureCommand = "main.saveAll", Icon = Icon.SaveMultiple },
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
                new(Strings.Exit) { Command = MenuBar.Exit, GestureCommand = "main.exit" },
            ]
        },
        new(Strings.Edit)
        {
            Items =
            [
                new(Strings.Undo) { Command = MenuBar.Undo, GestureCommand = "main.undo", Icon = Icon.ArrowUndo },
                new(Strings.Redo) { Command = MenuBar.Redo, GestureCommand = "main.redo", Icon = Icon.ArrowRedo },
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
            Items = CreateToolsMenuItems()
        },
        new(Strings.Scene)
        {
            Items =
            [
                new(Strings.CreateNew) { Command = MenuBar.NewScene },
                new(Strings.Remove) { Command = MenuBar.RemoveFromProject },
                new(Strings.SceneSettings) { Command = MenuBar.ShowSceneSettings },
                new(MissingMediaStrings.Title) { Command = MenuBar.RelinkMedia },
                new(Strings.Element)
                {
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
        new(Strings.Ai)
        {
            Items =
            [
                new(Strings.AiJobCenter) { Command = MenuBar.ShowAiJobs, Icon = Icon.History },
                MainMenuItem.Separator,
                new(Strings.AiGenerateImage) { Command = MenuBar.GenerateImage, Icon = Icon.SparkleCircle },
                new(Strings.AiGenerateSubtitles) { Command = MenuBar.GenerateSubtitles, Icon = Icon.Subtitles },
                new(Strings.AiEditImage) { Command = MenuBar.EditImage, Icon = Icon.ImageEdit },
                new(Strings.AiGenerateVideo) { Command = MenuBar.GenerateVideo, Icon = Icon.Video },
                new(Strings.AiVideoEditing) { Command = MenuBar.EditVideo, Icon = Icon.VideoClipWand },
            ]
        },
        new(Strings.Help)
        {
            Items =
            [
                new(Strings.Tutorials) { Command = MenuBar.ShowTutorials },
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

    private ObservableCollection<MainMenuItem> CreateToolsMenuItems()
    {
        var silenceItem = new MainMenuItem(Strings.AutoSplitBySilence) { Command = MenuBar.AutoSplitBySilence, Icon = Icon.Cut };
        var items = new ObservableCollection<MainMenuItem> { silenceItem };
        _menuSubscriptions.Add(ToolWindowExtensions.ToObservableChangeSet().ToCollection().ObserveOnUIDispatcher()
            .Subscribe(extensions =>
            {
                items.Clear();
                items.Add(silenceItem);
                if (extensions.Count > 0) items.Add(MainMenuItem.Separator);
                foreach (var extension in extensions)
                    items.Add(new(extension.DisplayName)
                    {
                        Command = MenuBar.OpenToolWindow,
                        CommandParameter = extension,
                        IconSource = ExtensionGuard.Call(extension, extension.GetIcon)
                    });
            }));
        return items;
    }

    // Shown only while the selected tab's file is one the editor can open.
    private MainMenuItem CreateEditorItem(EditorExtension extension) => new(extension.DisplayName)
    {
        Command = MenuBar.SwitchEditor,
        CommandParameter = extension,
        IconSource = ExtensionGuard.Call(extension, extension.GetIcon),
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

    private void OpenToolTab(ToolTabExtension extension)
    {
        if (_editorService.SelectedTabItem.Value?.Context.Value is IEditorContext editorContext
            && ExtensionGuard.Call(extension, () => extension.TryCreateContext(editorContext, out IToolContext? created) ? created : null)
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
            && !await editor.SaveAsync())
        {
            NotificationService.ShowError(MessageStrings.UnableToSaveFile, selectedTab.FileName.Value);
            return;
        }

        if (!ReferenceEquals(selectedTab.Context.Value, original)) return;

        if (ExtensionGuard.Call(editorExtension, () => editorExtension.TryCreateContext(
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
            NotificationService.ShowInformation(
                title: MessageStrings.ContextNotCreated,
                message: string.Format(
                    format: MessageStrings.FailedToOpenFileWithExtension,
                    arg0: editorExtension.DisplayName,
                    arg1: selectedTab.FileName.Value));
        }
    }
}
