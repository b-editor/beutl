namespace Beutl.Media.Proxy;

// Rules the store, the resolver and the eviction sweep must agree on: eviction ranks exactly the entries
// GetTotalBytes counts, and the resolver serves only metadata the store accepts as a valid Ready file.
internal static class ProxyEntryChecks
{
    // Entries whose recorded ProxyFileSizeBytes count toward the store total, and so toward the cap.
    public static bool CountsTowardStoreSize(ProxyState state)
    {
        return state is ProxyState.Ready or ProxyState.Stale or ProxyState.Failed;
    }

    public static bool HasPositiveSizes(ProxyEntry entry)
    {
        return entry.ProxyFileSizeBytes > 0
            && entry.OriginalLogicalFrameSize.Width > 0
            && entry.OriginalLogicalFrameSize.Height > 0
            && entry.ProxyDecodedFrameSize.Width > 0
            && entry.ProxyDecodedFrameSize.Height > 0;
    }

    // Null when the file is missing or cannot be inspected.
    public static long? TryGetFileLength(string path)
    {
        try
        {
            var info = new FileInfo(path);
            return info.Exists ? info.Length : null;
        }
        catch
        {
            return null;
        }
    }
}
