using System.Globalization;

namespace Beutl.Editor.Components.VersionControlTab.ViewModels;

internal sealed record VersionControlRemoteProgress(string Text, int? Percentage = null)
{
    public bool IsIndeterminate => Percentage is null;

    public double ProgressBarValue => Percentage.GetValueOrDefault();

    public static VersionControlRemoteProgress FromOutput(string output)
    {
        string text = output.Trim();
        ReadOnlySpan<char> record = text;
        if (record.StartsWith("remote: ", StringComparison.Ordinal))
        {
            record = record[8..].TrimStart();
        }

        int separator = record.IndexOf(':');
        if (separator < 0 || record[..separator] is not (
            "Counting objects" or "Compressing objects" or "Writing objects" or "Receiving objects"
            or "Resolving deltas" or "Unpacking objects" or "Uploading LFS objects" or "Downloading LFS objects"))
        {
            return new(text);
        }

        ReadOnlySpan<char> counter = record[(separator + 1)..].TrimStart();
        int percentSign = counter.IndexOf('%');
        if (percentSign is < 1 or > 3
            || !int.TryParse(counter[..percentSign], NumberStyles.None, CultureInfo.InvariantCulture, out int percentage)
            || percentage > 100)
        {
            return new(text);
        }

        string label = record[..separator].ToString();
        // The LFS percentage counts files. Keep the bytes and speed visible during a single large transfer.
        int endOfFileCount = counter.IndexOf("), ", StringComparison.Ordinal);
        if (endOfFileCount >= 0 && endOfFileCount + 3 < counter.Length
            && char.IsAsciiDigit(counter[endOfFileCount + 3]))
        {
            label += $" · {counter[(endOfFileCount + 3)..]}";
        }

        return new(label, percentage);
    }
}
