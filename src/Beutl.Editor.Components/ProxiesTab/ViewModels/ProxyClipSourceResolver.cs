using Beutl.Media.Proxy;
using Beutl.Media.Source;

namespace Beutl.Editor.Components.ProxiesTab.ViewModels;

// Maps a project video source to the fingerprint its row is keyed on: the file's own when it can be read,
// otherwise the store entry preview decoding would bind to, so an offline clip still surfaces a row.
internal static class ProxyClipSourceResolver
{
    internal static bool TryGetVideoSource(
        VideoSource? source,
        IReadOnlyList<ProxyEntry> storeEntries,
        HashSet<string> seenPaths,
        ProxyPreset preferredPreset,
        out (string Path, ProxyFingerprint Fingerprint) item)
    {
        if (source is not { HasUri: true } || source.Uri is not { IsFile: true } uri)
        {
            item = default;
            return false;
        }

        string path = uri.LocalPath;
        if (ProxyFingerprint.TryFromFile(path, out ProxyFingerprint fingerprint))
        {
            if (!seenPaths.Add(fingerprint.AbsolutePath))
            {
                item = default;
                return false;
            }

            item = (path, fingerprint);
            return true;
        }

        string normalizedPath = ProxyFingerprint.ResolveComparableKey(path);
        ProxyEntry? existing = SelectOfflineEntry(
            storeEntries.Where(entry => entry.Source.AbsolutePath == normalizedPath),
            preferredPreset);
        if (existing == null || !seenPaths.Add(existing.Source.AbsolutePath))
        {
            item = default;
            return false;
        }

        item = (path, existing.Source);
        return true;
    }

    // Picks the entry preview decoding binds to, the way ProxyResolver.ResolveByPath does; stale or failed
    // metadata, ranked by state, only when the newest source has no Ready proxy, so the offline clip keeps a row.
    private static ProxyEntry? SelectOfflineEntry(IEnumerable<ProxyEntry> pathMatches, ProxyPreset preferredPreset)
    {
        List<ProxyEntry> candidates = [.. pathMatches];
        if (candidates.Count == 0)
            return null;

        ProxyFingerprint newest = ProxyResolver.NewestSource(candidates);
        List<ProxyEntry> fromNewest = [.. candidates.Where(e => e.Source == newest)];

        return ProxyResolver.SelectDensest(
                fromNewest.Where(entry => entry.State == ProxyState.Ready),
                preferredPreset,
                entry => ProxyResolution.SupplyDensityOf(entry.OriginalLogicalFrameSize, entry.ProxyDecodedFrameSize))
            ?? fromNewest
                .OrderBy(entry => OfflineEntryRank(entry.State))
                .ThenByDescending(entry => (long)entry.ProxyDecodedFrameSize.Width * entry.ProxyDecodedFrameSize.Height)
                .ThenByDescending(entry => entry.GeneratedAtUtc)
                .FirstOrDefault();
    }

    // Ready first (a usable stand-in), then Stale, then in-progress, then failed/absent last. Mirrors
    // ProxyResolver's offline preference so the tab and the preview agree on which entry wins.
    private static int OfflineEntryRank(ProxyState state) => state switch
    {
        ProxyState.Ready => 0,
        ProxyState.Stale => 1,
        ProxyState.Partial => 2,
        ProxyState.Generating => 3,
        ProxyState.Failed => 4,
        _ => 5,
    };
}
