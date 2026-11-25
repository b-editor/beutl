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
    public override void ReadFrom(Uri uri)
    {
        if (!uri.IsFile) throw new NotSupportedException("Only file URIs are supported.");
        // A relink can switch back to a file that was replaced since other sources opened it, so read the
        // file switched to afresh instead of joining their readers.
        if (HasUri && Uri != uri) SharedMediaReaders.Shared.Invalidate(uri);
        Uri = uri;
    }

    public override Resource ToResource(CompositionContext context)
        => ToResource(context, VideoDeinterlaceOptions.Default);

    internal Resource ToResource(CompositionContext context, VideoDeinterlaceOptions deinterlace)
    {
        var resource = new Resource();
        bool versionBumped = true;
        resource.Reconcile(this, context, deinterlace, ref versionBumped);
        return resource;
    }

    internal override void InvalidateResourceCache()
    {
        // The file may have changed on disk, so reloading resources must not join readers opened before.
        if (HasUri) SharedMediaReaders.Shared.Invalidate(Uri);
        base.InvalidateResourceCache();
    }

    public new sealed class Resource : MediaSource.Resource
    {
        private MediaReader? _reader;
        private Ref<Bitmap>? _offlineBitmap;
        private Lazy<BitmapColorSpace?>? _legacyProxyIccColorSpace;
        private Uri? _loadedUri;
        private DecoderRegistry? _loadedDecoders;
        private bool _loadedPreferProxy;
        private ProxyPreset _loadedPreferredProxyPreset;
        private VideoDeinterlaceOptions _loadedDeinterlace;
        private long _loadedProxyResolverVersion;
        // Normalized source key (ProxyFingerprint.AbsolutePath) used to observe proxy-version bumps for
        // this source. Kept as the path key, not a live fingerprint, so a missing original still tracks
        // versions and reopens when a proxy is registered for its path.
        private string? _proxyVersionSource;
        private long _observedProxyStoreVersion;

        public MediaTime Duration { get; private set; }

        public Rational FrameRate { get; private set; }

        public PixelSize FrameSize { get; private set; }

        public PixelSize LogicalFrameSize { get; private set; }

        public ProxyResolution? ProxyResolution { get; private set; }

        public VideoColorTags OriginalColorTags => ProxyResolution?.OriginalColorTags ?? _reader?.VideoInfo.ColorTags ?? default;

        internal BitmapColorSpace GetOriginalColorSpace(BitmapColorSpace frameColorSpace)
        {
            if (ProxyResolution is null && _reader?.VideoInfo.HasIccProfile == true)
                return frameColorSpace;

            VideoColorTags tags = OriginalColorTags;
            return ProxyResolution?.OriginalColorSpace ?? _legacyProxyIccColorSpace?.Value
                ?? BitmapColorSpaceMapping.BuildTargetColorSpace(tags.Transfer, tags.Primaries);
        }

        public DeinterlaceApplicationStatus DeinterlaceStatus => _reader?.VideoInfo.DeinterlaceStatus ?? DeinterlaceApplicationStatus.Off;

        public float SupplyDensity => ProxyResolution?.SupplyDensity ?? 1f;

        public MediaReader? MediaReader => _reader;

        public bool IsOffline => _offlineBitmap != null;

        public int GetFrameIndex(MediaTime position)
        {
            // A reader that reports no frame rate has one frame to show.
            if (FrameRate.Numerator <= 0 || FrameRate.Denominator <= 0)
                return 0;

            long frame = position.ToFrame(FrameRate, MidpointRounding.AwayFromZero);
            long frameCount = MediaReader?.VideoInfo.NumFrames ?? 0;
            // Duration metadata can be rounded slightly above a frame boundary without adding another frame.
            if (frameCount <= 0)
                frameCount = Duration.ToFrame(FrameRate, MidpointRounding.AwayFromZero);

            // Keep nearest-frame sampling, but the last frame remains visible for its whole interval.
            // At higher output rates, rounding alone would request EOF half a source frame too early.
            if (frameCount > 0 && frame == frameCount
                && (position < MediaTime.FromFrames(frameCount, FrameRate)
                    || DeinterlaceStatus == DeinterlaceApplicationStatus.Applied && position < Duration))
                frame--;

            return (int)frame;
        }

        public bool Read(int frame, [NotNullWhen(true)] out Ref<Bitmap>? bitmap)
        {
            if (!IsDisposed && _offlineBitmap is { } offline)
            {
                bitmap = offline.Clone();
                return true;
            }
            if (IsDisposed || _reader == null)
            {
                bitmap = null;
                return false;
            }

            return _reader.ReadVideo(frame, out bitmap);
        }

        public override void Reconcile(EngineObject obj, CompositionContext context, ref bool versionBumped)
            => Reconcile((VideoSource)obj, context, VideoDeinterlaceOptions.Default, ref versionBumped);

        internal void Reconcile(VideoSource videoSource, CompositionContext context, VideoDeinterlaceOptions deinterlace, ref bool versionBumped)
        {
            base.Reconcile(videoSource, context, ref versionBumped);
            deinterlace = deinterlace.Normalize();
            IProxyResolver? proxyResolver = context.PreferProxy ? context.Decoders?.ProxyResolver : null;
            RefreshProxyVersionSource(videoSource, proxyResolver);
            // Compare only THIS source's proxy version so a proxy change to another
            // source does not force this reader to reopen.
            long proxyResolverVersion = GetSourceVersion(proxyResolver, _proxyVersionSource);

            // Load media reader if URI or proxy preference changed.
            if (NeedsReload(videoSource, context, proxyResolverVersion, deinterlace))
            {
                var decoders = context.Decoders ?? throw new InvalidOperationException("Opening video requires a decoder registry.");
                _reader?.Dispose();
                _reader = null;
                ProxyResolution = null;
                _legacyProxyIccColorSpace = null;
                _offlineBitmap?.Dispose();
                _offlineBitmap = null;

                // Refresh the per-source key for the current URI, then re-read this source's version so
                // the reload baseline matches the new source. A missing original cannot be fingerprinted,
                // but ResolveComparableKey still yields the path key the store bumps, so an offline reader
                // observes a proxy that is registered for its path after it opened.
                _proxyVersionSource = ResolveProxyVersionSource(videoSource, context);
                proxyResolverVersion = GetSourceVersion(proxyResolver, _proxyVersionSource);

                if (!TryOpenReader(videoSource, context, proxyResolverVersion, decoders, deinterlace))
                {
                    versionBumped = true;
                    return;
                }

                var reader = _reader;
                // Audio-only containers can open successfully without exposing VideoInfo. Reject
                // the reader before reading metadata.
                if (!reader.HasVideo)
                {
                    SetOffline(videoSource, context, proxyResolverVersion, deinterlace);
                    versionBumped = true;
                    return;
                }

                ProxyResolution = reader.ProxyResolution;
                if (ProxyResolution is { OriginalColorSpace: null })
                {
                    string originalPath = videoSource.Uri.LocalPath;
                    _legacyProxyIccColorSpace = new(() => ReadOriginalIccColorSpace(originalPath, decoders, deinterlace));
                }

                Duration = MediaTime.FromSeconds(reader.VideoInfo.Duration.ToDouble());
                FrameRate = reader.VideoInfo.FrameRate;
                FrameSize = reader.VideoInfo.FrameSize;
                LogicalFrameSize = ProxyResolution?.OriginalLogicalFrameSize ?? FrameSize;
                RecordLoadedState(videoSource, context, proxyResolverVersion, deinterlace);

                if (!versionBumped)
                {
                    Version++;
                    versionBumped = true;
                }
            }
        }

        // A symlink that cannot be followed is keyed by its own path, but the store bumps its target's once the target
        // is back. Reading the key touches the file system, so it waits until the store has something new to report.
        private void RefreshProxyVersionSource(VideoSource videoSource, IProxyResolver? proxyResolver)
        {
            if (proxyResolver is null || proxyResolver.StoreVersion == _observedProxyStoreVersion) return;

            _observedProxyStoreVersion = proxyResolver.StoreVersion;
            // A target that cannot be read says nothing about the key, and its proxies stay indexed under the known one.
            if (_proxyVersionSource != null && videoSource.HasUri && videoSource.Uri.IsFile
                && ProxyFingerprint.TryFromFile(videoSource.Uri.LocalPath, out ProxyFingerprint fingerprint))
            {
                _proxyVersionSource = fingerprint.AbsolutePath;
            }
        }

        private static long GetSourceVersion(IProxyResolver? proxyResolver, string? proxyVersionSource)
        {
            return proxyResolver is not null && proxyVersionSource is { } source
                ? proxyResolver.GetSourceVersion(source)
                : 0;
        }

        private bool NeedsReload(VideoSource videoSource, CompositionContext context, long proxyResolverVersion, VideoDeinterlaceOptions deinterlace)
        {
            return videoSource.HasUri && (_loadedUri != videoSource.Uri
                    || _loadedDecoders != context.Decoders
                    || ReloadRequested
                    || _loadedPreferProxy != context.PreferProxy
                    || _loadedPreferredProxyPreset != context.PreferredProxyPreset
                    || _loadedDeinterlace != deinterlace
                    || _loadedProxyResolverVersion != proxyResolverVersion);
        }

        private static string? ResolveProxyVersionSource(VideoSource videoSource, CompositionContext context)
        {
            return context.PreferProxy && videoSource.Uri.IsFile
                ? ProxyFingerprint.TryFromFile(videoSource.Uri.LocalPath, out ProxyFingerprint sourceFingerprint)
                    ? sourceFingerprint.AbsolutePath
                    : ProxyFingerprint.ResolveComparableKey(videoSource.Uri.LocalPath)
                : null;
        }

        // A failed open still records the inputs it was attempted with, so an unchanged source is not
        // reopened on every update.
        [MemberNotNullWhen(true, nameof(_reader))]
        private bool TryOpenReader(VideoSource videoSource, CompositionContext context, long proxyResolverVersion,
            DecoderRegistry decoders, VideoDeinterlaceOptions deinterlace)
        {
            try
            {
                var options = new MediaOptions(MediaMode.Video)
                {
                    PreferProxy = context.PreferProxy,
                    PreferredProxyPreset = context.PreferredProxyPreset,
                    Deinterlace = deinterlace,
                };
                string path = videoSource.Uri.LocalPath;
                // Encoding keeps its own reader; other resources share the file's readers. The key includes this
                // source's proxy version, so an original opened because no proxy resolved is shared only until a
                // proxy is registered. A reader without video never serves others.
                _reader = context.DisableResourceShare
                    ? MediaReader.Open(path, options, decoders)
                    : SharedMediaReaders.Shared.Open(
                        SharedMediaReaders.Key.ForFile(decoders, path, MediaMode.Video, context.PreferProxy,
                            context.PreferProxy ? context.PreferredProxyPreset : default, proxyResolverVersion, deinterlace),
                        () => MediaReader.Open(path, options, decoders),
                        opened => opened.HasVideo);
            }
            catch
            {
                SetOffline(videoSource, context, proxyResolverVersion, deinterlace);
                return false;
            }

            return true;
        }

        // Older proxy indexes only stored color tags. Read the original profile on the first explicit SDR override.
        private static BitmapColorSpace? ReadOriginalIccColorSpace(
            string path, DecoderRegistry decoders, VideoDeinterlaceOptions deinterlace)
        {
            try
            {
                using MediaReader? original = decoders.OpenMediaFile(path, new MediaOptions(MediaMode.Video)
                {
                    Deinterlace = deinterlace,
                    IsBackground = true,
                });
                if (original is { HasVideo: true } && original.VideoInfo.HasIccProfile && original.ReadVideo(0, out var frame))
                {
                    using (frame)
                        return frame.Value.ColorSpace;
                }
            }
            catch
            {
                // The proxy stays usable with its saved tags if the original cannot be read.
            }

            return null;
        }

        private void SetOffline(VideoSource videoSource, CompositionContext context, long proxyResolverVersion, VideoDeinterlaceOptions deinterlace)
        {
            _reader?.Dispose();
            _reader = null;
            _offlineBitmap = Ref<Bitmap>.Create(OfflineMediaPlaceholder.CreateBitmap());
            FrameSize = LogicalFrameSize = OfflineMediaPlaceholder.Size;
            Duration = MediaTime.Zero;
            FrameRate = new Rational(30, 1);
            Version++;
            RecordLoadedState(videoSource, context, proxyResolverVersion, deinterlace);
        }

        private void RecordLoadedState(VideoSource videoSource, CompositionContext context, long proxyResolverVersion, VideoDeinterlaceOptions deinterlace)
        {
            _loadedUri = videoSource.Uri;
            _loadedDecoders = context.Decoders;
            _loadedPreferProxy = context.PreferProxy;
            _loadedPreferredProxyPreset = context.PreferredProxyPreset;
            _loadedDeinterlace = deinterlace;
            _loadedProxyResolverVersion = proxyResolverVersion;
        }

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            _reader?.Dispose();
            _reader = null;
            _legacyProxyIccColorSpace = null;
            _offlineBitmap?.Dispose();
            _offlineBitmap = null;
        }
    }
}
