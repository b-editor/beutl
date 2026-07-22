using System.Collections.Specialized;
using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Beutl.Composition;
using Beutl.Controls;
using Beutl.Editor.Components.Helpers;
using Beutl.Engine;
using Beutl.Graphics.Rendering;
using Beutl.Media;
using Beutl.Threading;
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
                var original = r.GetOriginal();
                original.ApplyTo(context, r);

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
                    var disposables = new CompositeDisposable();
                    AdaptEngineObject(
                        db, time,
                        (o, rc) => o.ToResource(rc),
                        r =>
                        {
                            handler ??= new DrawableImageBrushHandler(r, imageBrush);
                            handler.Update();
                        })
                        .DisposeWith(disposables);
                    Disposable.Create(() => handler?.Dispose())
                        .DisposeWith(disposables);

                    return (imageBrush, disposables, null);
                }
        }

        return default;
    }

    public sealed class DrawableImageBrushHandler : IDisposable
    {
        private readonly object _gate = new();
        private WriteableBitmap? _bitmap;
        private CancellationTokenSource? _cts;
        private readonly ImageBrush _imageBrush;
        private readonly DrawableBrush.Resource _drawableBrush;
        private int _activeUpdates;
        private bool _disposed;
        private bool _resourceDisposalScheduled;

        public DrawableImageBrushHandler(DrawableBrush.Resource drawableBrush, ImageBrush imageBrush)
        {
            _imageBrush = imageBrush;
            _drawableBrush = drawableBrush;
        }

        public void Update()
        {
            CancellationTokenSource updateCts;
            lock (_gate)
            {
                if (_disposed)
                    return;

                _cts?.Cancel();
                updateCts = new CancellationTokenSource();
                _cts = updateCts;
                _activeUpdates++;
            }

            RenderThread.Dispatcher.Dispatch(async () =>
            {
                WriteableBitmap? nextBitmap = null;
                try
                {
                    CancellationToken token = updateCts.Token;
                    if (token.IsCancellationRequested || _drawableBrush.Drawable == null)
                        return;

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
                            Intent = RenderIntent.Preview,
                            TargetDomain = new Graphics.Rect(0, 0, 1920, 1080),
                            UseRenderCache = false,
                        });
                    using RenderNodeRasterization rasterization = renderer.Rasterize();
                    Media.Bitmap? bitmap = rasterization.Bitmap;
                    if (token.IsCancellationRequested || bitmap is null)
                        return;

                    nextBitmap = bitmap.ToAvaWriteableBitmap(null);
                    if (token.IsCancellationRequested)
                        return;

                    await Dispatcher.UIThread.InvokeAsync(() =>
                    {
                        if (token.IsCancellationRequested)
                            return;

                        WriteableBitmap? previous;
                        lock (_gate)
                        {
                            if (_disposed || token.IsCancellationRequested)
                                return;

                            previous = _bitmap;
                            _bitmap = nextBitmap;
                            nextBitmap = null;
                        }

                        _imageBrush.Stretch = _drawableBrush.Stretch switch
                        {
                            Stretch.Fill => Avalonia.Media.Stretch.Fill,
                            Stretch.Uniform => Avalonia.Media.Stretch.Uniform,
                            Stretch.UniformToFill => Avalonia.Media.Stretch.UniformToFill,
                            Stretch.None => Avalonia.Media.Stretch.None,
                            _ => Avalonia.Media.Stretch.Fill,
                        };
                        _imageBrush.Source = _bitmap;
                        previous?.Dispose();
                    }, DispatcherPriority.Background);
                }
                finally
                {
                    nextBitmap?.Dispose();
                    CompleteUpdate(updateCts);
                }
            }, DispatchPriority.Low, CancellationToken.None);
        }

        public void Dispose()
        {
            bool disposeResource;
            lock (_gate)
            {
                if (_disposed)
                    return;

                _disposed = true;
                _cts?.Cancel();
                _cts = null;
                disposeResource = ScheduleResourceDisposalIfIdle();
            }

            DisposeUiResources();
            if (disposeResource)
                DispatchResourceDisposal();
        }

        private void CompleteUpdate(CancellationTokenSource updateCts)
        {
            bool disposeResource;
            lock (_gate)
            {
                if (ReferenceEquals(_cts, updateCts))
                    _cts = null;

                _activeUpdates--;
                disposeResource = ScheduleResourceDisposalIfIdle();
            }

            updateCts.Dispose();
            if (disposeResource)
                DispatchResourceDisposal();
        }

        private bool ScheduleResourceDisposalIfIdle()
        {
            if (!_disposed || _activeUpdates != 0 || _resourceDisposalScheduled)
                return false;

            _resourceDisposalScheduled = true;
            return true;
        }

        private void DispatchResourceDisposal()
        {
            RenderThread.Dispatcher.Dispatch(
                _drawableBrush.Dispose,
                DispatchPriority.Low,
                CancellationToken.None);
        }

        private void DisposeUiResources()
        {
            void DisposeCore()
            {
                WriteableBitmap? bitmap;
                lock (_gate)
                {
                    bitmap = _bitmap;
                    _bitmap = null;
                }

                _imageBrush.Source = null;
                bitmap?.Dispose();
            }

            if (Dispatcher.UIThread.CheckAccess())
                DisposeCore();
            else
                Dispatcher.UIThread.Post(DisposeCore, DispatcherPriority.Background);
        }
    }
}
