namespace Beutl.Editor.Components.VersionControlTab.ViewModels;

internal enum VersionControlDiffLineKind
{
    Context,
    Added,
    Removed,
    Header,
}

internal sealed record VersionControlDiffLineViewModel(
    string Text,
    VersionControlDiffLineKind Kind)
{
    public bool IsAdded => Kind == VersionControlDiffLineKind.Added;

    public bool IsRemoved => Kind == VersionControlDiffLineKind.Removed;

    public bool IsHeader => Kind == VersionControlDiffLineKind.Header;

    public static IReadOnlyList<VersionControlDiffLineViewModel> Parse(
        string diff, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(diff);
        bool inHunk = false;
        var lines = new List<VersionControlDiffLineViewModel>();
        int start = 0;
        while (start <= diff.Length)
        {
            cancellationToken.ThrowIfCancellationRequested();
            int end = diff.IndexOf('\n', start);
            bool last = end < 0;
            if (last)
            {
                end = diff.Length;
            }
            int length = end - start;
            if (!last && length > 0 && diff[end - 1] == '\r')
            {
                length--;
            }
            string line = diff.Substring(start, length);
            if (line.StartsWith("diff ", StringComparison.Ordinal))
            {
                inHunk = false;
            }
            else if (line.StartsWith("@@", StringComparison.Ordinal))
            {
                inHunk = true;
            }

            lines.Add(new VersionControlDiffLineViewModel(line, GetKind(line, inHunk)));
            if (last)
            {
                break;
            }
            start = end + 1;
        }

        return lines;
    }

    private static VersionControlDiffLineKind GetKind(string line, bool inHunk)
    {
        if (line.StartsWith("@@", StringComparison.Ordinal)
            || line.StartsWith("diff ", StringComparison.Ordinal)
            || line.StartsWith("index ", StringComparison.Ordinal))
        {
            return VersionControlDiffLineKind.Header;
        }

        if (!inHunk
            && (line.StartsWith("+++ ", StringComparison.Ordinal)
                || line.StartsWith("--- ", StringComparison.Ordinal)))
        {
            return VersionControlDiffLineKind.Header;
        }

        if (line.StartsWith("+", StringComparison.Ordinal))
        {
            return VersionControlDiffLineKind.Added;
        }

        if (line.StartsWith("-", StringComparison.Ordinal))
        {
            return VersionControlDiffLineKind.Removed;
        }

        return VersionControlDiffLineKind.Context;
    }
}
