using System.Diagnostics;
using System.Management;
using System.Runtime.Versioning;
using System.Text.RegularExpressions;

namespace Beutl.Configuration;

internal static partial class SystemMemoryCapacity
{
    internal const ulong FallbackBytes = 4L * 1024 * 1024 * 1024;

    // memory_pressure answers at once; the bound only keeps a hung child from stalling startup, where
    // EditorConfig's static initializer asks for the capacity.
    private static readonly TimeSpan s_macQueryTimeout = TimeSpan.FromSeconds(5);

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
        return QueryMacMemoryCapacity(new ProcessStartInfo("/usr/bin/memory_pressure", "-Q"), s_macQueryTimeout);
    }

    internal static ulong QueryMacMemoryCapacity(ProcessStartInfo startInfo, TimeSpan timeout)
    {
        startInfo.RedirectStandardOutput = true;
        using Process? proc = Process.Start(startInfo);
        if (proc == null)
            return FallbackBytes;

        // Drain stdout before waiting for the exit: a child blocked on a full pipe never exits.
        Task<string> output = proc.StandardOutput.ReadToEndAsync();
        if (!output.Wait(timeout) || !proc.WaitForExit(timeout))
        {
            try
            {
                proc.Kill(entireProcessTree: true);
            }
            catch
            {
                // Already exited or not ours to kill; the fallback stands either way.
            }

            return FallbackBytes;
        }

        using var reader = new StringReader(output.Result);
        string? str = reader.ReadLine();
        Regex regex = NumberRegex();
        if (str != null && regex.Match(str) is { Success: true } match)
        {
            return ulong.Parse(match.Value);
        }

        return FallbackBytes;
    }

    [GeneratedRegex(@"(\d+)")]
    private static partial Regex NumberRegex();
}
