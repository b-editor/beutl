using System.Collections.Specialized;
using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Beutl.Composition;
using Beutl.Controls;
using Beutl.Editor.Components.Helpers;
using Beutl.Engine;
using Beutl.Graphics.Rendering;
using Beutl.Logging;
using Beutl.Media;
using Beutl.Threading;
using Microsoft.Extensions.Logging;
using Reactive.Bindings;
using Reactive.Bindings.Extensions;
using Dispatcher = Avalonia.Threading.Dispatcher;
using ImageBrush = Avalonia.Media.ImageBrush;
using PixelSize = Avalonia.PixelSize;
using Point = Avalonia.Point;

namespace Beutl;

public static class AvaloniaTypeConverter
{
    public static Media.GradientStop ToBtlGradientStop(this Avalonia.Media.IGradientStop obj)
    {
        return new Media.GradientStop(obj.Color.ToBtlColor(), (float)obj.Offset);
    }

    public static GradientStop.Resource ToBtlImmutableGradientStop(this Avalonia.Media.IGradientStop obj)
    {
        return new GradientStop.Resource { Color = obj.Color.ToBtlColor(), Offset = (float)obj.Offset, };
    }

    private static IDisposable AdaptEngineObject<T, TResource>(T obj, IObservable<TimeSpan> time,
        Func<T, CompositionContext, TResource> createResource, Action<TResource> onUpdated)
        where T : EngineObject
        where TResource : EngineObject.Resource
    {
        return obj.SubscribeEngineVersionedResource(time, createResource)
            .ObserveOnUIDispatcher()
            .Subscribe(t => onUpdated(t.Resource));
    }

    public static (Avalonia.Media.GradientStop, IDisposable) ToAvaGradientStopSync(
        this Media.GradientStop obj, IObservable<TimeSpan> time)
    {
        var s = new Avalonia.Media.GradientStop();
        var d = AdaptEngineObject(
            obj, time,
            (o, rc) => o.ToResource(rc),
            r =>
            {
                s.Color = r.Color.ToAvaColor();
                s.Offset = r.Offset;
            });

        return (s, d);
    }

    public static (IObservable<Avalonia.Media.Geometry>, IDisposable) ToAvaGeometrySync(
        this PathFigure obj, IObservable<TimeSpan> time)
    {
        var reactiveProperty = new ReactivePropertySlim<Avalonia.Media.Geometry>();
        var d = AdaptEngineObject(
            obj, time,
            (o, rc) => o.ToResource(rc),
            r =>
            {
                using var context = new GeometryContext();
                r.ApplyTo(context);

                string svgPath = context.NativeObject.ToSvgPathData();
                reactiveProperty.Value = Avalonia.Media.Geometry.Parse(svgPath);
            });

        return (reactiveProperty, d);
    }

    public static Matrix ToAvaMatrix(this in Graphics.Matrix matrix)
    {
        return new Matrix(
            matrix.M11, matrix.M12, matrix.M13,
            matrix.M21, matrix.M22, matrix.M23,
            matrix.M31, matrix.M32, matrix.M33);
    }

    public static Graphics.Point ToBtlPoint(this in Point point)
    {
        return new Graphics.Point((float)point.X, (float)point.Y);
    }

    public static (Avalonia.Media.GradientStops, IDisposable) ToAvaGradientStopsSync(
        this ICoreList<Media.GradientStop> obj,
        IObservable<TimeSpan> time)
    {
        var d = new CompositeDisposable();
        var stops = new Avalonia.Media.GradientStops();
        var subscription = new Dictionary<Media.GradientStop, IDisposable>();

        for (int i = 0; i < obj.Count; i++)
        {
            Media.GradientStop item = obj[i];
            var t = item.ToAvaGradientStopSync(time);
            subscription[item] = t.Item2;
            stops.Insert(i, t.Item1);
        }

        obj.CollectionChangedAsObservable()
            .Subscribe(e =>
            {
                int index;
                switch (e.Action)
                {
                    case NotifyCollectionChangedAction.Add:
                        index = e.NewStartingIndex;
                        foreach (Media.GradientStop? item in e.NewItems!)
                        {
                            var t = item!.ToAvaGradientStopSync(time);
                            subscription[item!] = t.Item2;
                            stops.Insert(index++, t.Item1);
                        }

                        break;

                    case NotifyCollectionChangedAction.Remove:
                        index = e.OldStartingIndex;
                        for (int i = e.OldItems!.Count - 1; i >= 0; --i)
                        {
                            var item = (Media.GradientStop)e.OldItems[i]!;
                            if (subscription.TryGetValue(item, out var disposable))
                            {
                                disposable.Dispose();
                                subscription.Remove(item);
                            }

                            stops.RemoveAt(index + i);
                        }

                        break;

                    case NotifyCollectionChangedAction.Replace:
                        index = e.NewStartingIndex;
                        for (int i = 0; i < e.NewItems!.Count; i++)
                        {
                            var oldItem = (Media.GradientStop)e.OldItems![i]!;
                            var newItem = (Media.GradientStop)e.NewItems![i]!;
                            if (subscription.TryGetValue(oldItem, out var disposable))
                            {
                                disposable.Dispose();
                                subscription.Remove(oldItem);
                            }

                            (Avalonia.Media.GradientStop, IDisposable) t = newItem.ToAvaGradientStopSync(time);

                            stops[index] = t.Item1;
                            index++;
                        }

                        break;
                    case NotifyCollectionChangedAction.Move:
                        if (e.OldStartingIndex >= 0
                            && e.OldStartingIndex < stops.Count
                            && e.NewStartingIndex >= 0
                            && e.NewStartingIndex < stops.Count
                            && e.OldStartingIndex != e.NewStartingIndex
                            && e.OldItems is { Count: 1 })
                        {
                            stops.Move(e.OldStartingIndex, e.NewStartingIndex);
                        }
                        break;

                    case NotifyCollectionChangedAction.Reset:
                        stops.Clear();
                        foreach (var item in subscription.Values)
                        {
                            item.Dispose();
                        }

                        subscription.Clear();
                        break;
                    default:
                        throw new ArgumentOutOfRangeException(nameof(e));
                }
            })
            .DisposeWith(d);
        Disposable.Create(subscription, s =>
        {
            foreach (var item in s.Values)
            {
                item.Dispose();
            }
        }).DisposeWith(d);

        return (stops, d);
    }

    public static (Avalonia.Media.Brush?, IDisposable, Action?) ToAvaBrushSync(this Media.Brush? brush,
        IObservable<TimeSpan> time)
    {
        switch (brush)
        {
            case Media.SolidColorBrush s:
                {
                    var ss = new Avalonia.Media.SolidColorBrush();
                    var d = AdaptEngineObject(
                        s, time,
                        (o, rc) => o.ToResource(rc),
                        r => ss.Color = r.Color.ToAvaColor());
                    return (ss, d, null);
                }

            case Media.GradientBrush g:
                {
                    (Avalonia.Media.GradientStops stops, IDisposable d) = g.GradientStops.ToAvaGradientStopsSync(time);

                    switch (g)
                    {
                        case Media.LinearGradientBrush:
                            return (new Avalonia.Media.LinearGradientBrush { GradientStops = stops, }, d, null);

                        case Media.ConicGradientBrush:
                            return (new Avalonia.Media.ConicGradientBrush { GradientStops = stops, }, d, null);

                        case Media.RadialGradientBrush:
                            return (new Avalonia.Media.RadialGradientBrush { GradientStops = stops, }, d, null);
                    }
                }
                break;

            case Media.DrawableBrush db:
                {
                    var imageBrush = new ImageBrush();
                    DrawableImageBrushHandler? handler = null;
                    var d = AdaptEngineObject(
                        db, time,
                        (o, rc) => o.ToResource(rc),
                        r =>
                        {
                            handler ??= new DrawableImageBrushHandler(
                                r, imageBrush, RenderThread.Dispatcher, ownsResource: false);
                            handler.Update();
                        });

                    return (
                        imageBrush,
                        System.Reactive.Disposables.Disposable.Create(() =>
                        {
                            d.Dispose();
                            handler?.Dispose();
                        }),
                        null);
                }
        }

        return default;
    }

    public sealed class DrawableImageBrushHandler : IDisposable
    {
        private static readonly ILogger s_thumbnailLogger = Log.CreateLogger<DrawableImageBrushHandler>();

        private readonly ImageBrush _imageBrush;
        private readonly DrawableBrush.Resource _drawableBrush;
        private readonly Beutl.Threading.Dispatcher _renderDispatcher;
        private readonly bool _ownsResource;
        private readonly EventHandler _shutdownHandler;
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
            // A shutdown drops queued work without running it, so the resource must be released from here too.
            _shutdownHandler = (_, _) => ReleaseResourceIfSettled();
            _renderDispatcher.ShutdownStarted += _shutdownHandler;
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

            ClearPublishedBitmap();
            ReleaseResourceIfSettled();
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
                _imageBrush.Source = null;
                published?.Dispose();
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
                // An update already in flight is still recording from the resource, so it has to settle
                // first even during shutdown; only work that never starts can be written off.
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

            _renderDispatcher.ShutdownStarted -= _shutdownHandler;
            if (!_ownsResource)
                return;

            // A shutting-down dispatcher never runs queued work, so the release has to happen inline there.
            if (_renderDispatcher.CheckAccess() || _renderDispatcher.HasShutdownStarted)
                _drawableBrush.Dispose();
            else
                _renderDispatcher.Dispatch(_drawableBrush.Dispose, DispatchPriority.Low);
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
                    _drawableBrush.Drawable.GetOriginal()!.Render(context, _drawableBrush.Drawable);
                }

                using var renderer = new RenderNodeRenderer(
                    node,
                    new RenderNodeRendererOptions
                    {
                        DefaultRequest = new RenderNodeRenderRequest
                        {
                            Intent = RenderIntent.Preview,
                            // A grouped drawable records a full-target layer scope, which cannot be
                            // resolved without a domain; use the canvas the content was recorded against.
                            TargetDomain = new Graphics.Rect(0, 0, 1920, 1080),
                            CacheOptions = Beutl.Graphics.Rendering.Cache.RenderCacheOptions.Disabled,
                        },
                    });
                using RenderNodeRasterization rasterization = renderer.Rasterize();
                Media.Bitmap? bitmap = rasterization.Bitmap;
                if (token.IsCancellationRequested || bitmap is null)
                    return;

                WriteableBitmap published = bitmap.ToAvaWriteableBitmap(null);
                Stretch stretch = _drawableBrush.Stretch;

                await Dispatcher.UIThread.InvokeAsync(() =>
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
                        _imageBrush.Stretch = stretch switch
                        {
                            Stretch.Fill => Avalonia.Media.Stretch.Fill,
                            Stretch.Uniform => Avalonia.Media.Stretch.Uniform,
                            Stretch.UniformToFill => Avalonia.Media.Stretch.UniformToFill,
                            Stretch.None => Avalonia.Media.Stretch.None,
                            _ => Avalonia.Media.Stretch.Fill,
                        };

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
                }, DispatcherPriority.Background);
            }
        }
    }
}
