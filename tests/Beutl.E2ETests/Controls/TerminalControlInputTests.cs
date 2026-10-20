using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.NUnit;
using Avalonia.Input;
using Beutl.Testing.Headless;
using Iciclecreek.Terminal;

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

        var terminal = new TerminalControl { Process = "/bin/cat" };
        var host = new ControlHost(terminal, 640, 320);
        Window window = host.Window;

        try
        {
            terminal.Focus();
            await HeadlessTestHelpers.WaitUntilAsync(
                () => terminal.Pid > 0, TimeSpan.FromSeconds(5), "the PTY process did not start");

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
        finally
        {
            try
            {
                terminal.Kill();
            }
            catch
            {
                // The PTY may never have started; closing the window is what matters.
            }

            host.Dispose();
        }
    }

    private static string ScreenText(TerminalControl control)
    {
        var buffer = control.Terminal.Buffer;
        var lines = new List<string>();
        for (int y = 0; y < control.Terminal.Rows; y++)
        {
            lines.Add(buffer.GetLine(buffer.YBase + y)?.TranslateToString(trimRight: true) ?? "");
        }

        return string.Join("\n", lines);
    }
}
