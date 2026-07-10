using Beutl.Configuration;
using Beutl.Logging;
using Beutl.Media.Proxy;

using Microsoft.Extensions.Logging;

namespace Beutl.Media.Decoding;

public static class DecoderRegistry
{
    private static readonly ILogger s_logger = Log.CreateLogger("DecoderRegistry");
    private static readonly List<IDecoderInfo> s_registered = [];
    private static readonly List<IDecoderInfo> s_ordered = [];
    private static readonly object s_lock = new();

    private static IProxyResolver? s_proxyResolver;

    // Written from the UI thread (ProxyMediaServices.Initialize / ReinitializeStore) and read
    // unsynchronized on background decode threads. Volatile access gives the store-root swap prompt
    // cross-thread visibility so a decode thread cannot keep reading a register-cached old resolver
    // (notably on ARM64) after the swap.
    public static IProxyResolver? ProxyResolver
    {
        get => Volatile.Read(ref s_proxyResolver);
        set => Volatile.Write(ref s_proxyResolver, value);
    }

    static DecoderRegistry()
    {
        GlobalConfiguration.Instance.ExtensionConfig.DecoderPriority.CollectionChanged += (_, _) => InvalidateCache();
    }

    private static void InvalidateCache()
    {
        lock (s_lock)
        {
            s_ordered.Clear();
        }
    }

    public static IEnumerable<IDecoderInfo> EnumerateDecoder()
    {
        lock (s_lock)
        {
            if (s_ordered.Count == 0)
            {
                ExtensionConfig extensionConfig = GlobalConfiguration.Instance.ExtensionConfig;
                IDecoderInfo[] preferred = extensionConfig.DecoderPriority.Select(v => v.Type)
                    .Where(v => v != null)
                    .Select(t => s_registered.FirstOrDefault(v => v.GetType() == t))
                    .Where(v => v != null)
                    .ToArray()!;

                s_ordered.AddRange(preferred);

                s_ordered.AddRange(s_registered.Except(preferred));
            }

            return s_ordered;
        }
    }

    public static MediaReader? OpenMediaFile(string file, MediaOptions options)
    {
        // Proxy is best-effort and must never break original playback: any proxy-side fault
        // degrades to the original-decode path below. A generated proxy carries silent audio (the
        // FFmpeg generator feeds SilentSampleProvider), so substitute it only for video-only opens;
        // an Audio/AudioVideo request keeps reading the original audio track.
        if (options.PreferProxy && options.StreamsToLoad == MediaMode.Video && ProxyResolver is { } resolver)
        {
            IDisposable? pin = null;
            try
            {
                var sourceUri = ToFileUri(file);
                if (resolver.Resolve(sourceUri, options.PreferredProxyPreset) is { } resolution)
                {
                    pin = resolver.Pin(resolution);
                    var proxyOptions = options with { PreferProxy = false };
                    foreach (IDecoderInfo decoder in GuessDecoder(resolution.AbsoluteProxyFilePath))
                    {
                        if (decoder.Open(resolution.AbsoluteProxyFilePath, proxyOptions) is { } reader)
                        {
                            return new ProxyMediaReader(reader, pin, resolution);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                s_logger.LogWarning(
                    ex,
                    "Proxy resolution or open failed for '{File}'; falling back to the original media.",
                    file);
            }

            pin?.Dispose();
        }

        // The original decode can only succeed if the file is present; when a PreferProxy open reached
        // here because the original was moved/deleted and no proxy resolved, skip it so the caller gets
        // a clean null (FileNotFound) instead of a decoder-specific open failure.
        if (File.Exists(file))
        {
            foreach (IDecoderInfo decoder in GuessDecoder(file))
            {
                if (decoder.Open(file, options) is { } reader)
                {
                    return reader;
                }
            }
        }

        return null;
    }

    // Build an escaped file URI for the resolver: new Uri(rawPath) parses URI-reserved chars in the
    // filename (# fragment, ? query) as delimiters, so LocalPath would drop them and no longer match
    // the path ProxyFingerprint.FromFile stored — the proxy would never resolve for such files.
    internal static Uri ToFileUri(string file)
    {
        return new UriBuilder
        {
            Scheme = Uri.UriSchemeFile,
            Host = string.Empty,
            Path = Path.GetFullPath(file),
        }.Uri;
    }

    public static IDecoderInfo[] GuessDecoder(string file)
    {
        return EnumerateDecoder().Where(i => i.IsSupported(file)).ToArray();
    }

    public static void Register(IDecoderInfo decoder)
    {
        lock (s_lock)
        {
            InvalidateCache();
            s_registered.Add(decoder);
        }
    }

    public static bool Unregister(IDecoderInfo decoder)
    {
        lock (s_lock)
        {
            bool removed = s_registered.Remove(decoder);
            if (removed)
            {
                InvalidateCache();
            }
            return removed;
        }
    }
}
