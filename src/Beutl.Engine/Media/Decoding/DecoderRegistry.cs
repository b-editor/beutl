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

    // Returns null, with the pin released, when no proxy resolves or opens; a proxy-side fault is logged
    // and also returns null.
    private MediaReader? TryOpenProxy(string file, MediaOptions options, IProxyResolver resolver)
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
