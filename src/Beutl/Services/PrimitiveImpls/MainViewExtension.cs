using System.Runtime.InteropServices;

namespace Beutl.Services.PrimitiveImpls;

// ショートカット用の拡張クラス
[PrimitiveImpl]
public class MainViewExtension : ViewExtension
{
    public static readonly MainViewExtension Instance = new();

    public override string Name => "MainView";

    public override string DisplayName => "Main View";

    public override IEnumerable<ContextCommandDefinition> ContextCommands =>
    [
        new("CreateNewProject", "Create New Project", "Create a new project",
        [
            new ContextCommandKeyGesture("Ctrl+Shift+N"),
            new ContextCommandKeyGesture("Cmd+Shift+N", OSPlatform.OSX),
        ]),
        new("CreateNewFile", "Create New File", "Create a new file",
        [
            new ContextCommandKeyGesture("Ctrl+N"),
            new ContextCommandKeyGesture("Cmd+N", OSPlatform.OSX),
        ]),
        new("OpenProject", "Open Project", "Open a project",
        [
            new ContextCommandKeyGesture("Ctrl+Shift+O"),
            new ContextCommandKeyGesture("Cmd+Shift+O", OSPlatform.OSX),
        ]),
        new("OpenFile", "Open File", "Open a file",
        [
            new ContextCommandKeyGesture("Ctrl+O"),
            new ContextCommandKeyGesture("Cmd+O", OSPlatform.OSX),
        ]),
        new("Save", "Save", "Save the current file",
        [
            new ContextCommandKeyGesture("Ctrl+S"),
            new ContextCommandKeyGesture("Cmd+S", OSPlatform.OSX),
        ]),
        new("SaveAll", "Save All", "Save all files",
        [
            new ContextCommandKeyGesture("Ctrl+Shift+S"),
            new ContextCommandKeyGesture("Cmd+Shift+S", OSPlatform.OSX),
        ]),
        new("CloseProject", "CloseProject", "Close the current project",
        [
            new ContextCommandKeyGesture("Ctrl+Shift+F4"),
            new ContextCommandKeyGesture("Cmd+Shift+W", OSPlatform.OSX),
        ]),
        new("Undo", "Undo", "Undo the last operation",
        [
            new ContextCommandKeyGesture("Ctrl+Z"),
            new ContextCommandKeyGesture("Cmd+Z", OSPlatform.OSX),
        ]),
        new("Redo", "Redo", "Redo the last operation",
        [
            new ContextCommandKeyGesture("Ctrl+Y"),
            new ContextCommandKeyGesture("Cmd+Shift+Z", OSPlatform.OSX),
        ]),
        new("Exit", "Exit", "Exit the application",
        [
            new ContextCommandKeyGesture("Alt+F4"),
            new ContextCommandKeyGesture("Cmd+Q", OSPlatform.OSX),
        ]),
    ];
}
