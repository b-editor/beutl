using System.Runtime.InteropServices;

namespace Beutl.Services.PrimitiveImpls;

// ショートカット用の拡張クラス
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
        new(NextTabCommandName, Strings.TabSwitcher_Next, "",
        [new ContextCommandKeyGesture("Ctrl+Tab")]) { Id = "main.nextTab" },
        new(PreviousTabCommandName, Strings.TabSwitcher_Previous, "",
        [new ContextCommandKeyGesture("Ctrl+Shift+Tab")]) { Id = "main.previousTab" },
        new(NextToolTabCommandName, Strings.TabSwitcher_NextTool, "",
        [new ContextCommandKeyGesture("Alt+F7"), new ContextCommandKeyGesture("Alt+Tab")]) { Id = "main.nextToolTab" },
        new(PreviousToolTabCommandName, Strings.TabSwitcher_PreviousTool, "",
        [new ContextCommandKeyGesture("Alt+Shift+F7"), new ContextCommandKeyGesture("Alt+Shift+Tab")]) { Id = "main.previousToolTab" },
        new(CreateToolTabCommandName, Strings.TabSwitcher_Create, "",
        [new ContextCommandKeyGesture("Ctrl+T"), new ContextCommandKeyGesture("Cmd+T", OSPlatform.OSX)]) { Id = "main.createToolTab" },
        new("CreateNewProject", Strings.CreateNewProject, "",
        [
            new ContextCommandKeyGesture("Ctrl+Shift+N"),
            new ContextCommandKeyGesture("Cmd+Shift+N", OSPlatform.OSX),
        ]) { Id = "main.createNewProject" },
        new("CreateNewFile", Strings.CreateNewScene, "",
        [
            new ContextCommandKeyGesture("Ctrl+N"),
            new ContextCommandKeyGesture("Cmd+N", OSPlatform.OSX),
        ]) { Id = "main.createNewFile" },
        new("OpenProject", Strings.OpenProject, "",
        [
            new ContextCommandKeyGesture("Ctrl+Shift+O"),
            new ContextCommandKeyGesture("Cmd+Shift+O", OSPlatform.OSX),
        ]) { Id = "main.openProject" },
        new("OpenFile", Strings.OpenFile, "",
        [
            new ContextCommandKeyGesture("Ctrl+O"),
            new ContextCommandKeyGesture("Cmd+O", OSPlatform.OSX),
        ]) { Id = "main.openFile" },
        new("Save", Strings.Save, Strings.Save_Description,
        [
            new ContextCommandKeyGesture("Ctrl+S"),
            new ContextCommandKeyGesture("Cmd+S", OSPlatform.OSX),
        ]) { Id = "main.save" },
        new("SaveAll", Strings.SaveAll, Strings.SaveAll_Description,
        [
            new ContextCommandKeyGesture("Ctrl+Shift+S"),
            new ContextCommandKeyGesture("Cmd+Shift+S", OSPlatform.OSX),
        ]) { Id = "main.saveAll" },
        new("OpenRecentProject", Strings.OpenRecentProject, "", []) { Id = "main.openRecentProject" },
        new("OpenRecentFile", Strings.OpenRecentFile, "", []) { Id = "main.openRecentFile" },
        new("ChangeTheme", Strings.ChangeTheme, "", []) { Id = "main.changeTheme" },
        new("EnableVersionControl", Strings.VersionControl_Enable, "", []) { Id = "main.enableVersionControl" },
        new("CommitVersion", Strings.VersionControl_Commit, "", []) { Id = "main.commitVersion" },
        new("CloseProject", Strings.CloseProject, Strings.CloseProject_Description,
        [
            new ContextCommandKeyGesture("Ctrl+Shift+F4"),
            new ContextCommandKeyGesture("Cmd+Shift+W", OSPlatform.OSX),
        ]) { Id = "main.closeProject" },
        new("Undo", Strings.Undo, Strings.Undo_Description,
        [
            new ContextCommandKeyGesture("Ctrl+Z"),
            new ContextCommandKeyGesture("Cmd+Z", OSPlatform.OSX),
        ]) { Id = "main.undo" },
        new("Redo", Strings.Redo, Strings.Redo_Description,
        [
            new ContextCommandKeyGesture("Ctrl+Y"),
            new ContextCommandKeyGesture("Cmd+Shift+Z", OSPlatform.OSX),
        ]) { Id = "main.redo" },
        new("Exit", Strings.Exit, Strings.Exit_Description,
        [
            new ContextCommandKeyGesture("Alt+F4"),
            new ContextCommandKeyGesture("Cmd+Q", OSPlatform.OSX),
        ]) { Id = "main.exit" },
        new(ShowCommandPaletteCommandName, Strings.ShowCommandPalette, Strings.ShowCommandPalette_Description,
        [
            new ContextCommandKeyGesture("Ctrl+Shift+P"),
            new ContextCommandKeyGesture("Cmd+Shift+P", OSPlatform.OSX),
        ]) { Id = "main.showCommandPalette" },
    ];
}
