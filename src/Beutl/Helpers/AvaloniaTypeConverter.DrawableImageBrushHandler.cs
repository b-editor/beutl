using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Beutl.Controls;
using Beutl.Graphics.Rendering;
using Beutl.Media;
using Beutl.Threading;
using Dispatcher = Avalonia.Threading.Dispatcher;
using ImageBrush = Avalonia.Media.ImageBrush;

namespace Beutl;

public static partial class AvaloniaTypeConverter
{
    public sealed class DrawableImageBrushHandler : IDisposable
    {
        private readonly ImageBrush _imageBrush;
        private readonly DrawableBrush.Resource _drawableBrush;
        private readonly Beutl.Threading.Dispatcher _renderDispatcher;
        private readonly object _gate = new();
        private WriteableBitmap? _bitmap;
        private CancellationTokenSource? _cts;
        private bool _disposeRequested;

        // The caller's subscription owns drawableBrush and disposes it.
        public DrawableImageBrushHandler(
            DrawableBrush.Resource drawableBrush,
            ImageBrush imageBrush,
            Beutl.Threading.Dispatcher renderDispatcher)
        {
            _imageBrush = imageBrush;
            _drawableBrush = drawableBrush;
            _renderDispatcher = renderDispatcher;
        }

        public void Dispose()
        {
            lock (_gate)
            {
                if (_disposeRequested)
                    return;

                _disposeRequested = true;
                _cts?.Cancel();
                _cts?.Dispose();
                _cts = null;
            }

            ClearPublishedBitmap();
        }

        public void Update()
        {
            CancellationToken token;
            lock (_gate)
            {
                if (_disposeRequested)
                    return;

                _cts?.Cancel();
                _cts?.Dispose();
                _cts = new CancellationTokenSource();
                token = _cts.Token;
            }

            _renderDispatcher.Dispatch(() => RenderAndPublishAsync(token), DispatchPriority.Low);
        }

        private void ClearPublishedBitmap()
        {
            WriteableBitmap? published;
            lock (_gate)
            {
                published = _bitmap;
                _bitmap = null;
            }

            void Clear()
            {
                _imageBrush.Source = null;
                published?.Dispose();
            }

            if (Dispatcher.UIThread.CheckAccess())
                Clear();
            else
                Dispatcher.UIThread.Post(Clear, DispatcherPriority.Background);
        }

        private async Task RenderAndPublishAsync(CancellationToken token)
        {
            if (token.IsCancellationRequested)
                return;

            if (_drawableBrush.Drawable == null) return;
            {
                // The node owns the recorded graph, and a thumbnail is re-rendered on every property change,
                // so leaving it to the finalizer strands one graph per keystroke in the editor.
                using var node = new DrawableRenderNode(_drawableBrush.Drawable);
                // TODO: UI側の物理的なサイズをもとに描画するように変更する
                using (var context = new GraphicsContext2D(node, new Graphics.Size(1920, 1080)))
                {
                    _drawableBrush.Drawable.RequireOriginal().Render(context, _drawableBrush.Drawable);
                }

                using var renderer = new RenderNodeRenderer(
                    node,
                    new RenderNodeRenderRequest
                    {
                        Intent = RenderIntent.Preview,
                        // A grouped drawable records a full-target layer scope, which cannot be
                        // resolved without a domain; use the canvas the content was recorded against.
                        TargetDomain = new Graphics.Rect(0, 0, 1920, 1080),
                        CacheOptions = Beutl.Graphics.Rendering.Cache.RenderCacheOptions.Disabled,
                    });
                using RenderNodeRasterization rasterization = renderer.Rasterize();
                Media.Bitmap? bitmap = rasterization.Bitmap;
                if (token.IsCancellationRequested || bitmap is null)
                    return;

                WriteableBitmap published = bitmap.ToAvaWriteableBitmap(null);
                Stretch stretch = _drawableBrush.Stretch;

                await Dispatcher.UIThread.InvokeAsync(
                    () => PublishThumbnail(published, stretch, token),
                    DispatcherPriority.Background);
            }
        }

        // Runs on the UI thread, where the brush may be read and its listeners run.
        private void PublishThumbnail(WriteableBitmap published, Stretch stretch, CancellationToken token)
        {
            WriteableBitmap? previous;
            lock (_gate)
            {
                // A superseding update or a disposal must win over work that was already queued here.
                if (token.IsCancellationRequested || _disposeRequested)
                {
                    published.Dispose();
                    return;
                }

                previous = _bitmap;
                _bitmap = published;
            }

            // Only Avalonia's bindings and rendering listen to this brush, and they neither throw nor reenter.
            _imageBrush.Stretch = ToAvaStretch(stretch);
            _imageBrush.Source = published;
            previous?.Dispose();
        }

        private static Avalonia.Media.Stretch ToAvaStretch(Stretch stretch)
        {
            return stretch switch
            {
                Stretch.Fill => Avalonia.Media.Stretch.Fill,
                Stretch.Uniform => Avalonia.Media.Stretch.Uniform,
                Stretch.UniformToFill => Avalonia.Media.Stretch.UniformToFill,
                Stretch.None => Avalonia.Media.Stretch.None,
                _ => Avalonia.Media.Stretch.Fill,
            };
        }
    }
}
