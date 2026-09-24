using System.Runtime.InteropServices;

namespace Beutl.Editor.Components.FileBrowserTab.Services;

internal static class FileManagerLauncher
{
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
            if (!isDirectory) startInfo.ArgumentList.Add("-R");
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

    private static OSPlatform CurrentPlatform => OperatingSystem.IsMacOS() ? OSPlatform.OSX
        : OperatingSystem.IsWindows() ? OSPlatform.Windows
        : OSPlatform.Linux;
}
