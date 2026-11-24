using Beutl.Configuration;
using Beutl.Logging;
using Beutl.Media.Proxy;
using Beutl.Serialization;
using Microsoft.Extensions.Logging;

namespace Beutl.Media.Decoding;

public sealed class DecoderRegistry
{
    private static readonly ILogger s_logger = Log.CreateLogger("DecoderRegistry");
    private readonly List<IDecoderInfo> _registered = [];
    private readonly List<IDecoderInfo> _ordered = [];
    private readonly object _lock = new();
    private readonly ExtensionConfig _configuration;

    internal Lock RegistrationLock { get; } = new();

    private IProxyResolver? _proxyResolver;

    // Written from the UI thread (ProxyMediaServices construction / ReinitializeStore) and read
    // unsynchronized on background decode threads. Volatile access gives the store-root swap prompt
    // cross-thread visibility so a decode thread cannot keep reading a register-cached old resolver
    // (notably on ARM64) after the swap.
    public IProxyResolver? ProxyResolver
    {
        get => Volatile.Read(ref _proxyResolver);
        set => Volatile.Write(ref _proxyResolver, value);
    }

    /// <summary>
    /// Raised after the set or priority of registered decoders changed.
    /// </summary>
    /// <remarks>
    /// Raised outside <c>_lock</c> so a handler may call back into the registry.
    /// </remarks>
    public event EventHandler? DecodersChanged;

    public DecoderRegistry(ExtensionConfig configuration)
    {
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        FileExtensions = new DecoderFileExtensions(this);
        _configuration.DecoderPriority.CollectionChanged += OnPriorityChanged;
    }

    public DecoderFileExtensions FileExtensions { get; }

    private void OnPriorityChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
    {
        lock (_lock)
        {
            _ordered.Clear();
        }
        DecodersChanged?.Invoke(this, EventArgs.Empty);
    }

    public IEnumerable<IDecoderInfo> EnumerateDecoder()
    {
        lock (_lock)
        {
            if (_ordered.Count == 0)
            {
                IDecoderInfo[] preferred = _configuration.DecoderPriority.Select(v => v.Type)
                    .Where(v => v != null)
                    .Select(t => _registered.FirstOrDefault(v => v.GetType() == t))
                    .Where(v => v != null)
                    .ToArray()!;

                _ordered.AddRange(preferred);

                _ordered.AddRange(_registered.Except(preferred));
            }

            // Snapshot: callers enumerate after the lock is released, and a registration or priority change can clear
            // _ordered from another thread.
            return _ordered.ToArray();
        }
    }

    public MediaReader? OpenMediaFile(string file, MediaOptions options)
    {
        // Proxy is best-effort and must never break original playback: any proxy-side fault
        // degrades to the original-decode path below. A generated proxy carries silent audio (the
        // FFmpeg generator feeds SilentSampleProvider), so substitute it only for video-only opens;
        // an Audio/AudioVideo request keeps reading the original audio track.
        if (options.PreferProxy && options.StreamsToLoad == MediaMode.Video && ProxyResolver is { } resolver
            && TryOpenProxy(file, options, resolver) is { } proxyReader)
        {
            return proxyReader;
        }

        // The original decode can only succeed if the file is present; when a PreferProxy open reached
        // here because the original was moved/deleted and no proxy resolved, skip it so the caller gets
        // a clean null (FileNotFound) instead of a decoder-specific open failure.
        return File.Exists(file) ? OpenOriginal(file, options) : null;
    }

    private MediaReader? OpenOriginal(string file, MediaOptions options)
    {
        var decoders = GuessDecoder(file);
        var deinterlace = options.StreamsToLoad.HasFlag(MediaMode.Video)
            ? options.Deinterlace.Normalize()
            : VideoDeinterlaceOptions.Default;
        if (deinterlace == VideoDeinterlaceOptions.Default)
        {
            if (options.Deinterlace != deinterlace)
                options = options with { Deinterlace = deinterlace };
            foreach (IDecoderInfo decoder in decoders)
            {
                if (decoder.Open(file, options) is { } reader)
                    return reader;
            }
            return null;
        }

        // Auto gives the first working decoder a chance to identify progressive footage. On goes
        // straight to capable decoders, even if a higher-priority native decoder could play the file.
        bool force = deinterlace.Mode == DeinterlaceMode.On;
        var candidates = force
            ? decoders.OrderByDescending(decoder => decoder.Capabilities.Supports(deinterlace))
            : decoders.AsEnumerable();
        MediaReader? fallback = null;
        foreach (IDecoderInfo decoder in candidates)
        {
            bool capable = decoder.Capabilities.Supports(deinterlace);
            if (!capable && fallback is { HasVideo: true })
                continue;
            if (TryOpenDecoder(decoder, file, options) is not { } reader)
                continue;

            if (reader.HasVideo)
            {
                var info = reader.VideoInfo;
                if (capable && (info.DeinterlaceStatus == DeinterlaceApplicationStatus.Applied
                    || (!force && info.DeinterlaceStatus == DeinterlaceApplicationStatus.NotNeededProgressive)))
                {
                    fallback?.Dispose();
                    return reader;
                }

                if (!force && fallback is not { HasVideo: true } && info.DetectedFieldOrder == VideoFieldOrder.Progressive)
                {
                    fallback?.Dispose();
                    return WithFallbackStatus(reader, deinterlace);
                }

                // A capable decoder that did not apply a forced setting failed this attempt.
                if (force && capable)
                {
                    reader.Dispose();
                    continue;
                }
            }

            if (fallback == null || (reader.HasVideo && !fallback.HasVideo))
            {
                fallback?.Dispose();
                fallback = reader;
            }
            else
            {
                reader.Dispose();
            }
        }

        if (fallback != null)
            return WithFallbackStatus(fallback, deinterlace);

        // A filter can fail to initialize for a file that the same decoder can play unfiltered.
        var unfiltered = options with { Deinterlace = VideoDeinterlaceOptions.Default };
        foreach (IDecoderInfo decoder in decoders)
        {
            if (decoder.Capabilities.Supports(deinterlace) && TryOpenDecoder(decoder, file, unfiltered) is { } reader)
                return WithFallbackStatus(reader, deinterlace);
        }

        return null;
    }

    private static MediaReader? TryOpenDecoder(IDecoderInfo decoder, string file, MediaOptions options)
    {
        try
        {
            return decoder.Open(file, options);
        }
        catch (Exception ex)
        {
            s_logger.LogWarning(ex, "{Decoder} failed to open '{File}'.", decoder.Name, file);
            return null;
        }
    }

    private static MediaReader WithFallbackStatus(MediaReader reader, VideoDeinterlaceOptions options)
    {
        if (!reader.HasVideo) return reader;
        var info = reader.VideoInfo;
        var status = options.GetFallbackStatus(info.DetectedFieldOrder);
        return info.DeinterlaceStatus == status
            ? reader
            : new VideoStreamInfoReader(reader, info with { DeinterlaceStatus = status });
    }

    // Returns null, with the pin released, when no proxy resolves or opens; a proxy-side fault is logged
    // and also returns null.
    private MediaReader? TryOpenProxy(string file, MediaOptions options, IProxyResolver resolver)
    {
        IDisposable? pin = null;
        try
        {
            var sourceUri = ToFileUri(file);
            var deinterlace = options.Deinterlace.Normalize();
            if (resolver.Resolve(sourceUri, options.PreferredProxyPreset, deinterlace) is { } resolution
                && resolution.Deinterlace == deinterlace
                && (deinterlace.Mode == DeinterlaceMode.Off
                    || resolution.DeinterlaceStatus == DeinterlaceApplicationStatus.Applied
                    || deinterlace.Mode == DeinterlaceMode.Auto
                    && resolution.DeinterlaceStatus == DeinterlaceApplicationStatus.NotNeededProgressive))
            {
                pin = resolver.Pin(resolution);
                var proxyOptions = options with { PreferProxy = false, Deinterlace = VideoDeinterlaceOptions.Default };
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
        return null;
    }

    // The resolver matches the LocalPath of this URI with the path ProxyFingerprint.FromFile stored.
    internal static Uri ToFileUri(string file) => UriHelper.CreateFromPath(Path.GetFullPath(file));

    public IDecoderInfo[] GuessDecoder(string file)
    {
        return EnumerateDecoder().Where(i => i.IsSupported(file)).ToArray();
    }

    public void Register(IDecoderInfo decoder)
    {
        lock (_lock)
        {
            _ordered.Clear();
            _registered.Add(decoder);
        }

        DecodersChanged?.Invoke(this, EventArgs.Empty);
    }

    public bool Unregister(IDecoderInfo decoder)
    {
        bool removed;
        lock (_lock)
        {
            removed = _registered.Remove(decoder);
            if (removed)
            {
                _ordered.Clear();
            }
        }

        if (removed)
        {
            DecodersChanged?.Invoke(this, EventArgs.Empty);
        }

        return removed;
    }
}
