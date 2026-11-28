using System.Runtime.InteropServices;
using Avalonia.Headless.NUnit;
using Avalonia.Input;
using Beutl.Api.Services;
using Beutl.Extensibility;
using Beutl.Services.PrimitiveImpls;

namespace Beutl.HeadlessUITests;

// A key bubbles from the control with the focus to the window (Avalonia's KeyDown routing), and
// ContextCommandManager.Attach subscribes each extension on that path:
// - MainViewExtension on MainView, the window's root (Views/MainView.axaml.cs),
// - an EditorExtension on the editor's control inside it (Views/EditorHostView.cs),
// - a ToolTabExtension on the tab's content inside the editor (Views/ToolTabContent.cs),
// - a ToolWindowExtension on its own window (Views/ToolWindowLauncher.cs).
// Dispatch runs the first command that can run and marks the key handled
// (ContextCommandManager.DispatchAsync), so a key in a tab reaches that tab, the editor and the
// window, but never another tab. A default two of those places share would silently leave one of
// its commands unreachable; this test rules that out for every OS (#4756).
[TestFixture, NonParallelizable]
public sealed class ShortcutClashTests
{
    [AvaloniaTest]
    public void No_default_gesture_is_bound_to_two_commands_that_one_focus_reaches()
    {
        string[] clashes = FindClashes(TestShell.MainViewModel.ContextCommandManager);

        Assert.That(clashes, Is.Empty, string.Join(Environment.NewLine, clashes));
    }

    private static string[] FindClashes(ContextCommandManager manager)
    {
        List<Binding> bindings = [];
        foreach (ContextCommandEntry entry in manager.GetDefinitions())
        {
            Reach reach = ReachOf(entry);
            foreach (ContextCommandKeyGesture declared in entry.Definition.KeyGestures ?? [])
            {
                if (declared.KeyGesture is null || declared.Platform is not { } platform) continue;

                KeyGesture gesture;
                try
                {
                    gesture = KeyGesture.Parse(declared.KeyGesture);
                }
                catch (Exception ex) when (ex is not OutOfMemoryException)
                {
                    Assert.Fail($"{entry.Definition.Id} declares a gesture that does not parse: {declared.KeyGesture}");
                    throw;
                }

                bindings.Add(new Binding(platform, reach, entry.Definition, gesture));
            }
        }

        var clashes = new List<string>();
        for (int i = 0; i < bindings.Count; i++)
        {
            for (int j = i + 1; j < bindings.Count; j++)
            {
                Binding a = bindings[i];
                Binding b = bindings[j];
                if (a.Platform != b.Platform
                    || a.Definition == b.Definition
                    || !Reaches(a.Reach, b.Reach)
                    || !IsSameChord(a.Gesture, b.Gesture)
                    || ShareExclusiveKeyboardGroup(a.Definition, b.Definition)
                    || IsAllowed(a, b))
                {
                    continue;
                }

                clashes.Add(Format(a, b));
            }
        }

        return [.. clashes.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)];
    }

    // Where a key press can reach the command. The main window's commands reach every region of
    // the main window; the editor's reach the dock and every tool tab; a tool tab's reach only
    // that tab; a tool window's reach only its own window.
    private static Reach ReachOf(ContextCommandEntry entry)
    {
        Type type = entry.ExtensionType;
        if (type == typeof(MainViewExtension))
            return new Reach(ReachKind.MainWindow, "main window");

        if (typeof(EditorExtension).IsAssignableFrom(type))
        {
            // These four run only when the key came from the preview
            // (SceneEditorExtension.PreviewToolCommand), so no other tab can reach them.
            return IsPreviewTool(entry.Definition.Id)
                ? new Reach(ReachKind.ToolTab, "preview")
                : new Reach(ReachKind.Editor, "editor");
        }

        if (typeof(ToolTabExtension).IsAssignableFrom(type))
            return new Reach(ReachKind.ToolTab, type.Name);

        if (typeof(ToolWindowExtension).IsAssignableFrom(type))
            return new Reach(ReachKind.ToolWindow, type.Name);

        // A plain ViewExtension has no element the host attaches, so no key reaches its commands.
        // Failing here makes a newly added kind of extension with keys tell this test where they go.
        if (entry.Definition.KeyGestures is { Length: > 0 })
        {
            throw new InvalidOperationException(
                $"{type.FullName} owns key gestures but this test does not know where its keys are attached.");
        }

        return new Reach(ReachKind.Unattached, type.Name);
    }

    private static bool IsPreviewTool(string commandId)
        => commandId is SceneEditorExtension.SelectPreviewMoveToolCommandId
            or SceneEditorExtension.SelectPreviewHandToolCommandId
            or SceneEditorExtension.SelectPreviewCropToolCommandId
            or SceneEditorExtension.SelectPreviewCameraToolCommandId;

    private static bool Reaches(Reach a, Reach b)
    {
        if (a.Kind == ReachKind.Unattached || b.Kind == ReachKind.Unattached)
            return false;

        // Another window is another keyboard focus.
        if (a.Kind == ReachKind.ToolWindow || b.Kind == ReachKind.ToolWindow)
            return a.Kind == b.Kind && a.Region == b.Region;

        // Only the focused tab receives the key, so two tabs never reach each other.
        if (a.Kind == ReachKind.ToolTab && b.Kind == ReachKind.ToolTab)
            return a.Region == b.Region;

        return true;
    }

    // The dispatch uses KeyGesture.Matches, which also equates the keypad's Add and OemPlus.
    private static bool IsSameChord(KeyGesture a, KeyGesture b)
        => a.Matches(AsArgs(b)) || b.Matches(AsArgs(a));

    private static KeyEventArgs AsArgs(KeyGesture gesture)
        => new() { Key = gesture.Key, KeyModifiers = gesture.KeyModifiers };

    // Modes of one control are mutually exclusive, so their exits may share an exit chord.
    private static bool ShareExclusiveKeyboardGroup(ContextCommandDefinition a, ContextCommandDefinition b)
    {
        string? group = a.ExclusiveKeyboardGroup;
        return group is not null && group == b.ExclusiveKeyboardGroup;
    }

    private static bool IsAllowed(Binding a, Binding b)
        => AllowedPairs.Any(pair => pair.Matches(a, b));

    private static string Format(Binding a, Binding b)
    {
        (Binding first, Binding second) = string.CompareOrdinal(a.Definition.Id, b.Definition.Id) < 0 ? (a, b) : (b, a);
        return $"{a.Gesture} | {PlatformName(a.Platform)} | {ScopeLabel(a.Reach, b.Reach)} | " +
               $"{first.Definition.Id} | {second.Definition.Id}";
    }

    private static string PlatformName(OSPlatform platform)
        => platform == OSPlatform.OSX ? "macOS" : platform == OSPlatform.Windows ? "Windows" : "Linux";

    private static string ScopeLabel(Reach a, Reach b)
    {
        if (a.Kind == ReachKind.ToolTab || b.Kind == ReachKind.ToolTab)
            return $"{(a.Kind == ReachKind.ToolTab ? a : b).Region} tab";

        if (a.Kind == ReachKind.ToolWindow)
            return $"{a.Region} window";

        return a.Kind == ReachKind.Editor || b.Kind == ReachKind.Editor ? "editor" : "main window";
    }

    private enum ReachKind
    {
        MainWindow,
        Editor,
        ToolTab,
        ToolWindow,
        Unattached
    }

    private sealed record Reach(ReachKind Kind, string Region);

    private sealed record Binding(
        OSPlatform Platform,
        Reach Reach,
        ContextCommandDefinition Definition,
        KeyGesture Gesture);

    // Keys shared on purpose. The dispatch reaches the tab's command before the editor's, so the
    // tab's meaning wins while it has the focus; a null platform means every OS. Keep this short:
    // an entry is not a way to silence a real clash.
    private static readonly AllowedPair[] AllowedPairs =
    [
        // Space starts a Space-drag pan in these canvases, as before the key map made them commands.
        new("editor.playPause", "graphEditor.pan", null),
        new("editor.playPause", "pathEditor.pan", null),
        // The path editor's arrows move the selected anchors, the reason for the tab's own nudge keys.
        new("editor.previous", "pathEditor.nudgeLeft", null),
        new("editor.next", "pathEditor.nudgeRight", null),
        // The graph's J/K navigate its keyframes and have taken precedence over the shuttle since
        // #4659, which kept the old handler's keys, including Shift+J.
        new("editor.shuttleBackward", "graphEditor.previousKeyFrame", null),
        new("editor.shuttleBackwardFine", "graphEditor.previousKeyFrame", null),
        new("editor.shuttleStop", "graphEditor.nextKeyFrame", null),
        // Both undo (redo) the same editor history; the graph's pauses playback first. Redo is a
        // clash only on macOS, where both are Cmd+Shift+Z (Windows and Linux redo with Ctrl+Y).
        new("main.undo", "graphEditor.undo", null),
        new("main.redo", "graphEditor.redo", OSPlatform.OSX),
        // On macOS Alt+arrow nudges the selected keyframes while the graph has focus and jumps between
        // markers elsewhere; the graph keeps precedence as it does for J/K.
        new("editor.previousMarker", "graphEditor.nudgeLeftFrame", OSPlatform.OSX),
        new("editor.nextMarker", "graphEditor.nudgeRightFrame", OSPlatform.OSX),
    ];

    private sealed record AllowedPair(string First, string Second, OSPlatform? Platform)
    {
        public bool Matches(Binding a, Binding b)
            => (a.Platform == Platform || Platform is null)
               && (Pair(a.Definition.Id, b.Definition.Id) || Pair(b.Definition.Id, a.Definition.Id));

        private bool Pair(string first, string second)
            => first == First && second == Second;
    }
}
