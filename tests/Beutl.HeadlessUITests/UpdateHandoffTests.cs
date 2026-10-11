using System.Diagnostics;
using Beutl.Services;
using Beutl.ViewModels.Dialogs;

namespace Beutl.HeadlessUITests;

[TestFixture]
public sealed class UpdateHandoffTests
{
    private static readonly string[] s_command = ["bash", "-c", "echo update"];

    [Test]
    public void WindowsZipStaging_IsNextToTheApplicationFolder()
    {
        // The application and the Beutl home directory are on different volumes.
        (string apps, string stagingRoot) = OperatingSystem.IsWindows()
            ? (@"D:\Apps", @"C:\Users\user\.beutl\tmp\update")
            : ("/mnt/apps", "/home/user/.beutl/tmp/update");
        string app = Path.Combine(apps, "Beutl") + Path.DirectorySeparatorChar;

        string staging = UpdateDialogViewModel.GetZipStagingDirectory(app, stagingRoot, isWindows: true);

        // win-update.ps1 moves the staged folder into place, which Windows PowerShell can only do on one volume.
        Assert.Multiple(() =>
        {
            Assert.That(staging, Is.EqualTo(Path.Combine(apps, "Beutl.update")));
            Assert.That(Path.GetPathRoot(staging), Is.EqualTo(Path.GetPathRoot(app)));
        });
    }

    [Test]
    public void UnixZipStaging_StaysInTheHomeDirectory()
    {
        string staging = UpdateDialogViewModel.GetZipStagingDirectory(
            "/mnt/apps/Beutl/", "/home/user/.beutl/tmp/update", isWindows: false);

        Assert.That(staging, Is.EqualTo(Path.Combine("/home/user/.beutl/tmp/update", "Beutl")));
    }

    [Test]
    public void Terminal_IsFoundWithoutGnomeTerminal()
    {
        ProcessStartInfo? start = TerminalLauncher.CreateStartInfo(s_command, Installed("konsole", "xterm"), null);

        Assert.That(start, Is.Not.Null);
        Assert.Multiple(() =>
        {
            Assert.That(start!.FileName, Is.EqualTo("/usr/bin/konsole"));
            Assert.That(start.ArgumentList, Is.EqualTo(new[] { "-e", "bash", "-c", "echo update" }));
        });
    }

    [Test]
    public void Terminal_GnomeTerminalTakesTheCommandAfterDoubleDash()
    {
        ProcessStartInfo? start = TerminalLauncher.CreateStartInfo(s_command, Installed("gnome-terminal"), null);

        Assert.That(start?.FileName, Is.EqualTo("/usr/bin/gnome-terminal"));
        Assert.That(start!.ArgumentList, Is.EqualTo(new[] { "--", "bash", "-c", "echo update" }));
    }

    [Test]
    public void Terminal_DefaultTerminalLaunchersComeBeforeSpecificTerminals()
    {
        ProcessStartInfo? xdg = TerminalLauncher.CreateStartInfo(
            s_command, Installed("gnome-terminal", "x-terminal-emulator", "xdg-terminal-exec"), null);
        ProcessStartInfo? debian = TerminalLauncher.CreateStartInfo(
            s_command, Installed("gnome-terminal", "x-terminal-emulator"), null);

        Assert.Multiple(() =>
        {
            Assert.That(xdg?.FileName, Is.EqualTo("/usr/bin/xdg-terminal-exec"));
            Assert.That(xdg?.ArgumentList, Is.EqualTo(s_command));
            Assert.That(debian?.FileName, Is.EqualTo("/usr/bin/x-terminal-emulator"));
            Assert.That(debian?.ArgumentList, Is.EqualTo(new[] { "-e", "bash", "-c", "echo update" }));
        });
    }

    [Test]
    public void Terminal_TerminalVariableComesFirst()
    {
        ProcessStartInfo? known = TerminalLauncher.CreateStartInfo(
            s_command, Installed("x-terminal-emulator", "xfce4-terminal"), "xfce4-terminal");
        // A terminal that is not listed gets the xterm convention.
        ProcessStartInfo? unknown = TerminalLauncher.CreateStartInfo(
            s_command, Installed("x-terminal-emulator", "st"), "st");

        Assert.Multiple(() =>
        {
            Assert.That(known?.FileName, Is.EqualTo("/usr/bin/xfce4-terminal"));
            Assert.That(known?.ArgumentList, Is.EqualTo(new[] { "-x", "bash", "-c", "echo update" }));
            Assert.That(unknown?.FileName, Is.EqualTo("/usr/bin/st"));
            Assert.That(unknown?.ArgumentList, Is.EqualTo(new[] { "-e", "bash", "-c", "echo update" }));
        });
    }

    [Test]
    public void Terminal_MissingTerminalVariableFallsBackToTheOthers()
    {
        ProcessStartInfo? start = TerminalLauncher.CreateStartInfo(s_command, Installed("xterm"), "kitty");

        Assert.That(start?.FileName, Is.EqualTo("/usr/bin/xterm"));
    }

    [Test]
    public void Terminal_IsNullWhenNoneIsInstalled()
    {
        Assert.That(TerminalLauncher.CreateStartInfo(s_command, Installed(), null), Is.Null);
    }

    [Test]
    public void FindExecutable_SkipsFilesThatAreNotExecutable()
    {
        if (OperatingSystem.IsWindows())
        {
            Assert.Ignore("Uses Unix file modes.");
            return;
        }

        string root = Directory.CreateTempSubdirectory("beutl-terminal-").FullName;
        try
        {
            string first = Directory.CreateDirectory(Path.Combine(root, "first")).FullName;
            string second = Directory.CreateDirectory(Path.Combine(root, "second")).FullName;
            File.WriteAllText(Path.Combine(first, "xterm"), "");
            string executable = Path.Combine(second, "xterm");
            File.WriteAllText(executable, "");
            File.SetUnixFileMode(executable, UnixFileMode.UserRead | UnixFileMode.UserExecute);
            string path = string.Join(':', Path.Combine(root, "missing"), first, second);

            Assert.Multiple(() =>
            {
                Assert.That(TerminalLauncher.FindExecutable("xterm", path), Is.EqualTo(executable));
                Assert.That(TerminalLauncher.FindExecutable("konsole", path), Is.Null);
                Assert.That(TerminalLauncher.FindExecutable(executable, null), Is.EqualTo(executable));
                Assert.That(TerminalLauncher.FindExecutable("xterm", null), Is.Null);
            });
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    // A PATH lookup that finds only the given commands, in /usr/bin.
    private static Func<string, string?> Installed(params string[] commands)
    {
        return name => commands.Contains(name) ? "/usr/bin/" + name : null;
    }
}
