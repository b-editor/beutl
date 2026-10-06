using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Beutl.Controls;
using Beutl.Graphics.Rendering;
using Beutl.Logging;
using Beutl.Media;
using Beutl.Threading;
using Microsoft.Extensions.Logging;
using Dispatcher = Avalonia.Threading.Dispatcher;
using ImageBrush = Avalonia.Media.ImageBrush;

namespace Beutl;

public static partial class AvaloniaTypeConverter
{
    public sealed class DrawableImageBrushHandler : IDisposable
    {
        private static readonly ILogger s_thumbnailLogger = Log.CreateLogger<DrawableImageBrushHandler>();

        private readonly ImageBrush _imageBrush;
        private readonly DrawableBrush.Resource _drawableBrush;
        private readonly Beutl.Threading.Dispatcher _renderDispatcher;
        private readonly bool _ownsResource;
        private readonly EventHandler _shutdownHandler;
        private readonly DispatcherCleanup _release;
        private readonly object _gate = new();
        private WriteableBitmap? _bitmap;
        private CancellationTokenSource? _cts;
        private int _queuedUpdates;
        private int _runningUpdates;
        private bool _disposeRequested;
        private bool _resourceReleased;

        public DrawableImageBrushHandler(DrawableBrush.Resource drawableBrush, ImageBrush imageBrush)
            : this(drawableBrush, imageBrush, RenderThread.Dispatcher)
        {
        }

        public DrawableImageBrushHandler(
            DrawableBrush.Resource drawableBrush,
            ImageBrush imageBrush,
            Beutl.Threading.Dispatcher renderDispatcher)
            : this(drawableBrush, imageBrush, renderDispatcher, ownsResource: true)
        {
        }

        /// <summary>Creates a handler with an explicit resource owner.</summary>
        /// <param name="ownsResource">
        /// <see langword="false"/> when the caller's subscription already owns <paramref name="drawableBrush"/>
        /// and disposes it; a second owner would dispose the same resource twice.
        /// </param>
        internal DrawableImageBrushHandler(
            DrawableBrush.Resource drawableBrush,
            ImageBrush imageBrush,
            Beutl.Threading.Dispatcher renderDispatcher,
            bool ownsResource)
        {
            _imageBrush = imageBrush;
            _drawableBrush = drawableBrush;
            _renderDispatcher = renderDispatcher;
            _ownsResource = ownsResource;
            // A shutdown drops queued work without running it, so the settle decision has to be retaken
            // from here: work this handler is still waiting on will now never run.
            _shutdownHandler = (_, _) => ReleaseResourceIfSettled();
            _renderDispatcher.ShutdownStarted += _shutdownHandler;
            _release = new DispatcherCleanup(_renderDispatcher, ReleaseResource);
        }

        public void Dispose()
        {
            lock (_gate)
            {
                if (_disposeRequested)
                    return;

                _disposeRequested = true;
                _cts?.Cancel();
            }

            try
            {
                ClearPublishedBitmap();
            }
            finally
            {
                // _disposeRequested is already latched, so every later Dispose returns at the guard above:
                // an exception escaping the clear leaves this the last chance to release the resource.
                ReleaseResourceIfSettled();
            }
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
                _queuedUpdates++;
            }

            _renderDispatcher.Dispatch(async () =>
            {
                lock (_gate)
                {
                    _queuedUpdates--;
                    _runningUpdates++;
                }

                try
                {
                    await RenderAndPublishAsync(token);
                }
                finally
                {
                    lock (_gate)
                    {
                        _runningUpdates--;
                    }

                    ReleaseResourceIfSettled();
                }
            }, DispatchPriority.Low);
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
                try
                {
                    _imageBrush.Source = null;
                }
                finally
                {
                    // Avalonia stores a property value before it raises the change, so the brush has already
                    // let go of the bitmap even when a subscriber throws out of this assignment.
                    published?.Dispose();
                }
            }

            if (Dispatcher.UIThread.CheckAccess())
                Clear();
            else
                Dispatcher.UIThread.Post(Clear, DispatcherPriority.Background);
        }

        private void ReleaseResourceIfSettled()
        {
            lock (_gate)
            {
                if (_resourceReleased || !_disposeRequested)
                    return;
                // An in-flight update still reads the resource and must settle before release.
                // RenderThread.Dispatcher stops only during process exit; a reusable dispatcher would need
                // ShutdownFinished cleanup for an update stranded on the UI hop.
                if (_runningUpdates > 0)
                    return;
                // The ShutdownStarted event is one-shot, so a dispatcher that stopped before this handler
                // subscribed never delivers it. Its own state is the signal that queued work is dead.
                if (!_renderDispatcher.HasShutdownStarted && _queuedUpdates > 0)
                    return;

                _resourceReleased = true;
                _cts?.Dispose();
                _cts = null;
            }

            if (!_ownsResource)
            {
                _renderDispatcher.ShutdownStarted -= _shutdownHandler;
                _release.Abandon();
                return;
            }

            _release.Request();
        }

        private void ReleaseResource()
        {
            _renderDispatcher.ShutdownStarted -= _shutdownHandler;
            _drawableBrush.Dispose();
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

            Avalonia.Media.Stretch previousStretch = _imageBrush.Stretch;

            void Rollback()
            {
                bool disposed;
                lock (_gate)
                {
                    disposed = _disposeRequested;
                    _bitmap = disposed ? null : previous;
                }

                // Restoring either property can raise the same listener that rejected the
                // publication; a failure there must not leave the new bitmap owned by nobody.
                Restore(() => _imageBrush.Source = disposed ? null : previous);
                Restore(() => _imageBrush.Stretch = previousStretch);
                if (disposed)
                {
                    // Dispose ran during the notification and already cleared and released the
                    // publication, so restoring it would reinstate a thumbnail nobody owns.
                    previous?.Dispose();
                    return;
                }

                published.Dispose();
            }

            static void Restore(Action restore)
            {
                try
                {
                    restore();
                }
                catch (Exception ex)
                {
                    s_thumbnailLogger.LogWarning(ex, "Failed to roll back a thumbnail publication.");
                }
            }

            try
            {
                _imageBrush.Stretch = ToAvaStretch(stretch);

                // Assigning Stretch can notify listeners that start a superseding update or a
                // disposal, so the decision to commit has to be re-taken after it.
                bool superseded;
                lock (_gate)
                {
                    superseded = token.IsCancellationRequested
                                 || _disposeRequested
                                 || !ReferenceEquals(_bitmap, published);
                }

                if (superseded)
                {
                    Rollback();
                    return;
                }

                _imageBrush.Source = published;

                // A listener can put the previous source back synchronously; that is a rejection.
                if (!ReferenceEquals(_imageBrush.Source, published))
                {
                    Rollback();
                    return;
                }
            }
            catch (Exception ex)
            {
                s_thumbnailLogger.LogWarning(ex, "A thumbnail publication callback threw.");
                Rollback();
                return;
            }

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
