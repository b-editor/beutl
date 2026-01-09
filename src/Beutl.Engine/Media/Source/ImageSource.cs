using System.Text.Json.Serialization;
using Beutl.Engine;
using Beutl.Graphics.Rendering;
using Beutl.Media.Pixel;
using Beutl.Serialization;

namespace Beutl.Media.Source;

[JsonConverter(typeof(ImageSourceJsonConverter))]
[SuppressResourceClassGeneration]
public sealed class ImageSource : MediaSource
{
    private WeakReference<Counter<IBitmap>>? _bitmapRef;

    public ImageSource()
    {
    }

    public override void ReadFrom(Uri uri)
    {
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
        private Counter<IBitmap>? _counter;
        private Uri? _loadedUri;

        public PixelSize FrameSize { get; private set; }

        public IBitmap? Bitmap => _counter?.Value;

        public override void Update(EngineObject obj, RenderContext context, ref bool updateOnly)
        {
            base.Update(obj, context, ref updateOnly);
            var imageSource = (ImageSource)obj;

            // Load bitmap if URI changed
            if (_loadedUri != imageSource.Uri && imageSource.HasUri)
            {
                _counter?.Release();
                _counter = null;
                if (imageSource._bitmapRef?.TryGetTarget(out var counter) == true && counter.RefCount > 0)
                {
                    _counter = counter;
                    counter.AddRef();
                }
                else
                {
                    using var stream = UriHelper.ResolveStream(imageSource.Uri);
                    var bitmap = Bitmap<Bgra8888>.FromStream(stream);
                    _counter = new Counter<IBitmap>(bitmap, null);
                    imageSource._bitmapRef = new(_counter);
                }

                FrameSize = new PixelSize(_counter.Value.Width, _counter.Value.Height);
                _loadedUri = imageSource.Uri;

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
