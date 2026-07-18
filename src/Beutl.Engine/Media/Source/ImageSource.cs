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
        if (HasUri && Uri != uri)
        {
            // 古い URI の Counter を別 Resource が保持していると
            // TryAddRef が成功して新 URI でも古いビットマップを返してしまうため、
            // URI が切り替わったタイミングで共有参照を破棄する。
            Volatile.Write(ref _bitmapRef, null);
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
                // Preserve the acquisition failure while reclaiming any partially initialized bitmap.
            }

            throw;
        }
    }

    public new sealed class Resource : MediaSource.Resource
    {
        private Counter<Bitmap>? _counter;
        private PixelSize _frameSize;
        private Uri? _loadedUri;

        public PixelSize FrameSize => ReadGeneratedResourceState(ref _frameSize);

        public Bitmap? Bitmap => ReadGeneratedResourceState(
            ref _counter,
            static counter => counter?.Value);

        public override void Update(EngineObject obj, CompositionContext context, ref bool updateOnly)
        {
            var imageSource = (ImageSource)obj;
            using GeneratedResourceOperationLease operation = BeginExclusiveResourceOperation(imageSource);
            base.Update(obj, context, ref updateOnly);

            // Load bitmap if URI changed
            if (_loadedUri != imageSource.Uri && imageSource.HasUri)
            {
                Counter<Bitmap>? oldCounter = _counter;
                _counter = null;
                _loadedUri = null;
                _frameSize = default;
                oldCounter?.Release();

                Counter<Bitmap>? shared = null;
                if (!context.DisableResourceShare)
                {
                    var localRef = Volatile.Read(ref imageSource._bitmapRef);
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

                _frameSize = new PixelSize(_counter.Value.Width, _counter.Value.Height);
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
            Counter<Bitmap>? counter = null;
            if (disposing)
            {
                counter = _counter;
                _counter = null;
                _loadedUri = null;
                _frameSize = default;
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
