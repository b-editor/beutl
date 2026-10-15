namespace Beutl.Services;

// https://github.com/dotnet/core-setup/blob/dev/release/2.0.0/src/managed/Microsoft.DotNet.PlatformAbstractions/Native/PlatformApis.cs
public static class LinuxDistro
{
    static LinuxDistro()
    {
        Id = LoadDistroInfo();
    }

    public static string Id { get; }

    private static string LoadDistroInfo()
    {
        string? id = null;

        // Reads ID= from /etc/os-release.
        if (File.Exists("/etc/os-release"))
        {
            var lines = File.ReadAllLines("/etc/os-release");
            foreach (var line in lines)
            {
                if (line.StartsWith("ID=", StringComparison.Ordinal))
                {
                    id = line.Substring(3).Trim('"', '\'');
                }
            }
        }

        return id ?? "Linux";
    }
}
