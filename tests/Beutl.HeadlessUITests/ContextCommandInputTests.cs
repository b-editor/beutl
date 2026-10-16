using Avalonia.Controls;
using Avalonia.Headless.NUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;

using Beutl.Editor.Components.TerminalTab.ViewModels;
using Beutl.Editor.Components.TerminalTab.Views;
using Beutl.Extensibility;
using Beutl.ProjectSystem;
using Beutl.Services.PrimitiveImpls;
using Beutl.Testing.Headless;
using Beutl.ViewModels;

using Iciclecreek.Terminal;

namespace Beutl.HeadlessUITests;

[TestFixture]
public class ContextCommandInputTests
{
    [AvaloniaTest]
    public void Plain_control_is_not_text_input()
    {
        var args = KeyDownFrom(new Border());

        Assert.That(ContextCommandInput.IsFromTextInput(args), Is.False);
    }

    [AvaloniaTest]
    public void Null_and_sourceless_events_are_not_text_input()
    {
        Assert.Multiple(() =>
        {
            Assert.That(ContextCommandInput.IsFromTextInput(null), Is.False);
            Assert.That(ContextCommandInput.IsFromTextInput(new KeyEventArgs { Key = Key.M }), Is.False);
        });
    }

    [AvaloniaTest]
    public void TextBox_is_text_input()
    {
        var args = KeyDownFrom(new TextBox());

        Assert.That(ContextCommandInput.IsFromTextInput(args), Is.True);
    }

    [AvaloniaTest]
    public void Descendant_of_a_marked_control_is_text_input()
    {
        var inner = new Border();
        var outer = new Border { Child = inner };
        ContextCommandInput.SetIsTextInput(outer, true);
        var window = new Window { Content = outer };
        window.Show();
        try
        {
            Assert.Multiple(() =>
            {
                Assert.That(ContextCommandInput.IsFromTextInput(KeyDownFrom(outer)), Is.True);
                Assert.That(ContextCommandInput.IsFromTextInput(KeyDownFrom(inner)), Is.True);
            });
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaTest]
    public async Task Scene_editor_plain_key_commands_ignore_the_terminal_and_leave_the_key_unhandled()
    {
        await TestReset.ResetShellAsync();
        try
        {
            EditViewModel editor = await TestShell.OpenNewSceneEditorAsync("context-command-input-terminal");
            Scene scene = editor.Scene;

            using var terminalVm = new TerminalTabViewModel(editor);
            using var terminalTab = new TerminalTabView { DataContext = terminalVm };
            var window = new Window { Content = terminalTab };
            window.Show();
            try
            {
                HeadlessTestHelpers.Render();
                TerminalControl control = terminalTab.FindControl<TerminalControl>("Terminal")!;
                // The focused element (and so the KeyDown source) is the inner TerminalView, not the templated control.
                TerminalView terminalView = control.GetVisualDescendants().OfType<TerminalView>().Single();
                int markersBefore = scene.Markers.Count;

                Assert.That(ContextCommandInput.GetIsTextInput(control), Is.True);

                // Typing "m" into the terminal must not toggle a marker. A command that cannot run leaves the key
                // unhandled (ContextCommandHandlerTests), so Avalonia still delivers the TextInput to the shell.
                var fromTerminal = new ContextCommandExecution("editor.toggleMarker") { KeyEventArgs = KeyDownFrom(terminalView) };
                Assert.That(SceneEditorExtension.Instance.CanRun(editor, fromTerminal), Is.False);

                // Negative control: the same gesture from a non-text control still runs the shortcut.
                var fromEditor = new ContextCommandExecution("editor.toggleMarker") { KeyEventArgs = KeyDownFrom(new Border()) };
                Assert.That(SceneEditorExtension.Instance.CanRun(editor, fromEditor), Is.True);
                await SceneEditorExtension.Instance.RunAsync(editor, fromEditor);

                Assert.That(scene.Markers.Count, Is.EqualTo(markersBefore + 1));
            }
            finally
            {
                window.Close();
            }
        }
        finally
        {
            await TestReset.ResetShellAsync();
        }
    }

    private static KeyEventArgs KeyDownFrom(Interactive source, Key key = Key.M)
    {
        return new KeyEventArgs
        {
            RoutedEvent = InputElement.KeyDownEvent,
            Source = source,
            Key = key,
        };
    }
}
