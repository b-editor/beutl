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
    private ProxyPreset _sharedReaderProxyPreset;

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
        bool updateOnly = true;
        resource.Update(this, context, ref updateOnly);
        return resource;
    }

    public new sealed class Resource : MediaSource.Resource
    {
        private Counter<MediaReader>? _counter;
        private Uri? _loadedUri;
        private bool _loadedPreferProxy;
        private ProxyPreset _loadedPreferredProxyPreset;
        private long _loadedProxyResolverVersion;
        private ProxyFingerprint? _proxyVersionSource;

        public TimeSpan Duration { get; private set; }

        public Rational FrameRate { get; private set; }

        public PixelSize FrameSize { get; private set; }

        public PixelSize LogicalFrameSize { get; private set; }

        public ProxyResolution? ProxyResolution { get; private set; }

        public float SupplyDensity => ProxyResolution?.SupplyDensity ?? 1f;

        public MediaReader? MediaReader => _counter?.Value;

        public bool Read(TimeSpan frame, [NotNullWhen(true)] out Ref<Bitmap>? bitmap)
        {
            if (IsDisposed || _counter == null)
            {
                bitmap = null;
                return false;
            }

            double frameRate = FrameRate.ToDouble();
            double frameNum = frame.TotalSeconds * frameRate;
            return _counter.Value.ReadVideo((int)frameNum, out bitmap);
        }

        public bool Read(int frame, [NotNullWhen(true)] out Ref<Bitmap>? bitmap)
        {
            if (IsDisposed || _counter == null)
            {
                bitmap = null;
                return false;
            }

            return _counter.Value.ReadVideo(frame, out bitmap);
        }

        public override void Update(EngineObject obj, CompositionContext context, ref bool updateOnly)
        {
            base.Update(obj, context, ref updateOnly);
            var videoSource = (VideoSource)obj;
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
                _counter?.Release();
                _counter = null;
                ProxyResolution = null;

                // Refresh the per-source fingerprint for the current URI, then re-read
                // this source's version so the reload baseline matches the new source.
                _proxyVersionSource = context.PreferProxy
                    && ProxyFingerprint.TryFromFile(videoSource.Uri.LocalPath, out ProxyFingerprint sourceFingerprint)
                    ? sourceFingerprint
                    : null;
                proxyResolverVersion = proxyResolver is not null && _proxyVersionSource is { } refreshedSource
                    ? proxyResolver.GetSourceVersion(refreshedSource)
                    : 0;

                Counter<MediaReader>? shared = null;
                bool canReuseShared = !context.DisableResourceShare
                    && (!context.PreferProxy
                        || (Volatile.Read(ref videoSource._sharedReaderProxyVersion) == proxyResolverVersion
                            && videoSource._sharedReaderProxyPreset == context.PreferredProxyPreset));
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
                            // Record the version/preset this shared reader was opened with, before
                            // publishing it, so a later fresh Resource can validate reuse against it.
                            videoSource._sharedReaderProxyPreset = context.PreferredProxyPreset;
                            Volatile.Write(ref videoSource._sharedReaderProxyVersion, proxyResolverVersion);
                            Volatile.Write(ref videoSource._mediaReaderRef, new(_counter));
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

                ProxyResolution = _counter.Value.ProxyResolution;
                Duration = TimeSpan.FromSeconds(_counter.Value.VideoInfo.Duration.ToDouble());
                FrameRate = _counter.Value.VideoInfo.FrameRate;
                FrameSize = _counter.Value.VideoInfo.FrameSize;
                LogicalFrameSize = ProxyResolution?.OriginalLogicalFrameSize ?? FrameSize;
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
            base.Dispose(disposing);
            _counter?.Release();
            _counter = null;
        }
    }
}
