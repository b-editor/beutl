using System.Collections.Specialized;
using System.ComponentModel;
using System.Reactive.Subjects;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using Beutl.Engine;
using Beutl.Graphics.Rendering;
using Beutl.Media;
using Beutl.Reactive;
using Beutl.Threading;
using FFmpeg.AutoGen;
using Reactive.Bindings;
using Reactive.Bindings.Extensions;
using SkiaSharp;
using Dispatcher = Avalonia.Threading.Dispatcher;
using ImageBrush = Avalonia.Media.ImageBrush;
using PixelPoint = Avalonia.PixelPoint;
using PixelRect = Avalonia.PixelRect;
using PixelSize = Avalonia.PixelSize;
using Point = Avalonia.Point;
using TileBrush = Beutl.Media.TileBrush;

namespace Beutl;

public static class AvaloniaTypeConverter
{
    public static Vector ToAvaVector(this in Graphics.Vector vector)
    {
        return new Vector(vector.X, vector.Y);
    }

    public static Graphics.Vector ToBtlVector(this in Vector vector)
    {
        return new Graphics.Vector((float)vector.X, (float)vector.Y);
    }

    public static Media.Color ToBtlColor(this FluentAvalonia.UI.Media.Color2 c)
    {
        return new Media.Color(c.A, c.R, c.G, c.B);
    }

    public static Media.GradientStop ToBtlGradientStop(this Avalonia.Media.IGradientStop obj)
    {
        return new Media.GradientStop(obj.Color.ToMedia(), (float)obj.Offset);
    }

    public static GradientStop.Resource ToBtlImmutableGradientStop(this Avalonia.Media.IGradientStop obj)
    {
        return new GradientStop.Resource { Color = obj.Color.ToMedia(), Offset = (float)obj.Offset, };
    }

    public static IObservable<T> SubscribeEngineProperty<T>(
        this IProperty<T> property, EngineObject obj, IObservable<TimeSpan> time)
    {
        return Observable.FromEventPattern(
                h => obj.Edited += h,
                h => obj.Edited -= h)
            .Select(_ => Unit.Default)
            .Publish(Unit.Default).RefCount()
            .CombineLatest(time)
            .Select(t => property.GetValue(t.Second));
    }

    public static IObservable<TResource> SubscribeEngineResource<T, TResource>(
        this T obj, IObservable<TimeSpan> time, Func<T, RenderContext, TResource> createResource)
        where T : EngineObject
        where TResource : EngineObject.Resource
    {
        var renderContext = new RenderContext(TimeSpan.Zero);
        TResource? resource = null;
        return Observable.FromEventPattern(
                h => obj.Edited += h,
                h => obj.Edited -= h)
            .Select(_ => Unit.Default)
            .Publish(Unit.Default).RefCount()
            .CombineLatest(time)
            .Select(t =>
            {
                renderContext.Time = t.Second;
                if (resource == null)
                {
                    resource = createResource(obj, renderContext);
                }
                else
                {
                    bool updateOnly = false;
                    resource.Update(obj, renderContext, ref updateOnly);
                }

                return (resource, resource.Version);
            })
            .DistinctUntilChanged(t => t.Version)
            .Select(t => t.resource);
    }

    public static IObservable<(TResource Resource, int Version)> SubscribeEngineVersionedResource<T, TResource>(
        this T obj, IObservable<TimeSpan> time, Func<T, RenderContext, TResource> createResource)
        where T : EngineObject
        where TResource : EngineObject.Resource
    {
        var renderContext = new RenderContext(TimeSpan.Zero);
        TResource? resource = null;
        return Observable.FromEventPattern(
                h => obj.Edited += h,
                h => obj.Edited -= h)
            .Select(_ => Unit.Default)
            .Publish(Unit.Default).RefCount()
            .CombineLatest(time)
            .Select(t =>
            {
                renderContext.Time = t.Second;
                if (resource == null)
                {
                    resource = createResource(obj, renderContext);
                }
                else
                {
                    bool updateOnly = false;
                    resource.Update(obj, renderContext, ref updateOnly);
                }

                return (resource, resource.Version);
            })
            .DistinctUntilChanged(t => t.Version);
    }

    private static IDisposable AdaptEngineObject<T, TResource>(T obj, IObservable<TimeSpan> time,
        Func<T, RenderContext, TResource> createResource, Action<TResource> onUpdated)
        where T : EngineObject
        where TResource : EngineObject.Resource
    {
        return obj.SubscribeEngineVersionedResource(time, createResource)
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
                s.Color = r.Color.ToAvalonia();
                s.Offset = r.Offset;
            });

        return (s, d);
    }

    public static (IObservable<Avalonia.Media.Geometry>, IDisposable) ToAvaGeometrySync(
        this Geometry obj, IObservable<TimeSpan> time)
    {
        var reactiveProperty = new ReactivePropertySlim<Avalonia.Media.Geometry>();
        var d = AdaptEngineObject(
            obj, time,
            (o, rc) => o.ToResource(rc),
            r =>
            {
                if (reactiveProperty.Value != null!)
                    return;

                string svgPath = r.GetCachedPath().ToSvgPathData();
                reactiveProperty.Value = Avalonia.Media.Geometry.Parse(svgPath);
            });

        return (reactiveProperty, d);
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
                if (reactiveProperty.Value != null!)
                    return;

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

    public static Graphics.Matrix ToBtlMatrix(this in Matrix matrix)
    {
        return new Graphics.Matrix(
            (float)matrix.M11, (float)matrix.M12, (float)matrix.M13,
            (float)matrix.M21, (float)matrix.M22, (float)matrix.M23,
            (float)matrix.M31, (float)matrix.M32, (float)matrix.M33);
    }

    public static Point ToAvaPoint(this in Graphics.Point point)
    {
        return new Point(point.X, point.Y);
    }

    public static Graphics.Point ToBtlPoint(this in Point point)
    {
        return new Graphics.Point((float)point.X, (float)point.Y);
    }

    public static PixelPoint ToAvaPixelPoint(this in Media.PixelPoint point)
    {
        return new PixelPoint(point.X, point.Y);
    }

    public static Media.PixelPoint ToBtlPixelPoint(this in PixelPoint point)
    {
        return new Media.PixelPoint(point.X, point.Y);
    }

    public static Size ToAvaSize(this in Graphics.Size size)
    {
        return new Size(size.Width, size.Height);
    }

    public static Graphics.Size ToBtlSize(this in Size size)
    {
        return new Graphics.Size((float)size.Width, (float)size.Height);
    }

    public static PixelSize ToAvaPixelSize(this in Media.PixelSize size)
    {
        return new PixelSize(size.Width, size.Height);
    }

    public static Media.PixelSize ToBtlPixelSize(this in PixelSize size)
    {
        return new Media.PixelSize(size.Width, size.Height);
    }

    public static Rect ToAvaRect(this in Graphics.Rect rect)
    {
        return new Rect(rect.X, rect.Y, rect.Width, rect.Height);
    }

    public static Graphics.Rect ToBtlRect(this in Rect rect)
    {
        return new Graphics.Rect((float)rect.X, (float)rect.Y, (float)rect.Width, (float)rect.Height);
    }

    public static PixelRect ToAvaPixelRect(this in Media.PixelRect rect)
    {
        return new PixelRect(rect.X, rect.Y, rect.Width, rect.Height);
    }

    public static Media.PixelRect ToBtlPixelRect(this in PixelRect rect)
    {
        return new Media.PixelRect(rect.X, rect.Y, rect.Width, rect.Height);
    }

    public static RelativePoint ToAvaRelativePoint(this in Graphics.RelativePoint pt)
    {
        return new RelativePoint(
            pt.Point.X,
            pt.Point.Y,
            pt.Unit == Graphics.RelativeUnit.Relative
                ? RelativeUnit.Relative
                : RelativeUnit.Absolute);
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
                        int newIndex = e.NewStartingIndex;
                        if (newIndex > e.OldStartingIndex)
                        {
                            newIndex += e.OldItems!.Count;
                        }

                        stops.MoveRange(e.OldStartingIndex, e.NewItems!.Count, newIndex);
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
                        r => ss.Color = r.Color.ToAvalonia());
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
                            handler ??= new DrawableImageBrushHandler(r, imageBrush);
                            handler.Update();
                        });

                    return (imageBrush, d, null);
                }
                break;
        }

        return default;
    }

    // SolidColorBrush.Color, GradientBrush.GradientStopsのみ
    public static Media.Brush? ToBtlBrush(this Avalonia.Media.Brush? brush)
    {
        switch (brush)
        {
            case Avalonia.Media.ISolidColorBrush s:
                return new Media.SolidColorBrush { Color = { CurrentValue = s.Color.ToMedia() } };
            case Avalonia.Media.IGradientBrush g:
                {
                    var stops = g.GradientStops.Select(v => v.ToBtlGradientStop()).ToArray();

                    switch (g)
                    {
                        case Avalonia.Media.ILinearGradientBrush:
                            var lb = new LinearGradientBrush();
                            lb.GradientStops.Replace(stops);
                            return lb;

                        case Avalonia.Media.IConicGradientBrush:
                            var cb = new ConicGradientBrush();
                            cb.GradientStops.Replace(stops);
                            return cb;

                        case Avalonia.Media.IRadialGradientBrush:
                            var rb = new RadialGradientBrush();
                            rb.GradientStops.Replace(stops);
                            return rb;
                    }
                }
                break;
        }

        return null;
    }

    public static Vector SwapAxis(this Vector vector)
    {
        return new Vector(vector.Y, vector.X);
    }

    public sealed class DrawableImageBrushHandler
    {
        private WriteableBitmap? _bitmap;
        private CancellationTokenSource? _cts;
        private readonly ImageBrush _imageBrush;
        private readonly DrawableBrush.Resource _drawableBrush;

        public DrawableImageBrushHandler(DrawableBrush.Resource drawableBrush, ImageBrush imageBrush)
        {
            _imageBrush = imageBrush;
            _drawableBrush = drawableBrush;
        }

        public void Update()
        {
            _cts?.Cancel();
            _cts = new CancellationTokenSource();
            var token = _cts.Token;

            RenderThread.Dispatcher.Dispatch(async () =>
            {
                if (_drawableBrush.Drawable == null) return;
                var node = new DrawableRenderNode(_drawableBrush.Drawable);
                // TODO: UI側の物理的なサイズをもとに描画するように変更する
                using (var context = new GraphicsContext2D(node, new Media.PixelSize(1920, 1080)))
                {
                    _drawableBrush.Drawable.GetOriginal()!.Render(context, _drawableBrush.Drawable);
                }

                var processor = new RenderNodeProcessor(node, false);
                using var bitmap = processor.RasterizeAndConcat();

                var previous = _bitmap;
                var pixelSize = new PixelSize(bitmap.Width, bitmap.Height);
                _bitmap = new WriteableBitmap(pixelSize, new Vector(96, 96), PixelFormat.Bgra8888,
                    AlphaFormat.Unpremul);

                using (var locked = _bitmap.Lock())
                {
                    unsafe
                    {
                        Buffer.MemoryCopy((void*)bitmap.Data, (void*)locked.Address, bitmap.ByteCount,
                            bitmap.ByteCount);
                    }
                }

                await Dispatcher.UIThread.InvokeAsync(() =>
                {
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
            }, DispatchPriority.Low, token);
        }
    }
}
