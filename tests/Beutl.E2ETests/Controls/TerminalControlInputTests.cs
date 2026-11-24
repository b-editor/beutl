using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.NUnit;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Beutl.Editor.Components.TerminalTab.Views;
using Beutl.Testing.Headless;
using RoyalTerminal.Avalonia.Controls;
using RoyalTerminal.Terminal;

namespace Beutl.E2ETests.Controls;

[TestFixture]
public class TerminalControlInputTests
{
    [AvaloniaTest]
    public async Task ShiftedLetter_IsSentOnce_ViaTextInput()
    {
        if (OperatingSystem.IsWindows())
        {
            Assert.Ignore("Uses a Unix PTY (/bin/cat); the Windows ConPTY path is not exercised here.");
        }

        using var view = new TerminalTabView();
        using var host = new ControlHost(view, 640, 320);
        var terminal = view.FindControl<TerminalControl>("Terminal")!;
        Window window = host.Window;

        terminal.StartPty("/bin/cat");
        terminal.Focus();
        await HeadlessTestHelpers.WaitUntilAsync(
            () => terminal.HasActiveSession, TimeSpan.FromSeconds(5), "the PTY process did not start");

        // A real desktop keystroke of Shift+A arrives as KeyDown (KeySymbol is the
        // unshifted "a" on macOS) followed by a TextInput event carrying "A".
        window.KeyPress(Key.A, RawInputModifiers.Shift, PhysicalKey.A, "a");
        window.KeyTextInput("A");
        HeadlessTestHelpers.Settle();

        // /bin/cat runs with tty echo on, so what the control writes to the PTY
        // comes straight back into the terminal buffer.
        await HeadlessTestHelpers.WaitUntilAsync(
            () => ScreenText(terminal).Contains('A'), TimeSpan.FromSeconds(5), "the echoed input never appeared");

        string text = ScreenText(terminal);
        Assert.That(text.Trim(), Is.EqualTo("A"),
            "Shift+A must reach the PTY exactly once and as an uppercase letter");
    }

    [AvaloniaTest]
    public async Task TerminalTab_RightClickAndControlCUseTheSelection()
    {
        if (OperatingSystem.IsWindows()) Assert.Ignore("Uses a Unix PTY with /bin/cat.");

        using var view = new TerminalTabView();
        using var host = new ControlHost(view, 640, 320);
        var clipboard = host.Window.Clipboard ?? throw new AssertionException("Expected a clipboard on the headless window.");
        var terminal = view.FindControl<TerminalControl>("Terminal")!;
        terminal.StartPty("/bin/cat");
        terminal.WriteOutput("copy this"u8);
        terminal.SelectAll();
        HeadlessTestHelpers.Click(terminal, new Point(10, 10), MouseButton.Right);
        Assert.That((await clipboard.TryGetTextAsync())?.Trim(), Is.EqualTo("copy this"));
        Assert.That(terminal.HasSelection, Is.False);

        await clipboard.SetTextAsync("paste this");
        HeadlessTestHelpers.Click(terminal, new Point(10, 10), MouseButton.Right);
        await HeadlessTestHelpers.WaitUntilAsync(() => ScreenText(terminal).Contains("paste this"));
        terminal.SelectAll();
        host.Window.KeyPress(Key.C, RawInputModifiers.Control, PhysicalKey.C, "c");
        host.Window.KeyRelease(Key.C, RawInputModifiers.Control, PhysicalKey.C, "c");
        HeadlessTestHelpers.Settle();
        Assert.That(await clipboard.TryGetTextAsync(), Does.Contain("copy this"));
        Assert.That(terminal.HasActiveSession, Is.True);

        terminal.WriteOutput("\u001b[?1000h"u8);
        terminal.SelectAll();
        await host.Window.Clipboard.SetTextAsync("mouse reporting");
        HeadlessTestHelpers.Click(terminal, new Point(10, 10), MouseButton.Right);
        Assert.That(await host.Window.Clipboard.TryGetTextAsync(), Is.EqualTo("mouse reporting"));

        terminal.ClearSelection();
        bool exited = false;
        terminal.ProcessExited += (_, _) => exited = true;
        host.Window.KeyPress(Key.C, RawInputModifiers.Control, PhysicalKey.C, "c");
        await HeadlessTestHelpers.WaitUntilAsync(() => exited);
    }

    private static string ScreenText(TerminalControl control)
    {
        Assert.That(control.TryExportSnapshot(TerminalSnapshotExportFormat.PlainText, default, out string text), Is.True);
        return text;
    }
}
