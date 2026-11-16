namespace Beutl.Services;

internal static class DebianPackageInstaller
{
    private const string Cleanup = """
        cleanup() { rm -f -- "$dir/update.deb"; rmdir -- "$dir"; }
        trap cleanup EXIT
        """;
    private const string Install = "sudo apt update && sudo apt install \"$dir/update.deb\"";

    public static IReadOnlyList<string> Command { get; } = ["bash", "-c", $"dir=$1\n{Cleanup}\n{Install}", "beutl-update"];

    public static string GetManualCommand(string package) => $"""
        bash -c '
          dir=$(mktemp -d) || exit 1
          {Cleanup}
          (ln -- "$1" "$dir/update.deb" || cp -- "$1" "$dir/update.deb") && {Install}
        ' beutl-update '{package.Replace("'", "'\\''")}'
        """;

    public static void Start(string package, ProcessStartInfo terminal, Action<ProcessStartInfo> start)
    {
        // The terminal may start its shell after Beutl exits. Give it an independent name for
        // the verified bytes before starting it. The shell owns that file until apt returns.
        DirectoryInfo directory = Directory.CreateTempSubdirectory("beutl-install-");
        string handoff = Path.Combine(directory.FullName, "update.deb");
        terminal.ArgumentList.Add(directory.FullName);
        try
        {
            try { File.CreateHardLink(handoff, package); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                File.Copy(package, handoff);
            }
            start(terminal);
        }
        catch
        {
            directory.Delete(recursive: true);
            throw;
        }
        finally
        {
            terminal.ArgumentList.RemoveAt(terminal.ArgumentList.Count - 1);
        }
    }
}
