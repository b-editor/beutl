using System.Diagnostics.CodeAnalysis;
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
    public ImageSource()
    {
    }

    public override void ReadFrom(Uri uri)
    {
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
        private Ref<IBitmap>? _bitmap;
        private Uri? _loadedUri;

        public PixelSize FrameSize { get; private set; }

        public bool TryGetRef([NotNullWhen(true)] out Ref<IBitmap>? bitmap)
        {
            if (IsDisposed || _bitmap == null)
            {
                bitmap = null;
                return false;
            }

            bitmap = _bitmap.Clone();
            return true;
        }

        public bool Read([NotNullWhen(true)] out IBitmap? bitmap)
        {
            if (IsDisposed || _bitmap == null)
            {
                bitmap = null;
                return false;
            }

            bitmap = _bitmap.Value.Clone();
            return true;
        }

        public override void Update(EngineObject obj, RenderContext context, ref bool updateOnly)
        {
            base.Update(obj, context, ref updateOnly);

            if (obj is not ImageSource imageSource)
                throw new ArgumentException("Expected ImageSource", nameof(obj));

            // Load bitmap if URI changed
            if (_loadedUri != imageSource.Uri && imageSource.HasUri)
            {
                _bitmap?.Dispose();
                using var stream = UriHelper.ResolveStream(imageSource.Uri);
                var bitmap = Bitmap<Bgra8888>.FromStream(stream);
                FrameSize = new PixelSize(bitmap.Width, bitmap.Height);
                _bitmap = Ref<IBitmap>.Create(bitmap);
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
            if (disposing)
            {
                _bitmap?.Dispose();
                _bitmap = null;
            }
        }
    }
}
