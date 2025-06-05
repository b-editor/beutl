﻿using System.Collections.Specialized;
using System.ComponentModel;
using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using Beutl.Media;
using Beutl.Reactive;
using Beutl.Threading;
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

    // SolidColorBrush.Color, GradientBrush.GradientStopsのみ
    public static Avalonia.Media.Brush? ToAvaBrush(this Media.IBrush? brush)
    {
        switch (brush)
        {
            case Media.ISolidColorBrush s:
                return new Avalonia.Media.SolidColorBrush { Color = s.Color.ToAvalonia(), };
            case Media.IGradientBrush g:
                {
                    var stops = new Avalonia.Media.GradientStops();
                    stops.AddRange(g.GradientStops.Select(v => v.ToAvaGradientStop()));

                    switch (g)
                    {
                        case Media.ILinearGradientBrush:
                            return new Avalonia.Media.LinearGradientBrush { GradientStops = stops, };


                        case Media.IConicGradientBrush:
                            return new Avalonia.Media.ConicGradientBrush { GradientStops = stops, };


                        case Media.IRadialGradientBrush:
                            return new Avalonia.Media.RadialGradientBrush { GradientStops = stops, };
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
            Media.GradientStop item = obj[i];
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

                            (Avalonia.Media.GradientStop, IDisposable) t = newItem.ToAvaGradientStopSync();

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

    public static (Avalonia.Media.Brush?, IDisposable, Action?) ToAvaBrushSync(this Media.IBrush? brush)
    {
        switch (brush)
        {
            case Media.SolidColorBrush s:
                {
                    var ss = new Avalonia.Media.SolidColorBrush();
                    var d = ss.Bind(Avalonia.Media.SolidColorBrush.ColorProperty, s
                        .GetObservable(Media.SolidColorBrush.ColorProperty)
                        .Select(v => v.ToAvalonia())
                        .ToBinding());

                    return (ss, d, null);
                }

            case Media.GradientBrush g:
                {
                    (Avalonia.Media.GradientStops stops, IDisposable d) = g.GradientStops.ToAvaGradientStopsSync();

                    switch (g)
                    {
                        case Media.ILinearGradientBrush:
                            return (new Avalonia.Media.LinearGradientBrush { GradientStops = stops, }, d, null);

                        case Media.IConicGradientBrush:
                            return (new Avalonia.Media.ConicGradientBrush { GradientStops = stops, }, d, null);

                        case Media.IRadialGradientBrush:
                            return (new Avalonia.Media.RadialGradientBrush { GradientStops = stops, }, d, null);
                    }
                }
                break;

            case Media.DrawableBrush db:
                {
                    var imageBrush = new ImageBrush();
                    var prop = db.GetObservable(DrawableBrush.DrawableProperty)
                        .ObserveOnUIDispatcher()
                        .Select(i => i != null ? new DrawableImageBrushHandler(db, i, imageBrush) : null)
                        .DisposePreviousValue()
                        .ToReadOnlyReactivePropertySlim();
                    return (imageBrush, prop, () => prop.Value?.Update());
                }
        }

        return default;
    }

    // SolidColorBrush.Color, GradientBrush.GradientStopsのみ
    public static Media.IBrush? ToBtlBrush(this Avalonia.Media.Brush? brush)
    {
        switch (brush)
        {
            case Avalonia.Media.ISolidColorBrush s:
                return new Media.SolidColorBrush { Color = s.Color.ToMedia(), };
            case Avalonia.Media.IGradientBrush g:
                {
                    var stops = new Media.GradientStops();
                    stops.AddRange(g.GradientStops.Select(v => v.ToBtlGradientStop()));

                    switch (g)
                    {
                        case Avalonia.Media.ILinearGradientBrush:
                            return new Media.LinearGradientBrush { GradientStops = stops, };

                        case Avalonia.Media.IConicGradientBrush:
                            return new Media.ConicGradientBrush { GradientStops = stops, };

                        case Avalonia.Media.IRadialGradientBrush:
                            return new Media.RadialGradientBrush { GradientStops = stops, };
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

    public sealed class DrawableImageBrushHandler : IDisposable
    {
        private WriteableBitmap? _bitmap;
        private CancellationTokenSource? _cts;
        private readonly Graphics.Drawable _drawable;
        private readonly ImageBrush _imageBrush;
        private readonly DrawableBrush _drawableBrush;

        public DrawableImageBrushHandler(DrawableBrush drawableBrush, Graphics.Drawable drawable, ImageBrush imageBrush)
        {
            _drawable = drawable;
            _imageBrush = imageBrush;
            _drawableBrush = drawableBrush;
            Update();
            _drawableBrush.PropertyChanged += OnDrawableBrushPropertyChanged;
        }

        private void OnDrawableBrushPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e is not CorePropertyChangedEventArgs ce) return;

            if (ce.Property.Id == TileBrush.StretchProperty.Id)
            {
                Dispatcher.UIThread.Post(() =>
                {
                    _imageBrush.Stretch = _drawableBrush.Stretch switch
                    {
                        Media.Stretch.Fill => Avalonia.Media.Stretch.Fill,
                        Media.Stretch.Uniform => Avalonia.Media.Stretch.Uniform,
                        Media.Stretch.UniformToFill => Avalonia.Media.Stretch.UniformToFill,
                        Media.Stretch.None => Avalonia.Media.Stretch.None,
                        _ => Avalonia.Media.Stretch.Fill,
                    };
                }, DispatcherPriority.Background);
            }
        }

        public void Dispose()
        {
            _drawableBrush.PropertyChanged -= OnDrawableBrushPropertyChanged;
        }

        public void Update()
        {
            _cts?.Cancel();
            _cts = new CancellationTokenSource();
            var token = _cts.Token;

            Graphics.Rendering.RenderThread.Dispatcher.Dispatch(async () =>
            {
                _drawable.Measure(Graphics.Size.Infinity);
                var bounds = _drawable.Bounds;
                using var renderTarget = Graphics.Rendering.RenderTarget.Create(
                    (int)Math.Ceiling(bounds.Width),
                    (int)Math.Ceiling(bounds.Height));
                if (renderTarget is null) return;

                using (var canvas = new Graphics.ImmediateCanvas(renderTarget))
                using (canvas.PushTransform(Graphics.Matrix.CreateTranslation(-bounds.X, -bounds.Y)))
                {
                    canvas.DrawDrawable(_drawable);
                }

                var previous = _bitmap;
                var pixelSize = new PixelSize(renderTarget.Width, renderTarget.Height);
                _bitmap = new WriteableBitmap(pixelSize, new Vector(96, 96), PixelFormat.Bgra8888,
                    AlphaFormat.Unpremul);

                using (var locked = _bitmap.Lock())
                {
                    renderTarget.Value.ReadPixels(
                        new SKImageInfo(pixelSize.Width, pixelSize.Height, SKColorType.Bgra8888,
                            SKAlphaType.Unpremul),
                        locked.Address,
                        locked.RowBytes,
                        0, 0);
                }

                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    _imageBrush.Source = _bitmap;
                    previous?.Dispose();
                }, DispatcherPriority.Background);
            }, DispatchPriority.Low, token);
        }
    }
}
