using System.Text.Json.Serialization;
using Beutl.Composition;
using Beutl.Engine;
using Beutl.Serialization;
using SkiaSharp;

namespace Beutl.Media.Source;

[JsonConverter(typeof(ImageSourceJsonConverter))]
[SuppressResourceClassGeneration]
public sealed class ImageSource : MediaSource
{
    private WeakReference<Counter<LoadedImage>>? _imageRef;

    public override void ReadFrom(Uri uri)
    {
        if (HasUri && Uri != uri)
        {
            // 古い URI の Counter を別 Resource が保持していると
            // TryAddRef が成功して新 URI でも古いビットマップを返してしまうため、
            // URI が切り替わったタイミングで共有参照を破棄する。
            Volatile.Write(ref _imageRef, null);
        }
        Uri = uri;
    }

    public override Resource ToResource(CompositionContext context)
    {
        var resource = new Resource();
        bool versionBumped = true;
        resource.Reconcile(this, context, ref versionBumped);
        return resource;
    }

    internal override void InvalidateResourceCache()
    {
        Volatile.Write(ref _imageRef, null);
        base.InvalidateResourceCache();
    }

    public new sealed class Resource : MediaSource.Resource
    {
        private Counter<LoadedImage>? _counter;
        private Uri? _loadedUri;

        public PixelSize FrameSize { get; private set; }

        public Bitmap? Bitmap => _counter?.Value.Bitmap;

        public bool IsOffline { get; private set; }

        // The drawing of an SVG; null for an image that is only pixels.
        internal SKPicture? Picture => _counter?.Value.Picture;

        public override void Reconcile(EngineObject obj, CompositionContext context, ref bool versionBumped)
        {
            base.Reconcile(obj, context, ref versionBumped);
            var imageSource = (ImageSource)obj;

            // A relink can restore the file at its original URI.
            if (imageSource.HasUri && (_loadedUri != imageSource.Uri || ReloadRequested))
            {
                _counter?.Release();
                _counter = null;
                IsOffline = false;

                Counter<LoadedImage>? shared = null;
                if (!context.DisableResourceShare)
                {
                    var localRef = Volatile.Read(ref imageSource._imageRef);
                    if (localRef?.TryGetTarget(out var counter) == true && counter.TryAddRef())
                        shared = counter;
                }

                if (shared is not null)
                {
                    _counter = shared;
                }
                else
                {
                    try
                    {
                        _counter = new Counter<LoadedImage>(LoadedImage.Open(imageSource.Uri), null);
                        // DisableResourceShare 時は WeakReference を書き換えない。
                        // 他 Renderer（プレビュー側）の共有カウンタを
                        // エンコード専用カウンタで汚染してしまうため。
                        if (!context.DisableResourceShare)
                        {
                            Volatile.Write(ref imageSource._imageRef, new(_counter));
                        }
                    }
                    catch
                    {
                        _counter = new Counter<LoadedImage>(new LoadedImage(OfflineMediaPlaceholder.CreateBitmap()), null);
                        IsOffline = true;
                        FrameSize = OfflineMediaPlaceholder.Size;
                        _loadedUri = imageSource.Uri;
                        Version++;
                        return;
                    }
                }

                FrameSize = _counter.Value.Size;
                _loadedUri = imageSource.Uri;

                if (!versionBumped)
                {
                    Version++;
                    versionBumped = true;
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
