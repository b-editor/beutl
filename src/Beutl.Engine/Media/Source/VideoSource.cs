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
    public VideoSource()
    {
    }

    public override void ReadFrom(Uri uri)
    {
        if (!uri.IsFile) throw new NotSupportedException("Only file URIs are supported.");

        Uri = uri;
    }

    public override EngineObject.Resource ToResource(RenderContext context)
    {
        var resource = new Resource();
        bool updateOnly = true;
        resource.Update(this, context, ref updateOnly);
        return resource;
    }

    public new sealed class Resource : MediaSource.Resource
    {
        private Ref<MediaReader>? _mediaReader;
        private Uri? _loadedUri;

        public TimeSpan Duration { get; private set; }

        public Rational FrameRate { get; private set; }

        public PixelSize FrameSize { get; private set; }

        public bool Read(TimeSpan frame, [NotNullWhen(true)] out IBitmap? bitmap)
        {
            if (IsDisposed || _mediaReader == null)
            {
                bitmap = null;
                return false;
            }

            double frameRate = FrameRate.ToDouble();
            double frameNum = frame.TotalSeconds * frameRate;
            return _mediaReader.Value.ReadVideo((int)frameNum, out bitmap);
        }

        public bool Read(int frame, [NotNullWhen(true)] out IBitmap? bitmap)
        {
            if (IsDisposed || _mediaReader == null)
            {
                bitmap = null;
                return false;
            }

            return _mediaReader.Value.ReadVideo(frame, out bitmap);
        }

        public override void Update(EngineObject obj, RenderContext context, ref bool updateOnly)
        {
            base.Update(obj, context, ref updateOnly);

            if (obj is not VideoSource videoSource)
                throw new ArgumentException("Expected VideoSource", nameof(obj));

            // Load media reader if URI changed
            if (_loadedUri != videoSource.Uri && videoSource.HasUri)
            {
                _mediaReader?.Dispose();
                var reader = MediaReader.Open(videoSource.Uri.LocalPath, new(MediaMode.Video));
                Duration = TimeSpan.FromSeconds(reader.VideoInfo.Duration.ToDouble());
                FrameRate = reader.VideoInfo.FrameRate;
                FrameSize = reader.VideoInfo.FrameSize;

                _mediaReader = Ref<MediaReader>.Create(reader);
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
            if (disposing)
            {
                _mediaReader?.Dispose();
                _mediaReader = null;
            }
        }
    }
}
