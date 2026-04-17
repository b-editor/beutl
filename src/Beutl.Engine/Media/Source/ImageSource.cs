using System.Text.Json.Serialization;
using Beutl.Composition;
using Beutl.Engine;
using Beutl.Serialization;

namespace Beutl.Media.Source;

[JsonConverter(typeof(ImageSourceJsonConverter))]
[SuppressResourceClassGeneration]
public sealed class ImageSource : MediaSource
{
    private WeakReference<Counter<Bitmap>>? _bitmapRef;

    public ImageSource()
    {
    }

    public override void ReadFrom(Uri uri)
    {
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
        private Counter<Bitmap>? _counter;
        private Uri? _loadedUri;

        public PixelSize FrameSize { get; private set; }

        public Bitmap? Bitmap => _counter?.Value;

        public override void Update(EngineObject obj, CompositionContext context, ref bool updateOnly)
        {
            base.Update(obj, context, ref updateOnly);
            var imageSource = (ImageSource)obj;

            // Load bitmap if URI changed
            if (_loadedUri != imageSource.Uri && imageSource.HasUri)
            {
                _counter?.Release();
                _counter = null;

                Counter<Bitmap>? shared = null;
                if (!context.DisableResourceShare)
                {
                    var localRef = Volatile.Read(ref imageSource._bitmapRef);
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
                        using var stream = UriHelper.ResolveStream(imageSource.Uri);
                        var bitmap = Media.Bitmap.FromStream(stream);
                        _counter = new Counter<Bitmap>(bitmap, null);
                        // DisableResourceShare 時は WeakReference を書き換えない。
                        // 他 Renderer（プレビュー側）の共有カウンタを
                        // エンコード専用カウンタで汚染してしまうため。
                        if (!context.DisableResourceShare)
                        {
                            Volatile.Write(ref imageSource._bitmapRef, new(_counter));
                        }
                    }
                    catch
                    {
                        _counter = null;
                        _loadedUri = imageSource.Uri;
                        return;
                    }
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
