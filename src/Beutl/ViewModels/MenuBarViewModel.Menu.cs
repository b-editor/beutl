using System.Collections.ObjectModel;
using System.Windows.Input;
using DynamicData;
using DynamicData.Binding;
using FluentIcons.Common;
using Reactive.Bindings;

namespace Beutl.ViewModels;

public partial class MenuBarViewModel
{
    public IReadOnlyList<MainMenuItem> MenuItems { get; }

    public ReactiveCommandSlim ShowSettings { get; } = new();

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
                        new(Strings.CreateNewProject) { Command = CreateNewProject, GestureCommand = "CreateNewProject" },
                        new(Strings.CreateNewScene) { Command = NewScene, GestureCommand = "CreateNewFile" },
                    ]
                },
                MainMenuItem.Separator,
                new(Strings.Open)
                {
                    Icon = Icon.Open,
                    Items =
                    [
                        new(Strings.Project) { Command = OpenProject, GestureCommand = "OpenProject" },
                        new(Strings.File) { Command = OpenFile, GestureCommand = "OpenFile" },
                    ]
                },
                MainMenuItem.Separator,
                new(Strings.Close) { Command = CloseFile },
                new(Strings.CloseProject) { Command = CloseProject },
                MainMenuItem.Separator,
                new(Strings.Save) { Command = Save, GestureCommand = "Save", Icon = Icon.Save },
                new(Strings.SaveAll) { Command = SaveAll, GestureCommand = "SaveAll", Icon = Icon.SaveMultiple },
                MainMenuItem.Separator,
                new(Strings.ExportProject) { Command = ExportProject, Icon = Icon.ArrowExport },
                new(Strings.ImportProject) { Command = ImportProject, Icon = Icon.ArrowImport },
                MainMenuItem.Separator,
                new(Strings.RecentFiles) { Items = CreateRecentItems(RecentFileItems, OpenRecentFile) },
                new(Strings.RecentProjects) { Items = CreateRecentItems(RecentProjectItems, OpenRecentProject) },
                MainMenuItem.Separator,
                new(Strings.Settings) { Command = ShowSettings, IsInWindowOnly = true },
                new(null) { IsInWindowOnly = true },
                new(Strings.Exit) { Command = Exit, GestureCommand = "Exit" },
            ]
        },
        new(Strings.Edit)
        {
            Items =
            [
                new(Strings.Undo) { Command = Undo, GestureCommand = "Undo", Icon = Icon.ArrowUndo },
                new(Strings.Redo) { Command = Redo, GestureCommand = "Redo", Icon = Icon.ArrowRedo },
            ]
        },
    ];

    private static ReadOnlyObservableCollection<MainMenuItem> CreateRecentItems(CoreList<string> paths, ICommand open)
    {
        paths.ToObservableChangeSet<CoreList<string>, string>()
            .Transform(path => new MainMenuItem(path) { Command = open, CommandParameter = path })
            .Bind(out ReadOnlyObservableCollection<MainMenuItem> items)
            .Subscribe();
        return items;
    }
}
