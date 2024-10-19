using System.Runtime.InteropServices;

namespace Beutl.Services.PrimitiveImpls;

// ショートカット用の拡張クラス
[PrimitiveImpl]
public class MainViewExtension : ViewExtension
{
    public static readonly MainViewExtension Instance = new();

    public override string Name => "MainView";

    public override string DisplayName => Strings.MainView;

    public override IEnumerable<ContextCommandDefinition> ContextCommands =>
    [
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
    ];
}
