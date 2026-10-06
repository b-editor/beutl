using System.Collections.Specialized;
using Avalonia;
using Beutl.Composition;
using Beutl.Controls;
using Beutl.Editor.Components.Helpers;
using Beutl.Engine;
using Beutl.Graphics.Rendering;
using Beutl.Media;
using Reactive.Bindings;
using Reactive.Bindings.Extensions;
using ImageBrush = Avalonia.Media.ImageBrush;
using PixelSize = Avalonia.PixelSize;
using Point = Avalonia.Point;

namespace Beutl;

public static partial class AvaloniaTypeConverter
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
            // The notification is marshalled to the UI thread, but the resource stays owned by the render
            // dispatcher; only Read holds that owner off for the length of the projection.
            .Subscribe(h => h.Read(onUpdated));
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
        var subscription = new List<IDisposable>();

        for (int i = 0; i < obj.Count; i++)
        {
            Media.GradientStop item = obj[i];
            var t = item.ToAvaGradientStopSync(time);
            subscription.Add(t.Item2);
            stops.Insert(i, t.Item1);
        }

        var sync = new GradientStopsSync(obj, time, stops, subscription);
        obj.CollectionChangedAsObservable()
            .Subscribe(sync.OnCollectionChanged)
            .DisposeWith(d);
        Disposable.Create(subscription, s =>
        {
            foreach (var item in s)
            {
                item.Dispose();
            }

            s.Clear();
        }).DisposeWith(d);

        return (stops, d);
    }

    // Mirrors each change of the source list into the Avalonia stops, keeping one subscription per
    // stop at the same index.
    private sealed class GradientStopsSync(
        ICoreList<Media.GradientStop> source,
        IObservable<TimeSpan> time,
        Avalonia.Media.GradientStops stops,
        List<IDisposable> subscription)
    {
        public void OnCollectionChanged(NotifyCollectionChangedEventArgs e)
        {
            switch (e.Action)
            {
                case NotifyCollectionChangedAction.Add:
                    OnAdded(e);
                    break;

                case NotifyCollectionChangedAction.Remove:
                    OnRemoved(e);
                    break;

                case NotifyCollectionChangedAction.Replace:
                    OnReplaced(e);
                    break;
                case NotifyCollectionChangedAction.Move:
                    OnMoved(e);
                    break;

                case NotifyCollectionChangedAction.Reset:
                    OnReset();
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(e));
            }
        }

        private void OnAdded(NotifyCollectionChangedEventArgs e)
        {
            int index = e.NewStartingIndex;
            foreach (Media.GradientStop? item in e.NewItems!)
            {
                var t = item!.ToAvaGradientStopSync(time);
                subscription.Insert(index, t.Item2);
                stops.Insert(index++, t.Item1);
            }
        }

        private void OnRemoved(NotifyCollectionChangedEventArgs e)
        {
            int index = e.OldStartingIndex;
            for (int i = e.OldItems!.Count - 1; i >= 0; --i)
            {
                subscription[index + i].Dispose();
                subscription.RemoveAt(index + i);
                stops.RemoveAt(index + i);
            }
        }

        private void OnReplaced(NotifyCollectionChangedEventArgs e)
        {
            int index = e.NewStartingIndex;
            for (int i = 0; i < e.NewItems!.Count; i++)
            {
                var newItem = (Media.GradientStop)e.NewItems![i]!;
                subscription[index].Dispose();
                (Avalonia.Media.GradientStop, IDisposable) t = newItem.ToAvaGradientStopSync(time);
                subscription[index] = t.Item2;
                stops[index] = t.Item1;
                index++;
            }
        }

        private void OnMoved(NotifyCollectionChangedEventArgs e)
        {
            if (e.OldStartingIndex >= 0
                && e.OldStartingIndex < stops.Count
                && e.NewStartingIndex >= 0
                && e.NewStartingIndex < stops.Count
                && e.OldStartingIndex != e.NewStartingIndex
                && e.OldItems is { Count: > 0 } movedItems)
            {
                var movedSubscriptions = subscription.GetRange(e.OldStartingIndex, movedItems.Count);
                subscription.RemoveRange(e.OldStartingIndex, movedItems.Count);
                subscription.InsertRange(e.NewStartingIndex, movedSubscriptions);
                if (movedItems.Count == 1)
                {
                    stops.Move(e.OldStartingIndex, e.NewStartingIndex);
                }
                else
                {
                    var movedStops = stops.Skip(e.OldStartingIndex).Take(movedItems.Count).ToArray();
                    stops.RemoveRange(e.OldStartingIndex, movedItems.Count);
                    stops.InsertRange(e.NewStartingIndex, movedStops);
                }
            }
        }

        private void OnReset()
        {
            stops.Clear();
            foreach (var item in subscription)
            {
                item.Dispose();
            }

            subscription.Clear();
            for (int i = 0; i < source.Count; i++)
            {
                var t = source[i].ToAvaGradientStopSync(time);
                subscription.Add(t.Item2);
                stops.Add(t.Item1);
            }
        }
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
}
