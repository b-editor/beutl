namespace Beutl.Services;

// https://github.com/dotnet/core-setup/blob/dev/release/2.0.0/src/managed/Microsoft.DotNet.PlatformAbstractions/Native/PlatformApis.cs
public static class LinuxDistro
{
    static LinuxDistro()
    {
        (Id, VersionId) = LoadDistroInfo();
    }

    public static string Id { get; }

    public static string VersionId { get; }

    private static (string Id, string VersionId) LoadDistroInfo()
    {
        string? id = null;
        string? versionId = null;

        // Sample os-release file:
        //   NAME="Ubuntu"
        //   VERSION = "14.04.3 LTS, Trusty Tahr"
        //   ID = ubuntu
        //   ID_LIKE = debian
        //   PRETTY_NAME = "Ubuntu 14.04.3 LTS"
        //   VERSION_ID = "14.04"
        //   HOME_URL = "http://www.ubuntu.com/"
        //   SUPPORT_URL = "http://help.ubuntu.com/"
        //   BUG_REPORT_URL = "http://bugs.launchpad.net/ubuntu/"
        // We use ID and VERSION_ID

        if (File.Exists("/etc/os-release"))
        {
            var lines = File.ReadAllLines("/etc/os-release");
            foreach (var line in lines)
            {
                if (line.StartsWith("ID=", StringComparison.Ordinal))
                {
                    id = line.Substring(3).Trim('"', '\'');
                }
                else if (line.StartsWith("VERSION_ID=", StringComparison.Ordinal))
                {
                    versionId = line.Substring(11).Trim('"', '\'');
                }
            }
        }
        else if (File.Exists("/etc/redhat-release"))
        {
            var lines = File.ReadAllLines("/etc/redhat-release");

            if (lines.Length >= 1)
            {
                string line = lines[0];
                if (line.StartsWith("Red Hat Enterprise Linux Server release 6.") ||
                    line.StartsWith("CentOS release 6."))
                {
                    id = "rhel";
                    versionId = "6";
                }
            }
        }

        if (id != null && versionId != null)
        {
            NormalizeDistroInfo(id, ref versionId);
        }

        return (id ?? "Linux", versionId ?? "Unknown");
    }

    private static void NormalizeDistroInfo(string id, ref string? versionId)
    {
        // Handle if VersionId is null by just setting the index to -1.
        int minorVersionNumberSeparatorIndex = versionId?.IndexOf('.') ?? -1;

        if (id == "rhel" && minorVersionNumberSeparatorIndex != -1)
        {
            versionId = versionId!.Substring(0, minorVersionNumberSeparatorIndex);
        }
    }
}
