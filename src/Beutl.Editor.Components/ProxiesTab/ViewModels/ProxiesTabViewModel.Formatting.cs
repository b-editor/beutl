using Beutl.Media;
using Beutl.Media.Proxy;

namespace Beutl.Editor.Components.ProxiesTab.ViewModels;

public sealed partial class ProxiesTabViewModel
{
    internal static string FormatBytes(long bytes)
    {
        string[] units = ["B", "KB", "MB", "GB", "TB"];
        double value = bytes;
        int unit = 0;
        while (value >= 1024 && unit < units.Length - 1)
        {
            value /= 1024;
            unit++;
        }

        return $"{value:0.#} {units[unit]}";
    }

    internal static string FormatSize(PixelSize size)
    {
        return $"{size.Width}x{size.Height}";
    }

    internal static string GetProxyStateText(ProxyState state)
    {
        return state switch
        {
            ProxyState.None => Strings.ProxyMissing,
            ProxyState.Generating => Strings.ProxyGenerating,
            ProxyState.Ready => Strings.ProxyReady,
            ProxyState.Stale => Strings.ProxyStale,
            ProxyState.Failed => Strings.ProxyFailed,
            ProxyState.Partial => Strings.ProxyPartial,
            _ => state.ToString(),
        };
    }

    internal static string GetJobStatusText(ProxyJobStatus status)
    {
        return status switch
        {
            ProxyJobStatus.Queued => Strings.ProxyJobStatusQueued,
            ProxyJobStatus.Running => Strings.ProxyJobStatusRunning,
            ProxyJobStatus.Succeeded => Strings.ProxyJobStatusSucceeded,
            ProxyJobStatus.Failed => Strings.ProxyJobStatusFailed,
            ProxyJobStatus.Canceled => Strings.ProxyJobStatusCanceled,
            ProxyJobStatus.Skipped => Strings.ProxyJobStatusSkipped,
            _ => status.ToString(),
        };
    }

    internal static string GetJobStatusText(ProxyJob job)
    {
        return string.IsNullOrWhiteSpace(job.StatusMessage)
            ? GetJobStatusText(job.Status)
            : string.Format(
                CultureInfo.CurrentCulture,
                Strings.ProxyJobStatusWithMessageFormat,
                GetJobStatusText(job.Status),
                job.StatusMessage);
    }

    internal static string FormatProgress(double fraction)
    {
        return Math.Clamp(fraction, 0, 1).ToString("P0", CultureInfo.CurrentCulture);
    }
}
