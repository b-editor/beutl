namespace Beutl.Helpers;

public static class DotNetProcess
{
    public static void Configure(ProcessStartInfo processStartInfo, string path)
    {
        if (path.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
        {
            path = path.Substring(0, path.Length - 4);
        }

        if (path.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
        {
            if (!OperatingSystem.IsWindows())
            {
                path = path.Substring(0, path.Length - 4);
            }
        }
        else if (OperatingSystem.IsWindows())
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
