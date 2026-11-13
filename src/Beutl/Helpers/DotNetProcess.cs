namespace Beutl.Helpers;

// Also compiled into Beutl.PackageTools.UI, which starts Beutl when the user asks it to.
internal static class DotNetProcess
{
    // Starts the program named name in this application's directory, through the shell.
    public static ProcessStartInfo CreateBesideApplication(string name)
    {
        var startInfo = new ProcessStartInfo { UseShellExecute = true };
        Configure(startInfo, Path.Combine(AppContext.BaseDirectory, name));
        return startInfo;
    }

    public static void Configure(ProcessStartInfo processStartInfo, string path)
    {
        if (OperatingSystem.IsWindows())
        {
            path += ".exe";
        }

        // ここではpathはプラットフォームに応じた実行ファイル
        bool useDotNet = false;

        if (!File.Exists(path))
        {
            useDotNet = true;
            if (path.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            {
                path = Path.ChangeExtension(path, "dll");
            }
            else
            {
                path += ".dll";
            }
        }

        string dotnetHost = Environment.GetEnvironmentVariable("DOTNET_HOST_PATH")
            ?? (OperatingSystem.IsWindows() ? "dotnet.exe" : "dotnet");

        if (useDotNet)
        {
            processStartInfo.FileName = dotnetHost;
            processStartInfo.ArgumentList.Insert(0, path);
        }
        else
        {
            processStartInfo.FileName = path;
        }
    }
}
