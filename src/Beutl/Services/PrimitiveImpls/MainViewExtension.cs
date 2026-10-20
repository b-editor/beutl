using System.Runtime.InteropServices;
using System.Windows.Input;
using Beutl.ViewModels;

namespace Beutl.Services.PrimitiveImpls;

// The window's commands, which run on MainViewModel.
public class MainViewExtension : ViewExtension
{
    public const string ShowCommandPaletteCommandId = "main.showCommandPalette";
    public const string NextTabCommandId = "main.nextTab";
    public const string PreviousTabCommandId = "main.previousTab";
    public const string NextToolTabCommandId = "main.nextToolTab";
    public const string PreviousToolTabCommandId = "main.previousToolTab";
    public const string CreateToolTabCommandId = "main.createToolTab";

    public static readonly MainViewExtension Instance = new();

    public override string Name => "MainView";

    public override string DisplayName => Strings.MainView;

    public override IEnumerable<ContextCommandDefinition> ContextCommands =>
    [
        Navigation(NextTabCommandId, Strings.TabSwitcher_Next, [new("Ctrl+Tab")]),
        Navigation(PreviousTabCommandId, Strings.TabSwitcher_Previous, [new("Ctrl+Shift+Tab")]),
        Navigation(NextToolTabCommandId, Strings.TabSwitcher_NextTool,
            [new("Alt+F7"), new("Alt+Tab")]),
        Navigation(PreviousToolTabCommandId, Strings.TabSwitcher_PreviousTool,
            [new("Alt+Shift+F7"), new("Alt+Shift+Tab")]),
        Navigation(CreateToolTabCommandId, Strings.TabSwitcher_Create,
            [new("Ctrl+T"), new("Cmd+T", OSPlatform.OSX)]),
        Menu("main.createNewProject", Strings.CreateNewProject, "",
            [new("Ctrl+Shift+N"), new("Cmd+Shift+N", OSPlatform.OSX)], menuBar => menuBar.CreateNewProject),
        Menu("main.createNewFile", Strings.CreateNewScene, "",
            [new("Ctrl+N"), new("Cmd+N", OSPlatform.OSX)], menuBar => menuBar.NewScene),
        Menu("main.openProject", Strings.OpenProject, "",
            [new("Ctrl+Shift+O"), new("Cmd+Shift+O", OSPlatform.OSX)], menuBar => menuBar.OpenProject),
        Menu("main.openFile", Strings.OpenFile, "",
            [new("Ctrl+O"), new("Cmd+O", OSPlatform.OSX)], menuBar => menuBar.OpenFile),
        Menu("main.save", Strings.Save, Strings.Save_Description,
            [new("Ctrl+S"), new("Cmd+S", OSPlatform.OSX)], menuBar => menuBar.Save),
        Menu("main.saveAll", Strings.SaveAll, Strings.SaveAll_Description,
            [new("Ctrl+Shift+S"), new("Cmd+Shift+S", OSPlatform.OSX)], menuBar => menuBar.SaveAll),
        Palette("main.openRecentProject", Strings.OpenRecentProject,
            main => main.MenuBar.RecentProjectItems.Count > 0, (main, interaction) => main.OpenRecentProjectAsync(interaction)),
        Palette("main.openRecentFile", Strings.OpenRecentFile,
            main => main.MenuBar.RecentFileItems.Count > 0, (main, interaction) => main.OpenRecentFileAsync(interaction)),
        Palette("main.changeTheme", Strings.ChangeTheme,
            _ => ThemeRegistry.Enumerate().Count > 1, (_, interaction) => MainViewModel.ChangeThemeAsync(interaction)),
        Menu("main.enableVersionControl", Strings.VersionControl_Enable, "", [],
            menuBar => menuBar.EnableVersionControl),
        new ContextCommandDefinition<MainViewModel>("main.commitVersion", Strings.VersionControl_Commit, "", [])
        {
            CanExecuteOn = (main, _) => ((ICommand)main.MenuBar.CommitVersion).CanExecute(null),
            // The palette asks for the message; otherwise the menu's flyout does.
            ExecuteOnAsync = (main, execution) => execution.Interaction is { } interaction
                ? main.CommitVersionAsync(interaction)
                : MenuBarViewModel.ExecuteCommandAsync(main.MenuBar.CommitVersion)
        },
        Menu("main.closeProject", Strings.CloseProject, Strings.CloseProject_Description,
            [new("Ctrl+Shift+F4"), new("Cmd+Shift+W", OSPlatform.OSX)], menuBar => menuBar.CloseProject),
        Menu("main.undo", Strings.Undo, Strings.Undo_Description,
            [new("Ctrl+Z"), new("Cmd+Z", OSPlatform.OSX)], menuBar => menuBar.Undo),
        Menu("main.redo", Strings.Redo, Strings.Redo_Description,
            [new("Ctrl+Y"), new("Cmd+Shift+Z", OSPlatform.OSX)], menuBar => menuBar.Redo),
        Menu("main.exit", Strings.Exit, Strings.Exit_Description,
            [new("Alt+F4"), new("Cmd+Q", OSPlatform.OSX)], menuBar => menuBar.Exit),
        new ContextCommandDefinition<MainViewModel>(ShowCommandPaletteCommandId, Strings.ShowCommandPalette,
            Strings.ShowCommandPalette_Description, [new("Ctrl+Shift+P"), new("Cmd+Shift+P", OSPlatform.OSX)])
        {
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
        string id, string displayName, ContextCommandKeyGesture[] keyGestures)
        => new(id, displayName, "", keyGestures)
        {
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
        string id, string displayName, string description, ContextCommandKeyGesture[] keyGestures,
        Func<MenuBarViewModel, ICommand> command)
        => new(id, displayName, description, keyGestures)
        {
            CanExecuteOn = (main, _) => command(main.MenuBar).CanExecute(null),
            ExecuteOnAsync = (main, _) => MenuBarViewModel.ExecuteCommandAsync(command(main.MenuBar))
        };

    // Palette-only: it asks for its argument, so it has no default gesture.
    private static ContextCommandDefinition<MainViewModel> Palette(
        string id, string displayName, Func<MainViewModel, bool> canExecute,
        Func<MainViewModel, IContextCommandInteraction, Task> execute)
        => new(id, displayName, "", [])
        {
            CanExecuteOn = (main, _) => canExecute(main),
            ExecuteOnAsync = (main, execution) =>
                execution.Interaction is { } interaction ? execute(main, interaction) : Task.CompletedTask
        };
}
