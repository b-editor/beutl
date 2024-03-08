using System.Collections;
using System.Collections.Specialized;

using Avalonia;

using Beutl.Reactive;

using DynamicData;
using DynamicData.Binding;

using Reactive.Bindings;
using Reactive.Bindings.Binding;
using Reactive.Bindings.Extensions;

namespace Beutl;

public static class AvaloniaTypeConverter
{
    public static Avalonia.Vector ToAvaVector(this in Graphics.Vector vector)
    {
        return new(vector.X, vector.Y);
    }
    
    public static Graphics.Vector ToBtlVector(this in Avalonia.Vector vector)
    {
        return new((float)vector.X, (float)vector.Y);
    }

    public static Media.Color ToBtlColor(this FluentAvalonia.UI.Media.Color2 c)
    {
        return new Media.Color(c.A, c.R, c.G, c.B);
    }

    public static Media.GradientStop ToBtlGradientStop(this Avalonia.Media.IGradientStop obj)
    {
        return new Media.GradientStop(obj.Color.ToMedia(), (float)obj.Offset);
    }

    public static Media.Immutable.ImmutableGradientStop ToBtlImmutableGradientStop(this Avalonia.Media.IGradientStop obj)
    {
        return new Media.Immutable.ImmutableGradientStop((float)obj.Offset, obj.Color.ToMedia());
    }

    public static Avalonia.Media.GradientStop ToAvaGradientStop(this Media.IGradientStop obj)
    {
        return new Avalonia.Media.GradientStop(obj.Color.ToAvalonia(), obj.Offset);
    }

    public static (Avalonia.Media.GradientStop, IDisposable) ToAvaGradientStopSync(this Media.GradientStop obj)
    {
        var s = new Avalonia.Media.GradientStop(obj.Color.ToAvalonia(), obj.Offset);
        var d1 = s.Bind(Avalonia.Media.GradientStop.OffsetProperty, obj.GetObservable(Media.GradientStop.OffsetProperty)
            .Select(v => (double)v)
            .ToBinding());
        var d2 = s.Bind(Avalonia.Media.GradientStop.ColorProperty, obj.GetObservable(Media.GradientStop.ColorProperty)
            .Select(v => v.ToAvalonia())
            .ToBinding());

        return (s, Disposable.Create((d1, d2), t => t.DisposeAll()));
    }

    public static Avalonia.Matrix ToAvaMatrix(this in Graphics.Matrix matrix)
    {
        return new(
            matrix.M11, matrix.M12, matrix.M13,
            matrix.M21, matrix.M22, matrix.M23,
            matrix.M31, matrix.M32, matrix.M33);
    }

    public static Graphics.Matrix ToBtlMatrix(this in Avalonia.Matrix matrix)
    {
        return new(
            (float)matrix.M11, (float)matrix.M12, (float)matrix.M13,
            (float)matrix.M21, (float)matrix.M22, (float)matrix.M23,
            (float)matrix.M31, (float)matrix.M32, (float)matrix.M33);
    }

    public static Avalonia.Point ToAvaPoint(this in Graphics.Point point)
    {
        return new(point.X, point.Y);
    }

    public static Graphics.Point ToBtlPoint(this in Avalonia.Point point)
    {
        return new((float)point.X, (float)point.Y);
    }

    public static Avalonia.PixelPoint ToAvaPixelPoint(this in Media.PixelPoint point)
    {
        return new(point.X, point.Y);
    }

    public static Media.PixelPoint ToBtlPixelPoint(this in Avalonia.PixelPoint point)
    {
        return new(point.X, point.Y);
    }

    public static Avalonia.Size ToAvaSize(this in Graphics.Size size)
    {
        return new(size.Width, size.Height);
    }

    public static Graphics.Size ToBtlSize(this in Avalonia.Size size)
    {
        return new((float)size.Width, (float)size.Height);
    }

    public static Avalonia.PixelSize ToAvaPixelSize(this in Media.PixelSize size)
    {
        return new(size.Width, size.Height);
    }

    public static Media.PixelSize ToBtlPixelSize(this in Avalonia.PixelSize size)
    {
        return new(size.Width, size.Height);
    }

    public static Avalonia.Rect ToAvaRect(this in Graphics.Rect rect)
    {
        return new(rect.X, rect.Y, rect.Width, rect.Height);
    }

    public static Graphics.Rect ToBtlRect(this in Avalonia.Rect rect)
    {
        return new((float)rect.X, (float)rect.Y, (float)rect.Width, (float)rect.Height);
    }

    public static Avalonia.PixelRect ToAvaPixelRect(this in Media.PixelRect rect)
    {
        return new(rect.X, rect.Y, rect.Width, rect.Height);
    }

    public static Media.PixelRect ToBtlPixelRect(this in Avalonia.PixelRect rect)
    {
        return new(rect.X, rect.Y, rect.Width, rect.Height);
    }

    public static Avalonia.RelativePoint ToAvaRelativePoint(this in Graphics.RelativePoint pt)
    {
        return new(
            pt.Point.X,
            pt.Point.Y,
            pt.Unit == Graphics.RelativeUnit.Relative
                ? Avalonia.RelativeUnit.Relative
                : Avalonia.RelativeUnit.Absolute);
    }

    // SolidColorBrush.Color, GradientBrush.GradientStopsのみ
    public static Avalonia.Media.Brush? ToAvaBrush(this Media.IBrush? brush)
    {
        switch (brush)
        {
            case Media.ISolidColorBrush s:
                return new Avalonia.Media.SolidColorBrush
                {
                    Color = s.Color.ToAvalonia(),
                };
            case Media.IGradientBrush g:
                {
                    var stops = new Avalonia.Media.GradientStops();
                    stops.AddRange(g.GradientStops.Select(v => v.ToAvaGradientStop()));

                    switch (g)
                    {
                        case Media.ILinearGradientBrush l:
                            return new Avalonia.Media.LinearGradientBrush
                            {
                                GradientStops = stops,
                            };


                        case Media.IConicGradientBrush c:
                            return new Avalonia.Media.ConicGradientBrush
                            {
                                GradientStops = stops,
                            };


                        case Media.IRadialGradientBrush r:
                            return new Avalonia.Media.RadialGradientBrush
                            {
                                GradientStops = stops,
                            };
                    }
                }
                break;
        }

        return null;
    }

    public static (Avalonia.Media.GradientStops, IDisposable) ToAvaGradientStopsSync(this Media.GradientStops obj)
    {
        var d = new CompositeDisposable();
        var stops = new Avalonia.Media.GradientStops();
        var subscription = new Dictionary<Media.GradientStop, IDisposable>();

        for (int i = 0; i < obj.Count; i++)
        {
            Media.GradientStop? item = obj[i];
            var t = item.ToAvaGradientStopSync();
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
                            var t = item!.ToAvaGradientStopSync();
                            subscription[item!] = t.Item2;
                            stops.Insert(index++, t.Item1);
                        }
                        break;

                    case NotifyCollectionChangedAction.Remove:
                        index = e.OldStartingIndex;
                        for (int i = e.OldItems!.Count - 1; i >= 0; --i)
                        {
                            var item = (Media.GradientStop)e.OldItems[i]!;
                            if (subscription.TryGetValue(item, out var d))
                            {
                                d.Dispose();
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
                            if (subscription.TryGetValue(oldItem, out var d))
                            {
                                d.Dispose();
                                subscription.Remove(oldItem);
                            }
                            (Avalonia.Media.GradientStop, IDisposable) t = newItem!.ToAvaGradientStopSync();

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

    public static (Avalonia.Media.Brush?, IDisposable) ToAvaBrushSync(this Media.IBrush? brush)
    {
        switch (brush)
        {
            case Media.SolidColorBrush s:
                {

                    var ss = new Avalonia.Media.SolidColorBrush();
                    var d = ss.Bind(Avalonia.Media.SolidColorBrush.ColorProperty, s.GetObservable(Media.SolidColorBrush.ColorProperty)
                        .Select(v => v.ToAvalonia())
                        .ToBinding());

                    return (ss, d);
                }

            case Media.GradientBrush g:
                {
                    var (stops, d) = g.GradientStops.ToAvaGradientStopsSync();

                    switch (g)
                    {
                        case Media.ILinearGradientBrush l:
                            return (new Avalonia.Media.LinearGradientBrush
                            {
                                GradientStops = stops,
                            }, d);


                        case Media.IConicGradientBrush c:
                            return (new Avalonia.Media.ConicGradientBrush
                            {
                                GradientStops = stops,
                            }, d);


                        case Media.IRadialGradientBrush r:
                            return (new Avalonia.Media.RadialGradientBrush
                            {
                                GradientStops = stops,
                            }, d);
                    }
                }
                break;
        }

        return default;
    }

    // SolidColorBrush.Color, GradientBrush.GradientStopsのみ
    public static Media.IBrush? ToBtlBrush(this Avalonia.Media.Brush? brush)
    {
        switch (brush)
        {
            case Avalonia.Media.ISolidColorBrush s:
                return new Media.SolidColorBrush
                {
                    Color = s.Color.ToMedia(),
                };
            case Avalonia.Media.IGradientBrush g:
                {
                    var stops = new Media.GradientStops();
                    stops.AddRange(g.GradientStops.Select(v => v.ToBtlGradientStop()));

                    switch (g)
                    {
                        case Avalonia.Media.ILinearGradientBrush l:
                            return new Media.LinearGradientBrush
                            {
                                GradientStops = stops,
                            };


                        case Avalonia.Media.IConicGradientBrush c:
                            return new Media.ConicGradientBrush
                            {
                                GradientStops = stops,
                            };


                        case Avalonia.Media.IRadialGradientBrush r:
                            return new Media.RadialGradientBrush
                            {
                                GradientStops = stops,
                            };
                    }
                }
                break;
        }

        return null;
    }

    public static Vector SwapAxis(this Vector vector)
    {
        return new(vector.Y, vector.X);
    }
}
