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
    public void ShiftedLetter_IsSentOnce_ViaTextInput()
    {
        if (OperatingSystem.IsWindows())
        {
            Assert.Ignore("Uses a Unix PTY (/bin/cat); the Windows ConPTY path is not exercised here.");
        }

        var terminal = new TerminalControl { Process = "/bin/cat" };
        var window = new Window { Width = 640, Height = 320, Content = terminal };
        window.Show();
        HeadlessTestHelpers.Settle();

        try
        {
            terminal.Focus();
            WaitUntil(() => terminal.Pid > 0, "the PTY process did not start");

            // A real desktop keystroke of Shift+A arrives as KeyDown (KeySymbol is the
            // unshifted "a" on macOS) followed by a TextInput event carrying "A".
            window.KeyPress(Key.A, RawInputModifiers.Shift, PhysicalKey.A, "a");
            window.KeyTextInput("A");
            HeadlessTestHelpers.Settle();

            // /bin/cat runs with tty echo on, so what the control writes to the PTY
            // comes straight back into the terminal buffer.
            WaitUntil(() => ScreenText(terminal).Contains('A'), "the echoed input never appeared");

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

            window.Close();
            HeadlessTestHelpers.Settle();
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

    private static void WaitUntil(Func<bool> condition, string failureMessage, int timeoutMs = 5000)
    {
        for (int elapsed = 0; elapsed < timeoutMs; elapsed += 50)
        {
            HeadlessTestHelpers.Settle();
            if (condition())
            {
                return;
            }

            Thread.Sleep(50);
        }

        Assert.Fail(failureMessage);
    }
}
