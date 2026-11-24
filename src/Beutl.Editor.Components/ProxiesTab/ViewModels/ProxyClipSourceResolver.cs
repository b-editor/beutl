using Beutl.Media.Decoding;
using Beutl.Media.Proxy;
using Beutl.Media.Source;

namespace Beutl.Editor.Components.ProxiesTab.ViewModels;

// An offline clip still has a source identity when the store knows its path. Row lookup then checks
// which variants of that source can be used with the clip's settings.
internal static class ProxyClipSourceResolver
{
    internal static bool TryGetVideoSource(
        VideoSource? source,
        VideoDeinterlaceOptions deinterlace,
        IReadOnlyList<ProxyEntry> storeEntries,
        HashSet<(string Path, VideoDeinterlaceOptions Deinterlace)> seenPaths,
        out (string Path, ProxyFingerprint Fingerprint) item)
    {
        item = default;
        if (source is not { HasUri: true } || source.Uri is not { IsFile: true } uri)
            return false;

        string path = uri.LocalPath;
        if (!ProxyFingerprint.TryFromFile(path, out ProxyFingerprint fingerprint))
        {
            string normalizedPath = ProxyFingerprint.ResolveComparableKey(path);
            var matches = storeEntries.Where(entry => entry.Source.AbsolutePath == normalizedPath).ToArray();
            if (matches.Length == 0) return false;
            fingerprint = ProxyResolver.NewestSource(matches);
        }

        if (!seenPaths.Add((fingerprint.AbsolutePath, deinterlace)))
            return false;

        item = (path, fingerprint);
        return true;
    }
}
