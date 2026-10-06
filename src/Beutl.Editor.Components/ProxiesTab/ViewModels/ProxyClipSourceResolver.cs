using Beutl.Media.Proxy;
using Beutl.Media.Source;

namespace Beutl.Editor.Components.ProxiesTab.ViewModels;

// Maps a project video source to the fingerprint its row is keyed on: the file's own when it can be read,
// otherwise the store entry preview decoding would bind to, so an offline clip still surfaces a row.
internal static class ProxyClipSourceResolver
{
    // Matches ProxyResolver.DensityTolerance so the tab's offline ranking clamps identically.
    private const float DensityTolerance = 1e-6f;

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

        string normalizedPath = NormalizeSourcePath(path);
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

    // Pick the offline row's entry the same way preview decoding will. Among path-matching Ready
    // entries, mirror ProxyResolver.SelectBest: prefer the densest whose supply density is within the
    // preferred-preset cap, else the densest overall — so the row binds to the fingerprint playback
    // actually decodes. Only when no Ready proxy exists does it fall back to stale/failed metadata
    // (ranked by state) so the offline clip still surfaces a row.
    private static ProxyEntry? SelectOfflineEntry(IEnumerable<ProxyEntry> pathMatches, ProxyPreset preferredPreset)
    {
        List<ProxyEntry> candidates = [.. pathMatches];
        if (candidates.Count == 0)
            return null;

        // Choose the newest source version across ALL candidates (any state), then confine the whole
        // selection to that source — mirroring ProxyResolver.ResolveByPath. A newer Failed/Stale entry for
        // a replaced source must outrank an older Ready proxy of a stale fingerprint, so the row reflects
        // the current source's state instead of binding delete/regenerate to content preview won't decode.
        // Precompute newest generation per source once (a linear group) rather than re-scanning inside
        // the sort comparer, which would be O(n²) on a path with many accumulated versions/presets.
        Dictionary<ProxyFingerprint, DateTime> newestBySource = candidates
            .GroupBy(e => e.Source)
            .ToDictionary(g => g.Key, g => g.Max(e => e.GeneratedAtUtc));
        ProxyFingerprint newest = candidates
            .OrderByDescending(e => newestBySource[e.Source])
            .ThenByDescending(e => e.Source.MtimeUtc)
            .First().Source;
        List<ProxyEntry> fromNewest = [.. candidates.Where(e => e.Source == newest)];

        float cap = ProxyPresetDefinitions.Get(preferredPreset).Scale;
        ProxyEntry? cappedWinner = null;
        float cappedDensity = -1f;
        ProxyEntry? densestWinner = null;
        float densestDensity = -1f;
        foreach (ProxyEntry entry in fromNewest)
        {
            if (entry.State != ProxyState.Ready)
                continue;

            float density = SupplyDensityOf(entry);
            if (density <= cap + DensityTolerance && density > cappedDensity)
            {
                cappedWinner = entry;
                cappedDensity = density;
            }

            if (density > densestDensity)
            {
                densestWinner = entry;
                densestDensity = density;
            }
        }

        return cappedWinner ?? densestWinner ?? fromNewest
            .OrderBy(entry => OfflineEntryRank(entry.State))
            .ThenByDescending(entry => (long)entry.ProxyDecodedFrameSize.Width * entry.ProxyDecodedFrameSize.Height)
            .ThenByDescending(entry => entry.GeneratedAtUtc)
            .FirstOrDefault();
    }

    // Mirrors ProxyResolution.SupplyDensity (long-edge ratio) so the tab ranks Ready entries exactly as
    // the resolver does; ProxyEntry already stores both frame sizes.
    private static float SupplyDensityOf(ProxyEntry entry)
    {
        int originalLongEdge = Math.Max(entry.OriginalLogicalFrameSize.Width, entry.OriginalLogicalFrameSize.Height);
        int proxyLongEdge = Math.Max(entry.ProxyDecodedFrameSize.Width, entry.ProxyDecodedFrameSize.Height);
        return originalLongEdge == 0 || proxyLongEdge == 0
            ? 1f
            : (float)proxyLongEdge / originalLongEdge;
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

    private static string NormalizeSourcePath(string path)
    {
        // Resolve a symlink to the target path the store entry was keyed on (FromFile resolves the
        // link at registration). A moved/deleted target leaves the link resolvable via
        // returnFinalTarget:false, so a broken symlink still matches its stored entry instead of
        // dropping the clip from the tab. Case folding must match ProxyFingerprint.NormalizeAbsolutePath.
        string fullPath = ResolveLinkTarget(Path.GetFullPath(path));
        return OperatingSystem.IsWindows() || OperatingSystem.IsMacOS()
            ? fullPath.ToUpperInvariant()
            : fullPath;
    }

    private static string ResolveLinkTarget(string fullPath)
    {
        try
        {
            return new FileInfo(fullPath).ResolveLinkTarget(returnFinalTarget: false)?.FullName ?? fullPath;
        }
        catch
        {
            return fullPath;
        }
    }
}
