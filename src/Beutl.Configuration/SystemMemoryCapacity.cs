using System.Diagnostics;
using System.Management;
using System.Runtime.Versioning;
using System.Text.RegularExpressions;

namespace Beutl.Configuration;

internal static partial class SystemMemoryCapacity
{
    private const ulong FallbackBytes = 4L * 1024 * 1024 * 1024;

    public static ulong GetTotalBytes()
    {
        return OperatingSystem.IsWindows() ? GetWindowsMemoryCapacity()
            : OperatingSystem.IsLinux() ? GetLinuxMemoryCapacity()
            : OperatingSystem.IsMacOS() ? GetMacMemoryCapacity()
            : 1024 * 1024 * 1024;
    }

    [SupportedOSPlatform("windows")]
    private static ulong GetWindowsMemoryCapacity()
    {
        try
        {
            using var mc = new ManagementClass("Win32_OperatingSystem");
            using ManagementObjectCollection moc = mc.GetInstances();

            ulong total = 0;
            foreach (ManagementBaseObject? mo in moc)
            {
                total += Convert.ToUInt64(mo["TotalVisibleMemorySize"]);
            }

            return total * 1024;
        }
        catch
        {
            return FallbackBytes;
        }
    }

    private static ulong GetLinuxMemoryCapacity()
    {
        const string FileName = "/proc/meminfo";
        // このifは多分無駄
        if (File.Exists(FileName))
        {
            foreach (string item in File.ReadLines(FileName))
            {
                if (item.StartsWith("MemTotal:"))
                {
                    string? s = item.Split(' ', StringSplitOptions.RemoveEmptyEntries).ElementAtOrDefault(1);
                    if (ulong.TryParse(s, out ulong v))
                    {
                        // kBで固定されている
                        // https://github.com/torvalds/linux/blob/3a5879d495b226d0404098e3564462d5f1daa33b/fs/proc/meminfo.c#L31
                        return v * 1024;
                    }
                }
            }
        }

        // https://help.ubuntu.com/community/Installation/SystemRequirements
        return FallbackBytes;
    }

    private static ulong GetMacMemoryCapacity()
    {
        var startInfo = new ProcessStartInfo("/usr/bin/memory_pressure", "-Q")
        {
            RedirectStandardOutput = true
        };
        var proc = Process.Start(startInfo);
        if (proc != null)
        {
            proc.WaitForExit();
            string? str = proc.StandardOutput.ReadLine();
            Regex regex = NumberRegex();
            if (str != null && regex.Match(str) is { Success: true } match)
            {
                return ulong.Parse(match.Value);
            }
        }

        return FallbackBytes;
    }

    [GeneratedRegex(@"(\d+)")]
    private static partial Regex NumberRegex();
}
