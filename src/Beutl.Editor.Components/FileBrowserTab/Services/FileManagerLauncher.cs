using System.Runtime.InteropServices;

namespace Beutl.Editor.Components.FileBrowserTab.Services;

internal static class FileManagerLauncher
{
    private static readonly TimeSpan s_launchWaitTimeout = TimeSpan.FromSeconds(10);

    internal static string MenuHeader => GetMenuHeader(CurrentPlatform);

    internal static ProcessStartInfo CreateStartInfo(string path, bool isDirectory)
        => CreateStartInfo(path, isDirectory, CurrentPlatform);

    internal static string GetMenuHeader(OSPlatform platform)
    {
        if (platform == OSPlatform.OSX) return Strings.OpenInFinder;
        if (platform == OSPlatform.Windows) return Strings.OpenInExplorer;
        return Strings.OpenInFileManager;
    }

    internal static ProcessStartInfo CreateStartInfo(string path, bool isDirectory, OSPlatform platform)
    {
        if (platform == OSPlatform.OSX)
        {
            var startInfo = new ProcessStartInfo("/usr/bin/open") { UseShellExecute = false };
            if (!isDirectory || path.EndsWith(".app", StringComparison.OrdinalIgnoreCase))
                startInfo.ArgumentList.Add("-R");
            startInfo.ArgumentList.Add(path);
            return startInfo;
        }

        if (platform == OSPlatform.Windows)
        {
            string windowsDirectory = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
            var startInfo = new ProcessStartInfo(Path.Combine(windowsDirectory, "explorer.exe"))
            {
                UseShellExecute = false
            };
            if (isDirectory)
                startInfo.ArgumentList.Add(path);
            else
                startInfo.Arguments = $"/select,\"{path}\"";
            return startInfo;
        }

        var linuxStartInfo = new ProcessStartInfo("xdg-open") { UseShellExecute = false };
        linuxStartInfo.ArgumentList.Add(isDirectory ? path : Path.GetDirectoryName(path)!);
        return linuxStartInfo;
    }

    internal static async Task<bool> LaunchAsync(ProcessStartInfo startInfo)
    {
        using Process process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("The file manager process did not start.");
        using var timeout = new CancellationTokenSource(s_launchWaitTimeout);

        try
        {
            await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (timeout.IsCancellationRequested)
        {
            // xdg-open may remain attached to the opened application for its entire lifetime.
            return !process.HasExited || process.ExitCode == 0;
        }

        return process.ExitCode == 0;
    }

    private static OSPlatform CurrentPlatform => OperatingSystem.IsMacOS() ? OSPlatform.OSX
        : OperatingSystem.IsWindows() ? OSPlatform.Windows
        : OSPlatform.Linux;
}
