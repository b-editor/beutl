using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.NUnit;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Layout;
using Avalonia.VisualTree;
using Beutl.Editor.Components;
using Beutl.Editor.Components.Helpers;
using Beutl.Editor.Components.TerminalTab.Views;
using Beutl.Editor.Components.TimelineTab.Views;
using Beutl.Editor.Services;
using Beutl.Graphics.Shapes;
using Beutl.Language;
using Beutl.ProjectSystem;
using Beutl.Serialization;
using Beutl.Testing.Headless;
using Beutl.ViewModels;
using Beutl.Views;
using Microsoft.Extensions.DependencyInjection;
using RoyalTerminal.Terminal;

namespace Beutl.HeadlessUITests;

[TestFixture, NonParallelizable]
public sealed class InWindowEditMenuTests
{
    [AvaloniaTest]
    [TestCase(false)]
    [TestCase(true)]
    public async Task Edit_commands_target_the_text_box_selection(bool keyboard)
    {
        (_, Scene scene, EditViewModel editor) = await TestShell.OpenNewSceneAsync("edit-menu");
        await TestShell.AddElementAsync(editor, () => new RectShape(), MediaTime.Zero, MediaTime.FromSeconds(1));
        Element element = scene.Children.Single();
        var selection = editor.GetRequiredService<IEditorSelection>();
        selection.Select(element);
        using var host = new EditMenuWindow();

        SelectText();
        host.OpenEdit(keyboard);
        if (!keyboard)
            HeadlessCapture.SaveIfRequested(host.FindEditItem(Strings.Copy), nameof(InWindowEditMenuTests), "edit-menu-after");
        host.InvokeEdit(Strings.Copy, keyboard);
        Assert.That(await host.Clipboard.TryGetTextAsync(), Is.EqualTo("selected"));
        Assert.That(host.Box.SelectedText, Is.EqualTo("selected"));

        SelectText();
        host.RunEdit(Strings.Cut, keyboard);
        await HeadlessTestHelpers.WaitUntilAsync(() => host.Box.Text == "Before  after");
        Assert.That(await host.Clipboard.TryGetTextAsync(), Is.EqualTo("selected"));

        SelectText();
        await host.Clipboard.SetTextAsync("pasted");
        host.RunEdit(Strings.Paste, keyboard);
        await HeadlessTestHelpers.WaitUntilAsync(() => host.Box.Text == "Before pasted after");

        SelectText();
        host.RunEdit(Strings.Delete, keyboard);
        Assert.That(host.Box.Text, Is.EqualTo("Before  after"));
        Assert.That(await host.Clipboard.TryGetTextAsync(), Is.EqualTo("pasted"));
        Assert.That(scene.Children, Is.EqualTo(new[] { element }));

        selection.Select(null);
        SelectText();
        host.RunEdit(Strings.SelectAll, keyboard);
        Assert.That(host.Box.SelectedText, Is.EqualTo(host.Box.Text));
        Assert.That(selection.SelectedElements, Is.Empty);

        void SelectText()
        {
            Assert.That(host.Box.Focus(), Is.True);
            host.Box.Text = "Before selected after";
            host.Box.SelectionStart = 7;
            host.Box.SelectionEnd = 15;
            Assert.That(host.Box.SelectedText, Is.EqualTo("selected"));
        }
    }

    [AvaloniaTest]
    [TestCase(false)]
    [TestCase(true)]
    public async Task Edit_commands_target_elements_when_the_timeline_has_focus(bool keyboard)
    {
        (_, Scene scene, EditViewModel editor) = await TestShell.OpenNewSceneAsync("timeline-edit-menu");
        await TestShell.AddElementAsync(editor, () => new RectShape(), MediaTime.Zero, MediaTime.FromSeconds(1));
        await TestShell.AddElementAsync(editor, () => new RectShape(), MediaTime.FromSeconds(2), MediaTime.FromSeconds(1));
        Element first = scene.Children[0];
        Element second = scene.Children[1];
        string firstJson = CoreSerializer.SerializeToJsonString(first);
        var selection = editor.GetRequiredService<IEditorSelection>();
        using var host = new EditMenuWindow();
        TimelineTabView timeline = host.Window.GetVisualDescendants().OfType<TimelineTabView>().Single();

        Assert.That(host.Box.Focus(), Is.True);
        host.Box.SelectAll();
        host.OpenEdit(keyboard);
        host.Menu.Close();

        selection.Select(first);
        RunTimelineEdit(Strings.Copy);
        Assert.That(await host.Clipboard.TryGetValueAsync(BeutlDataFormats.Element), Is.EqualTo(firstJson));
        Assert.That(scene.Children, Has.Count.EqualTo(2));

        await host.Clipboard.SetTextAsync("clipboard");
        RunTimelineEdit(Strings.Cut);
        await HeadlessTestHelpers.WaitUntilAsync(() => !scene.Children.Contains(first));
        Assert.That(await host.Clipboard.TryGetValueAsync(BeutlDataFormats.Element), Is.EqualTo(firstJson));
        Assert.That(scene.Children, Is.EqualTo(new[] { second }));

        RunTimelineEdit(Strings.Paste);
        await HeadlessTestHelpers.WaitUntilAsync(() => scene.Children.Count == 2);
        Element pasted = scene.Children.Single(element => element != second);

        selection.Select(null);
        RunTimelineEdit(Strings.SelectAll);
        Assert.That(selection.SelectedElements, Is.EquivalentTo(new[] { second, pasted }));

        selection.SelectedElements.Remove(pasted);
        Assert.That(selection.SelectedElements, Is.EqualTo(new[] { second }));
        RunTimelineEdit(Strings.Delete);
        Assert.That(scene.Children, Is.EqualTo(new[] { pasted }));
        Assert.That(host.Box.Text, Is.EqualTo("Before selected after"));

        void RunTimelineEdit(string header)
        {
            Assert.That(timeline.Focus(), Is.True);
            host.RunEdit(header, keyboard);
        }
    }

    [AvaloniaTest]
    public async Task Text_edit_entries_are_available_without_a_scene()
    {
        await TestReset.ResetShellAsync();
        using var host = new EditMenuWindow();
        host.OpenEdit(keyboard: false);
        Assert.That(host.FindEditItem(Strings.Copy).IsEffectivelyEnabled, Is.False);
        host.Menu.Close();

        Assert.That(host.Box.Focus(), Is.True);
        host.Box.SelectAll();
        host.RunEdit(Strings.Copy, keyboard: false);
        Assert.That(await host.Clipboard.TryGetTextAsync(), Is.EqualTo(host.Box.Text));
    }

    [AvaloniaTest]
    public async Task Text_clipboard_commands_respect_read_only_and_password_inputs()
    {
        await TestReset.ResetShellAsync();
        using var host = new EditMenuWindow();
        const string clipboardText = "clipboard text";
        string? originalText = host.Box.Text;
        await host.Clipboard.SetTextAsync(clipboardText);

        host.Box.IsReadOnly = true;
        RunEditWithSelection(Strings.Cut);
        RunEditWithSelection(Strings.Paste);
        Assert.That(host.Box.Text, Is.EqualTo(originalText));
        Assert.That(await host.Clipboard.TryGetTextAsync(), Is.EqualTo(clipboardText));

        host.Box.IsReadOnly = false;
        host.Box.PasswordChar = '*';
        RunEditWithSelection(Strings.Copy);
        RunEditWithSelection(Strings.Cut);
        Assert.That(host.Box.Text, Is.EqualTo(originalText));
        Assert.That(await host.Clipboard.TryGetTextAsync(), Is.EqualTo(clipboardText));

        void RunEditWithSelection(string header)
        {
            Assert.That(host.Box.Focus(), Is.True);
            host.Box.SelectAll();
            host.RunEdit(header, keyboard: false);
        }
    }

    [AvaloniaTest]
    [TestCase(false)]
    [TestCase(true)]
    public async Task Edit_menu_uses_the_focused_terminals_clipboard_actions(bool keyboard)
    {
        await TestReset.ResetShellAsync();
        using var terminalView = new TerminalTabView();
        using var host = new EditMenuWindow(terminalView);
        var terminal = terminalView.FindControl<ShellTerminalControl>("Terminal")!;
        var endpoint = new RecordingTerminalEndpoint();
        terminal.VtProcessorPreference = VtProcessorPreference.Managed;
        terminal.AttachEndpoint(endpoint);
        try
        {
            const string clipboardText = "clipboard text";
            const string terminalText = "terminal output";
            const string pastedInput = "\x1b[200~" + clipboardText + "\x1b[201~";
            terminal.WriteOutput(Encoding.UTF8.GetBytes(terminalText + "\x1b[?2004h"));
            await host.Clipboard.SetTextAsync(clipboardText);

            RunTerminalEdit(Strings.Paste);
            await HeadlessTestHelpers.WaitUntilAsync(() => endpoint.Input.Count > 0);
            Assert.That(endpoint.Input, Is.EqualTo(new[] { pastedInput }));

            Assert.That(terminal.HasSelection, Is.False);
            RunTerminalEdit(Strings.Copy);
            RunTerminalEdit(Strings.Cut);
            Assert.That(await host.Clipboard.TryGetTextAsync(), Is.EqualTo(clipboardText));
            Assert.That(endpoint.Input, Is.EqualTo(new[] { pastedInput }));

            RunTerminalEdit(Strings.SelectAll);
            Assert.That(terminal.HasSelection, Is.True);
            RunTerminalEdit(Strings.Copy);
            Assert.That((await host.Clipboard.TryGetTextAsync())?.Trim(), Is.EqualTo(terminalText));
            Assert.That(terminal.HasSelection, Is.True);

            await host.Clipboard.SetTextAsync(clipboardText);
            RunTerminalEdit(Strings.Cut);
            await HeadlessTestHelpers.WaitUntilAsync(() => !terminal.HasSelection);
            Assert.That((await host.Clipboard.TryGetTextAsync())?.Trim(), Is.EqualTo(terminalText));
            Assert.That(endpoint.Input, Is.EqualTo(new[] { pastedInput }));

            Assert.That(terminal.IsFocused, Is.True);
            host.Window.KeyPressQwerty(PhysicalKey.V, RawInputModifiers.Control);
            host.Window.KeyReleaseQwerty(PhysicalKey.V, RawInputModifiers.None);
            Assert.That(endpoint.Input, Is.EqualTo(new[] { pastedInput, "\u0016" }));
        }
        finally
        {
            terminal.DetachEndpoint();
        }

        void RunTerminalEdit(string header)
        {
            Assert.That(terminal.Focus(), Is.True);
            host.RunEdit(header, keyboard);
        }
    }

    private sealed class RecordingTerminalEndpoint : ITerminalEndpoint
    {
        public List<string> Input { get; } = [];

        public void SendText(ReadOnlySpan<byte> utf8) => Input.Add(Encoding.UTF8.GetString(utf8));

        public void SetFocus(bool focused) { }

        public void SetSize(int widthPx, int heightPx) { }
    }

    private sealed class EditMenuWindow : IDisposable
    {
        private readonly MainView _view = new() { DataContext = TestShell.MainViewModel };
        private readonly IClipboard? _previousClipboard = ClipboardHelper.OverrideClipboard;
        private readonly MenuItem _edit;

        public EditMenuWindow(Control? input = null)
        {
            input ??= Box;
            Grid.SetRow(input, 2);
            ((Grid)_view.Content!).Children.Add(input);
            Window = new Window { Content = _view, Width = 1000, Height = 680 };
            Window.Show();
            ClipboardHelper.OverrideClipboard = Clipboard;
            Menu = _view.FindControl<Menu>("MenuBar")!;
            Menu.IsVisible = true;
            _edit = Menu.Items.OfType<MenuItem>().Single(item => Equals(item.Header, Strings.Edit));
            HeadlessTestHelpers.Render(3);
        }

        public Window Window { get; }
        public IClipboard Clipboard => Window.Clipboard!;
        public Menu Menu { get; }
        public TextBox Box { get; } = new() { Text = "Before selected after", VerticalAlignment = VerticalAlignment.Bottom };

        public MenuItem FindEditItem(string header)
            => _edit.Items.OfType<MenuItem>().Single(item => Equals(item.Header, header));

        public void RunEdit(string header, bool keyboard)
        {
            OpenEdit(keyboard);
            InvokeEdit(header, keyboard);
        }

        public void OpenEdit(bool keyboard)
        {
            if (keyboard)
            {
                Press(PhysicalKey.AltLeft, RawInputModifiers.Alt);
                Assert.That(Menu.IsOpen, Is.True);
                Press(PhysicalKey.ArrowRight);
                Assert.That(_edit.IsFocused, Is.True);
                Press(PhysicalKey.ArrowDown);
            }
            else
            {
                HeadlessTestHelpers.Click(_edit, move: true, ticks: 5);
            }

            Assert.That(_edit.IsSubMenuOpen, Is.True);
        }

        public void InvokeEdit(string header, bool keyboard)
        {
            MenuItem item = FindEditItem(header);
            Assert.That(item.IsEffectivelyEnabled, Is.True, header);
            if (keyboard)
            {
                for (int i = 0; i < _edit.ItemCount && !item.IsFocused; i++)
                    Press(PhysicalKey.ArrowDown);
                Assert.That(item.IsFocused, Is.True, header);
                Press(PhysicalKey.Enter);
            }
            else
            {
                HeadlessTestHelpers.Click(item, move: true);
            }

            Assert.That(_edit.IsSubMenuOpen, Is.False, header);
        }

        public void Dispose()
        {
            ClipboardHelper.OverrideClipboard = _previousClipboard;
            _view.DataContext = null;
            Window.Close();
            HeadlessTestHelpers.Settle();
        }

        private void Press(PhysicalKey key, RawInputModifiers modifiers = RawInputModifiers.None)
        {
            var topLevel = TopLevel.GetTopLevel(Window.FocusManager?.GetFocusedElement() as Visual) ?? Window;
            topLevel.KeyPressQwerty(key, modifiers);
            topLevel.KeyReleaseQwerty(key, RawInputModifiers.None);
            HeadlessTestHelpers.Render();
        }
    }
}
