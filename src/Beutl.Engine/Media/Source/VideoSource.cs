using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization;
using Beutl.Engine;
using Beutl.Graphics.Rendering;
using Beutl.Media.Decoding;

namespace Beutl.Media.Source;

[JsonConverter(typeof(VideoSourceJsonConverter))]
[SuppressResourceClassGeneration]
public sealed class VideoSource : MediaSource
{
    private WeakReference<Counter<MediaReader>>? _mediaReaderRef;

    public VideoSource()
    {
    }

    public override void ReadFrom(Uri uri)
    {
        if (!uri.IsFile) throw new NotSupportedException("Only file URIs are supported.");

        Uri = uri;
    }

    public override Resource ToResource(RenderContext context)
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

        public MediaReader? MediaReader => _counter?.Value;

        public bool Read(TimeSpan frame, [NotNullWhen(true)] out IBitmap? bitmap)
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

        public bool Read(int frame, [NotNullWhen(true)] out IBitmap? bitmap)
        {
            if (IsDisposed || _counter == null)
            {
                bitmap = null;
                return false;
            }

            return _counter.Value.ReadVideo(frame, out bitmap);
        }

        public override void Update(EngineObject obj, RenderContext context, ref bool updateOnly)
        {
            base.Update(obj, context, ref updateOnly);
            var videoSource = (VideoSource)obj;

            // Load media reader if URI changed
            if (_loadedUri != videoSource.Uri && videoSource.HasUri)
            {
                _counter?.Release();
                _counter = null;
                var localRef = Volatile.Read(ref videoSource._mediaReaderRef);
                if (localRef?.TryGetTarget(out var counter) == true && counter.RefCount > 0)
                {
                    _counter = counter;
                    counter.AddRef();
                }
                else
                {
                    var reader = MediaReader.Open(videoSource.Uri.LocalPath, new(MediaMode.Video));
                    _counter = new Counter<MediaReader>(reader, null);
                    Volatile.Write(ref videoSource._mediaReaderRef, new(_counter));
                }

                Duration = TimeSpan.FromSeconds(_counter.Value.VideoInfo.Duration.ToDouble());
                FrameRate = _counter.Value.VideoInfo.FrameRate;
                FrameSize = _counter.Value.VideoInfo.FrameSize;
                _loadedUri = videoSource.Uri;

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
