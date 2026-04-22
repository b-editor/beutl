using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization;
using Beutl.Composition;
using Beutl.Engine;
using Beutl.Media.Decoding;
using Beutl.Media.Source.Proxy;

namespace Beutl.Media.Source;

[JsonConverter(typeof(VideoSourceJsonConverter))]
[SuppressResourceClassGeneration]
public sealed class VideoSource : MediaSource
{
    private WeakReference<Counter<MediaReader>>? _mediaReaderRef;
    private WeakReference<Counter<MediaReader>>? _proxyMediaReaderRef;

    public VideoSource()
    {
    }

    public override void ReadFrom(Uri uri)
    {
        if (!uri.IsFile) throw new NotSupportedException("Only file URIs are supported.");

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

        public TimeSpan Duration { get; private set; }

        public Rational FrameRate { get; private set; }

        public PixelSize FrameSize { get; private set; }

        public PixelSize DecodedFrameSize { get; private set; }

        public bool UsingProxy { get; private set; }

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

            // Load media reader if URI changed
            if (_loadedUri != videoSource.Uri && videoSource.HasUri)
            {
                _counter?.Release();
                _counter = null;

                string originalPath = videoSource.Uri.LocalPath;
                bool useProxy = ResolveOpenPath(originalPath, context, out string openPath);

                Counter<MediaReader>? shared = null;
                if (!context.DisableResourceShare)
                {
                    ref var refField = ref (useProxy ? ref videoSource._proxyMediaReaderRef : ref videoSource._mediaReaderRef);
                    var localRef = Volatile.Read(ref refField);
                    if (localRef?.TryGetTarget(out var counter) == true && counter.RefCount > 0)
                        shared = counter;
                }

                if (shared is not null)
                {
                    _counter = shared;
                    shared.AddRef();
                }
                else
                {
                    try
                    {
                        var reader = MediaReader.Open(openPath, new(MediaMode.Video));
                        _counter = new Counter<MediaReader>(reader, null);
                        // DisableResourceShare 時は WeakReference を書き換えない。
                        // 他 Renderer（プレビュー側）の共有カウンタを
                        // エンコード専用カウンタで汚染してしまうため。
                        if (!context.DisableResourceShare)
                        {
                            ref var refField = ref (useProxy ? ref videoSource._proxyMediaReaderRef : ref videoSource._mediaReaderRef);
                            Volatile.Write(ref refField, new(_counter));
                        }
                    }
                    catch
                    {
                        _counter = null;
                        _loadedUri = videoSource.Uri;
                        return;
                    }
                }

                // プロキシで開いた場合でも、タイムライン尺と MeasureCore が返す
                // FrameSize はオリジナル解像度に固定する。実際のデコード結果サイズ
                // は DecodedFrameSize に別で保持し、描画時にオリジナル解像度へ
                // スケール補正するために使う。プロキシは同一尺・同一 fps で
                // 生成される前提。
                Duration = TimeSpan.FromSeconds(_counter.Value.VideoInfo.Duration.ToDouble());
                FrameRate = _counter.Value.VideoInfo.FrameRate;
                DecodedFrameSize = _counter.Value.VideoInfo.FrameSize;
                UsingProxy = useProxy;
                FrameSize = useProxy
                    ? ProxyCacheManager.Instance.TryGetEntry(originalPath)?.OriginalFrameSize ?? _counter.Value.VideoInfo.FrameSize
                    : _counter.Value.VideoInfo.FrameSize;
                _loadedUri = videoSource.Uri;

                if (!updateOnly)
                {
                    Version++;
                    updateOnly = true;
                }
            }
        }

        private static bool ResolveOpenPath(string originalPath, CompositionContext context, out string openPath)
        {
            if (context.UseProxyIfAvailable
                && ProxyCacheManager.Instance.TryGetProxyPath(originalPath, out var proxy))
            {
                openPath = proxy;
                return true;
            }

            openPath = originalPath;
            return false;
        }

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            _counter?.Release();
            _counter = null;
        }
    }
}
