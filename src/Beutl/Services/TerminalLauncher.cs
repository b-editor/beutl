namespace Beutl.Services;

// Opens a terminal window that runs a command, for the Linux update handoff: apt and the update
// script ask the user for input. Desktops ship different terminals, so none is assumed.
internal static class TerminalLauncher
{
    // Terminals that may be installed, each with the arguments that precede the command to run.
    private static readonly (string Name, string[] Prefix)[] s_knownTerminals =
    [
        ("gnome-terminal", ["--"]),
        ("ptyxis", ["--"]),
        ("konsole", ["-e"]),
        ("xfce4-terminal", ["-x"]),
        ("mate-terminal", ["-x"]),
        ("terminator", ["-x"]),
        ("alacritty", ["-e"]),
        ("kitty", []),
        ("foot", []),
        ("xterm", ["-e"]),
    ];

    internal static ProcessStartInfo? CreateStartInfo(IReadOnlyList<string> command)
    {
        string? path = Environment.GetEnvironmentVariable("PATH");
        return CreateStartInfo(
            command,
            name => FindExecutable(name, path),
            Environment.GetEnvironmentVariable("TERMINAL"));
    }

    // Returns null when no terminal is found.
    internal static ProcessStartInfo? CreateStartInfo(
        IReadOnlyList<string> command,
        Func<string, string?> findExecutable,
        string? preferredTerminal)
    {
        // $TERMINAL is the user's choice. A terminal not listed above gets the xterm convention, -e.
        if (!string.IsNullOrWhiteSpace(preferredTerminal) && findExecutable(preferredTerminal) is { } preferred)
        {
            string name = Path.GetFileName(preferred);
            int known = Array.FindIndex(s_knownTerminals, t => t.Name == name);
            return Create(preferred, known >= 0 ? s_knownTerminals[known].Prefix : ["-e"], command);
        }

        // The freedesktop launcher for the default terminal, then Debian's alternative for it.
        if (findExecutable("xdg-terminal-exec") is { } xdgTerminalExec)
            return Create(xdgTerminalExec, [], command);

        if (findExecutable("x-terminal-emulator") is { } terminalEmulator)
            return Create(terminalEmulator, ["-e"], command);

        foreach ((string name, string[] prefix) in s_knownTerminals)
        {
            if (findExecutable(name) is { } terminal)
                return Create(terminal, prefix, command);
        }

        return null;
    }

    // Looks a command up the way a shell does: a name with a slash is a path, otherwise each PATH entry is tried.
    internal static string? FindExecutable(string name, string? pathVariable)
    {
        if (name.Contains('/'))
            return IsExecutable(name) ? name : null;

        foreach (string directory in (pathVariable ?? "").Split(':', StringSplitOptions.RemoveEmptyEntries))
        {
            string candidate = Path.Combine(directory, name);
            if (IsExecutable(candidate))
                return candidate;
        }

        return null;
    }

    private static bool IsExecutable(string path)
    {
        if (!File.Exists(path))
            return false;

        const UnixFileMode Execute = UnixFileMode.UserExecute | UnixFileMode.GroupExecute | UnixFileMode.OtherExecute;
        return OperatingSystem.IsWindows() || (File.GetUnixFileMode(path) & Execute) != 0;
    }

    private static ProcessStartInfo Create(string terminal, string[] prefix, IReadOnlyList<string> command)
    {
        var startInfo = new ProcessStartInfo(terminal);
        foreach (string argument in prefix)
            startInfo.ArgumentList.Add(argument);

        foreach (string argument in command)
            startInfo.ArgumentList.Add(argument);

        return startInfo;
    }
}
