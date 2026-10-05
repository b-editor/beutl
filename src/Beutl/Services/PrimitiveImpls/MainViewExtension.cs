using System.Runtime.InteropServices;

namespace Beutl.Services.PrimitiveImpls;

// ショートカット用の拡張クラス
[PrimitiveImpl]
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
        [new ContextCommandKeyGesture("Ctrl+Tab")]),
        new(PreviousTabCommandName, Strings.TabSwitcher_Previous, "",
        [new ContextCommandKeyGesture("Ctrl+Shift+Tab")]),
        new(NextToolTabCommandName, Strings.TabSwitcher_NextTool, "",
        [new ContextCommandKeyGesture("Alt+F7"), new ContextCommandKeyGesture("Alt+Tab")]),
        new(PreviousToolTabCommandName, Strings.TabSwitcher_PreviousTool, "",
        [new ContextCommandKeyGesture("Alt+Shift+F7"), new ContextCommandKeyGesture("Alt+Shift+Tab")]),
        new(CreateToolTabCommandName, Strings.TabSwitcher_Create, "",
        [new ContextCommandKeyGesture("Ctrl+T"), new ContextCommandKeyGesture("Cmd+T", OSPlatform.OSX)]),
        new("CreateNewProject", Strings.CreateNewProject, "",
        [
            new ContextCommandKeyGesture("Ctrl+Shift+N"),
            new ContextCommandKeyGesture("Cmd+Shift+N", OSPlatform.OSX),
        ]),
        new("CreateNewFile", Strings.CreateNewScene, "",
        [
            new ContextCommandKeyGesture("Ctrl+N"),
            new ContextCommandKeyGesture("Cmd+N", OSPlatform.OSX),
        ]),
        new("OpenProject", Strings.OpenProject, "",
        [
            new ContextCommandKeyGesture("Ctrl+Shift+O"),
            new ContextCommandKeyGesture("Cmd+Shift+O", OSPlatform.OSX),
        ]),
        new("OpenFile", Strings.OpenFile, "",
        [
            new ContextCommandKeyGesture("Ctrl+O"),
            new ContextCommandKeyGesture("Cmd+O", OSPlatform.OSX),
        ]),
        new("Save", Strings.Save, Strings.Save_Description,
        [
            new ContextCommandKeyGesture("Ctrl+S"),
            new ContextCommandKeyGesture("Cmd+S", OSPlatform.OSX),
        ]),
        new("SaveAll", Strings.SaveAll, Strings.SaveAll_Description,
        [
            new ContextCommandKeyGesture("Ctrl+Shift+S"),
            new ContextCommandKeyGesture("Cmd+Shift+S", OSPlatform.OSX),
        ]),
        new("EnableVersionControl", Strings.VersionControl_Enable, "", []),
        new("CommitVersion", Strings.VersionControl_Commit, "", []),
        new("CloseProject", Strings.CloseProject, Strings.CloseProject_Description,
        [
            new ContextCommandKeyGesture("Ctrl+Shift+F4"),
            new ContextCommandKeyGesture("Cmd+Shift+W", OSPlatform.OSX),
        ]),
        new("Undo", Strings.Undo, Strings.Undo_Description,
        [
            new ContextCommandKeyGesture("Ctrl+Z"),
            new ContextCommandKeyGesture("Cmd+Z", OSPlatform.OSX),
        ]),
        new("Redo", Strings.Redo, Strings.Redo_Description,
        [
            new ContextCommandKeyGesture("Ctrl+Y"),
            new ContextCommandKeyGesture("Cmd+Shift+Z", OSPlatform.OSX),
        ]),
        new("Exit", Strings.Exit, Strings.Exit_Description,
        [
            new ContextCommandKeyGesture("Alt+F4"),
            new ContextCommandKeyGesture("Cmd+Q", OSPlatform.OSX),
        ]),
        new(ShowCommandPaletteCommandName, Strings.ShowCommandPalette, Strings.ShowCommandPalette_Description,
        [
            new ContextCommandKeyGesture("Ctrl+Shift+P"),
            new ContextCommandKeyGesture("Cmd+Shift+P", OSPlatform.OSX),
        ]),
    ];
}
