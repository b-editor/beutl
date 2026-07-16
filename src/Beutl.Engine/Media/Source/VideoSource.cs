using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization;
using Beutl.Composition;
using Beutl.Engine;
using Beutl.Media.Decoding;
using Beutl.Media.Proxy;

namespace Beutl.Media.Source;

[JsonConverter(typeof(VideoSourceJsonConverter))]
[SuppressResourceClassGeneration]
public sealed class VideoSource : MediaSource
{
    private WeakReference<Counter<MediaReader>>? _mediaReaderRef;
    // The proxy version/preset the shared reader above was opened with. A fresh Resource compares the
    // current context against THESE (not its own default-valued loaded fields) to decide reuse.
    private long _sharedReaderProxyVersion;
    // int-backed for Volatile.Read/Write: paired with _sharedReaderProxyVersion in the reuse predicate,
    // so it must not be observable stale relative to a fresh version (else a reader opened for a
    // different preset is reused).
    private int _sharedReaderProxyPreset;

    public VideoSource()
    {
    }

    public override void ReadFrom(Uri uri)
    {
        if (!uri.IsFile) throw new NotSupportedException("Only file URIs are supported.");

        if (HasUri && Uri != uri)
        {
            // 古い URI の Counter を別 Resource が保持していると
            // TryAddRef が成功して新 URI でも古い MediaReader を返してしまうため、
            // URI が切り替わったタイミングで共有参照を破棄する。
            Volatile.Write(ref _mediaReaderRef, null);
        }
        Uri = uri;
    }

    public override Resource ToResource(CompositionContext context)
    {
        var resource = new Resource();
        try
        {
            bool updateOnly = true;
            resource.Update(this, context, ref updateOnly);
            return resource;
        }
        catch
        {
            try
            {
                resource.Dispose();
            }
            catch
            {
                // Preserve the acquisition failure while reclaiming any partially initialized reader.
            }

            throw;
        }
    }

    public new sealed class Resource : MediaSource.Resource
    {
        private Counter<MediaReader>? _counter;
        private TimeSpan _duration;
        private Rational _frameRate;
        private PixelSize _frameSize;
        private PixelSize _logicalFrameSize;
        private Uri? _loadedUri;
        private bool _loadedPreferProxy;
        private ProxyPreset _loadedPreferredProxyPreset;
        private long _loadedProxyResolverVersion;
        // Normalized source key (ProxyFingerprint.AbsolutePath) used to observe proxy-version bumps for
        // this source. Kept as the path key, not a live fingerprint, so a missing original still tracks
        // versions and reopens when a proxy is registered for its path.
        private string? _proxyVersionSource;
        private ProxyResolution? _proxyResolution;

        public TimeSpan Duration => ReadGeneratedResourceState(ref _duration);

        public Rational FrameRate => ReadGeneratedResourceState(ref _frameRate);

        public PixelSize FrameSize => ReadGeneratedResourceState(ref _frameSize);

        public PixelSize LogicalFrameSize => ReadGeneratedResourceState(ref _logicalFrameSize);

        public ProxyResolution? ProxyResolution => ReadGeneratedResourceState(ref _proxyResolution);

        public float SupplyDensity => ReadGeneratedResourceState(
            ref _proxyResolution,
            static resolution => resolution?.SupplyDensity ?? 1f);

        public MediaReader? MediaReader => ReadGeneratedResourceState(
            ref _counter,
            static counter => counter?.Value);

        public bool Read(TimeSpan frame, [NotNullWhen(true)] out Ref<Bitmap>? bitmap)
        {
            using GeneratedResourceOperationLease operation = BeginExclusiveResourceOperation();
            Counter<MediaReader>? counter = _counter;
            if (counter == null)
            {
                bitmap = null;
                return false;
            }

            double frameRate = _frameRate.ToDouble();
            double frameNum = frame.TotalSeconds * frameRate;
            return counter.Value.ReadVideo((int)frameNum, out bitmap);
        }

        public bool Read(int frame, [NotNullWhen(true)] out Ref<Bitmap>? bitmap)
        {
            using GeneratedResourceOperationLease operation = BeginExclusiveResourceOperation();
            Counter<MediaReader>? counter = _counter;
            if (counter == null)
            {
                bitmap = null;
                return false;
            }

            return counter.Value.ReadVideo(frame, out bitmap);
        }

        public override void Update(EngineObject obj, CompositionContext context, ref bool updateOnly)
        {
            var videoSource = (VideoSource)obj;
            using GeneratedResourceOperationLease operation = BeginExclusiveResourceOperation(videoSource);
            base.Update(obj, context, ref updateOnly);
            IProxyResolver? proxyResolver = context.PreferProxy ? DecoderRegistry.ProxyResolver : null;
            // Compare only THIS source's proxy version so a proxy change to another
            // source does not force this reader to reopen (FR-023).
            long proxyResolverVersion = proxyResolver is not null && _proxyVersionSource is { } proxyVersionSource
                ? proxyResolver.GetSourceVersion(proxyVersionSource)
                : 0;

            // Load media reader if URI or proxy preference changed.
            if ((_loadedUri != videoSource.Uri
                    || _loadedPreferProxy != context.PreferProxy
                    || _loadedPreferredProxyPreset != context.PreferredProxyPreset
                    || _loadedProxyResolverVersion != proxyResolverVersion)
                && videoSource.HasUri)
            {
                Counter<MediaReader>? oldCounter = _counter;
                _counter = null;
                _proxyResolution = null;
                _duration = default;
                _frameRate = default;
                _frameSize = default;
                _logicalFrameSize = default;
                _loadedUri = null;
                _loadedPreferProxy = false;
                _loadedPreferredProxyPreset = default;
                _loadedProxyResolverVersion = 0;
                _proxyVersionSource = null;
                oldCounter?.Release();

                // Refresh the per-source key for the current URI, then re-read this source's version so
                // the reload baseline matches the new source. A missing original cannot be fingerprinted,
                // but ResolveComparableKey still yields the path key the store bumps, so an offline reader
                // observes a proxy that is registered for its path after it opened.
                _proxyVersionSource = context.PreferProxy && videoSource.Uri.IsFile
                    ? ProxyFingerprint.TryFromFile(videoSource.Uri.LocalPath, out ProxyFingerprint sourceFingerprint)
                        ? sourceFingerprint.AbsolutePath
                        : ProxyFingerprint.ResolveComparableKey(videoSource.Uri.LocalPath)
                    : null;
                proxyResolverVersion = proxyResolver is not null && _proxyVersionSource is { } refreshedSource
                    ? proxyResolver.GetSourceVersion(refreshedSource)
                    : 0;

                Counter<MediaReader>? shared = null;
                bool canReuseShared = !context.DisableResourceShare
                    && (!context.PreferProxy
                        || (Volatile.Read(ref videoSource._sharedReaderProxyVersion) == proxyResolverVersion
                            && (ProxyPreset)Volatile.Read(ref videoSource._sharedReaderProxyPreset) == context.PreferredProxyPreset));
                if (canReuseShared)
                {
                    var localRef = Volatile.Read(ref videoSource._mediaReaderRef);
                    if (localRef?.TryGetTarget(out var counter) == true && counter.TryAddRef())
                    {
                        if (IsCompatibleWithContext(counter.Value, context))
                        {
                            shared = counter;
                        }
                        else
                        {
                            counter.Release();
                        }
                    }
                }

                static bool IsCompatibleWithContext(MediaReader reader, CompositionContext context)
                {
                    return context.PreferProxy
                        ? reader.ProxyResolution != null
                        : reader.ProxyResolution == null;
                }

                if (shared is not null)
                {
                    _counter = shared;
                }
                else
                {
                    try
                    {
                        var options = new MediaOptions(MediaMode.Video)
                        {
                            PreferProxy = context.PreferProxy,
                            PreferredProxyPreset = context.PreferredProxyPreset,
                        };
                        var reader = MediaReader.Open(videoSource.Uri.LocalPath, options);
                        _counter = new Counter<MediaReader>(reader, null);
                        // DisableResourceShare 時、またはプロキシ優先だがプロキシ解決に至らなかった場合は
                        // WeakReference を書き換えない。他 Renderer（プレビュー側）の共有カウンタを
                        // エンコード専用／原本フォールバックのカウンタで汚染してしまうため。
                        if (!context.DisableResourceShare
                            && (!context.PreferProxy || reader.ProxyResolution != null))
                        {
                            // Record the version/preset/ref this shared reader was opened with, before
                            // publishing it, so a later fresh Resource can validate reuse against it. The
                            // version is written LAST as the single "all valid" signal: the reuse guard
                            // reads version first, so a reader observing the fresh version is then
                            // guaranteed to also see the matching preset AND the new ref. Writing the ref
                            // after the version would let a reuser pass the version/preset guard yet still
                            // read the previous preset's ref and decode from the wrong-density proxy.
                            Volatile.Write(ref videoSource._sharedReaderProxyPreset, (int)context.PreferredProxyPreset);
                            Volatile.Write(ref videoSource._mediaReaderRef, new(_counter));
                            Volatile.Write(ref videoSource._sharedReaderProxyVersion, proxyResolverVersion);
                        }
                    }
                    catch
                    {
                        _counter = null;
                        _loadedUri = videoSource.Uri;
                        _loadedPreferProxy = context.PreferProxy;
                        _loadedPreferredProxyPreset = context.PreferredProxyPreset;
                        _loadedProxyResolverVersion = proxyResolverVersion;
                        return;
                    }
                }

                _proxyResolution = _counter.Value.ProxyResolution;

                _duration = TimeSpan.FromSeconds(_counter.Value.VideoInfo.Duration.ToDouble());
                _frameRate = _counter.Value.VideoInfo.FrameRate;
                _frameSize = _counter.Value.VideoInfo.FrameSize;
                _logicalFrameSize = _proxyResolution?.OriginalLogicalFrameSize ?? _frameSize;
                _loadedUri = videoSource.Uri;
                _loadedPreferProxy = context.PreferProxy;
                _loadedPreferredProxyPreset = context.PreferredProxyPreset;
                _loadedProxyResolverVersion = proxyResolverVersion;

                if (!updateOnly)
                {
                    Version++;
                    updateOnly = true;
                }
            }
        }

        protected override void Dispose(bool disposing)
        {
            Counter<MediaReader>? counter = null;
            if (disposing)
            {
                counter = _counter;
                _counter = null;
                _loadedUri = null;
                _proxyResolution = null;
                _duration = default;
                _frameRate = default;
                _frameSize = default;
                _logicalFrameSize = default;
            }

            Exception? failure = null;
            try
            {
                counter?.Release();
            }
            catch (Exception ex)
            {
                failure = ex;
            }

            try
            {
                base.Dispose(disposing);
            }
            catch (Exception ex)
            {
                failure ??= ex;
            }

            ThrowIfCleanupFailed(failure);
        }
    }
}
