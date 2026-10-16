using System.Runtime.InteropServices;
using System.Windows.Input;
using Beutl.ViewModels;

namespace Beutl.Services.PrimitiveImpls;

// The window's commands, which run on MainViewModel.
public class MainViewExtension : ViewExtension
{
    public const string ShowCommandPaletteCommandName = "ShowCommandPalette";
    public const string NextTabCommandName = "NextTab";
    public const string PreviousTabCommandName = "PreviousTab";
    public const string NextToolTabCommandName = "NextToolTab";
    public const string PreviousToolTabCommandName = "PreviousToolTab";
    public const string CreateToolTabCommandName = "CreateToolTab";

    public static readonly MainViewExtension Instance = new();

    public override string Name => "MainView";

    public override string DisplayName => Strings.MainView;

    public override IEnumerable<ContextCommandDefinition> ContextCommands =>
    [
        Navigation(NextTabCommandName, "main.nextTab", Strings.TabSwitcher_Next, [new("Ctrl+Tab")]),
        Navigation(PreviousTabCommandName, "main.previousTab", Strings.TabSwitcher_Previous, [new("Ctrl+Shift+Tab")]),
        Navigation(NextToolTabCommandName, "main.nextToolTab", Strings.TabSwitcher_NextTool,
            [new("Alt+F7"), new("Alt+Tab")]),
        Navigation(PreviousToolTabCommandName, "main.previousToolTab", Strings.TabSwitcher_PreviousTool,
            [new("Alt+Shift+F7"), new("Alt+Shift+Tab")]),
        Navigation(CreateToolTabCommandName, "main.createToolTab", Strings.TabSwitcher_Create,
            [new("Ctrl+T"), new("Cmd+T", OSPlatform.OSX)]),
        Menu("CreateNewProject", "main.createNewProject", Strings.CreateNewProject, "",
            [new("Ctrl+Shift+N"), new("Cmd+Shift+N", OSPlatform.OSX)], menuBar => menuBar.CreateNewProject),
        Menu("CreateNewFile", "main.createNewFile", Strings.CreateNewScene, "",
            [new("Ctrl+N"), new("Cmd+N", OSPlatform.OSX)], menuBar => menuBar.NewScene),
        Menu("OpenProject", "main.openProject", Strings.OpenProject, "",
            [new("Ctrl+Shift+O"), new("Cmd+Shift+O", OSPlatform.OSX)], menuBar => menuBar.OpenProject),
        Menu("OpenFile", "main.openFile", Strings.OpenFile, "",
            [new("Ctrl+O"), new("Cmd+O", OSPlatform.OSX)], menuBar => menuBar.OpenFile),
        Menu("Save", "main.save", Strings.Save, Strings.Save_Description,
            [new("Ctrl+S"), new("Cmd+S", OSPlatform.OSX)], menuBar => menuBar.Save),
        Menu("SaveAll", "main.saveAll", Strings.SaveAll, Strings.SaveAll_Description,
            [new("Ctrl+Shift+S"), new("Cmd+Shift+S", OSPlatform.OSX)], menuBar => menuBar.SaveAll),
        Palette("OpenRecentProject", "main.openRecentProject", Strings.OpenRecentProject,
            main => main.MenuBar.RecentProjectItems.Count > 0, (main, interaction) => main.OpenRecentProjectAsync(interaction)),
        Palette("OpenRecentFile", "main.openRecentFile", Strings.OpenRecentFile,
            main => main.MenuBar.RecentFileItems.Count > 0, (main, interaction) => main.OpenRecentFileAsync(interaction)),
        Palette("ChangeTheme", "main.changeTheme", Strings.ChangeTheme,
            _ => ThemeRegistry.Enumerate().Count > 1, (_, interaction) => MainViewModel.ChangeThemeAsync(interaction)),
        Menu("EnableVersionControl", "main.enableVersionControl", Strings.VersionControl_Enable, "", [],
            menuBar => menuBar.EnableVersionControl),
        new ContextCommandDefinition<MainViewModel>("CommitVersion", Strings.VersionControl_Commit, "", [])
        {
            Id = "main.commitVersion",
            CanExecuteOn = (main, _) => ((ICommand)main.MenuBar.CommitVersion).CanExecute(null),
            // The palette asks for the message; otherwise the menu's flyout does.
            ExecuteOnAsync = (main, execution) => execution.Interaction is { } interaction
                ? main.CommitVersionAsync(interaction)
                : MenuBarViewModel.ExecuteCommandAsync(main.MenuBar.CommitVersion)
        },
        Menu("CloseProject", "main.closeProject", Strings.CloseProject, Strings.CloseProject_Description,
            [new("Ctrl+Shift+F4"), new("Cmd+Shift+W", OSPlatform.OSX)], menuBar => menuBar.CloseProject),
        Menu("Undo", "main.undo", Strings.Undo, Strings.Undo_Description,
            [new("Ctrl+Z"), new("Cmd+Z", OSPlatform.OSX)], menuBar => menuBar.Undo),
        Menu("Redo", "main.redo", Strings.Redo, Strings.Redo_Description,
            [new("Ctrl+Y"), new("Cmd+Shift+Z", OSPlatform.OSX)], menuBar => menuBar.Redo),
        Menu("Exit", "main.exit", Strings.Exit, Strings.Exit_Description,
            [new("Alt+F4"), new("Cmd+Q", OSPlatform.OSX)], menuBar => menuBar.Exit),
        new ContextCommandDefinition<MainViewModel>(ShowCommandPaletteCommandName, Strings.ShowCommandPalette,
            Strings.ShowCommandPalette_Description, [new("Ctrl+Shift+P"), new("Cmd+Shift+P", OSPlatform.OSX)])
        {
            Id = "main.showCommandPalette",
            ExecuteOnAsync = (main, _) =>
            {
                main.TabSwitcher.Close();
                main.CommandPalette.Toggle();
                return Task.CompletedTask;
            }
        },
    ];

    // A tab switcher gesture is not taken from a text box or while the palette is open.
    private static ContextCommandDefinition<MainViewModel> Navigation(
        string name, string id, string displayName, ContextCommandKeyGesture[] keyGestures)
        => new(name, displayName, "", keyGestures)
        {
            Id = id,
            CanExecuteOn = (main, execution) => main.TabSwitcher.CanOpen
                && !TabSwitcherViewModel.IsTextInputGesture(execution.KeyEventArgs)
                && (execution.KeyEventArgs is null || !main.CommandPalette.IsOpen.Value),
            ExecuteOnAsync = (main, execution) =>
            {
                bool opened = main.TabSwitcher.ExecuteCommand(execution);
                if (execution.KeyEventArgs is { } args)
                    args.Handled = opened;
                return Task.CompletedTask;
            }
        };

    private static ContextCommandDefinition<MainViewModel> Menu(
        string name, string id, string displayName, string description, ContextCommandKeyGesture[] keyGestures,
        Func<MenuBarViewModel, ICommand> command)
        => new(name, displayName, description, keyGestures)
        {
            Id = id,
            CanExecuteOn = (main, _) => command(main.MenuBar).CanExecute(null),
            ExecuteOnAsync = (main, _) => MenuBarViewModel.ExecuteCommandAsync(command(main.MenuBar))
        };

    // Palette-only: it asks for its argument, so it has no default gesture.
    private static ContextCommandDefinition<MainViewModel> Palette(
        string name, string id, string displayName, Func<MainViewModel, bool> canExecute,
        Func<MainViewModel, IContextCommandInteraction, Task> execute)
        => new(name, displayName, "", [])
        {
            Id = id,
            CanExecuteOn = (main, _) => canExecute(main),
            ExecuteOnAsync = (main, execution) =>
                execution.Interaction is { } interaction ? execute(main, interaction) : Task.CompletedTask
        };
}
