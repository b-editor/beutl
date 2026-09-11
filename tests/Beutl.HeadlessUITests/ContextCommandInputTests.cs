using Avalonia.Controls;
using Avalonia.Headless.NUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;

using Beutl.Editor.Components.TerminalTab.ViewModels;
using Beutl.Editor.Components.TerminalTab.Views;
using Beutl.Extensibility;
using Beutl.ProjectSystem;
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
            string location = Path.Combine(BeutlHomeIsolation.CurrentHome!, "context-command-input-terminal");
            Directory.CreateDirectory(location);
            Project project = (await TestShell.Project.CreateProject(
                640, 480, 30, 44100, "terminal-keys", location))!;
            Scene scene = project.Items.OfType<Scene>().Single();
            TestShell.Editor.ActivateTabItem(scene);
            HeadlessTestHelpers.Settle();
            var editor = (EditViewModel)TestShell.Editor.SelectedTabItem.Value!.Context.Value;

            using var terminalVm = new TerminalTabViewModel(editor);
            using var terminalTab = new TerminalTabView { DataContext = terminalVm };
            var window = new Window { Content = terminalTab };
            window.Show();
            using var _ = new WindowCloser(window);
            HeadlessTestHelpers.Render();
            TerminalControl control = terminalTab.FindControl<TerminalControl>("Terminal")!;
            // The focused element (and so the KeyDown source) is the inner TerminalView, not the templated control.
            TerminalView terminalView = control.GetVisualDescendants().OfType<TerminalView>().Single();
            int markersBefore = scene.Markers.Count;

            Assert.That(ContextCommandInput.GetIsTextInput(control), Is.True);

            // Typing "m" into the terminal must not toggle a marker, and the key must stay unhandled so
            // Avalonia still delivers the TextInput to the shell.
            KeyEventArgs fromTerminal = KeyDownFrom(terminalView, Key.M);
            await editor.ExecuteAsync(new ContextCommandExecution("ToggleMarker") { KeyEventArgs = fromTerminal });

            Assert.Multiple(() =>
            {
                Assert.That(scene.Markers.Count, Is.EqualTo(markersBefore));
                Assert.That(fromTerminal.Handled, Is.False);
            });

            // Negative control: the same gesture from a non-text control still runs the shortcut.
            KeyEventArgs fromEditor = KeyDownFrom(new Border(), Key.M);
            await editor.ExecuteAsync(new ContextCommandExecution("ToggleMarker") { KeyEventArgs = fromEditor });

            Assert.Multiple(() =>
            {
                Assert.That(scene.Markers.Count, Is.EqualTo(markersBefore + 1));
                Assert.That(fromEditor.Handled, Is.True);
            });
        }
        finally
        {
            await TestReset.ResetShellAsync();
        }
    }

    private sealed class WindowCloser(Window window) : IDisposable
    {
        public void Dispose() => window.Close();
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
