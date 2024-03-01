#nullable enable

using System.Reactive.Disposables;

using Avalonia;
using Avalonia.Collections;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.Immutable;

using Beutl.Reactive;
using Beutl.Utilities;
using Beutl.Language;

using FluentAvalonia.UI.Controls;

namespace Beutl.Controls.PropertyEditors;

public class GradientStopsSlider : TemplatedControl
{
    public static readonly StyledProperty<GradientStops?> StopsProperty =
        AvaloniaProperty.Register<GradientStopsSlider, GradientStops?>(nameof(Stops));

    public static readonly StyledProperty<GradientStop?> SelectedStopProperty =
        AvaloniaProperty.Register<GradientStopsSlider, GradientStop?>(nameof(SelectedStop));

    private readonly CompositeDisposable _disposables = [];
    private Rectangle? _backgroundElement;
    private ItemsControl? _itemsControl;
    private GradientStops? _backgroundStops;
    private FAMenuFlyout? _menuFlyout;
    private MenuFlyoutItem? _deleteMenuItem;

    public GradientStops? Stops
    {
        get => GetValue(StopsProperty);
        set => SetValue(StopsProperty, value);
    }

    public GradientStop? SelectedStop
    {
        get => GetValue(SelectedStopProperty);
        set => SetValue(SelectedStopProperty, value);
    }

    private double DragWidth => _itemsControl!.Bounds.Width - 18;

    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        _disposables.Clear();
        base.OnApplyTemplate(e);

        _backgroundElement = e.NameScope.Find<Rectangle>("BackgroundElement");
        _itemsControl = e.NameScope.Find<ItemsControl>("ItemsControl");

        if (_itemsControl != null)
        {
            _itemsControl.ContainerPrepared += OnItemsControlContainerPrepared;
            _itemsControl.ContainerClearing += OnItemsControlContainerClearing;
            _disposables.Add(Disposable.Create(_itemsControl, c =>
            {
                c.ContainerPrepared -= OnItemsControlContainerPrepared;
                c.ContainerClearing -= OnItemsControlContainerClearing;
            }));
        }

        if (_itemsControl != null)
        {
            _itemsControl.AddDisposableHandler(SizeChangedEvent, OnItemsControlSizeChanged)
                .DisposeWith(_disposables);
            _itemsControl.AddDisposableHandler(PointerPressedEvent, OnItemsControlPointerPressed)
                .DisposeWith(_disposables);
            _itemsControl.AddDisposableHandler(PointerMovedEvent, OnItemsControlPointerMoved)
                .DisposeWith(_disposables);
            _itemsControl.AddDisposableHandler(PointerReleasedEvent, OnItemsControlPointerReleased, handledEventsToo: true)
                .DisposeWith(_disposables);
            _itemsControl.AddDisposableHandler(PointerExitedEvent, OnItemsControlPointerExited)
                .DisposeWith(_disposables);
        }

        if (_backgroundElement != null)
        {
            _backgroundStops = [];
            if (Stops != null)
            {
                _backgroundStops.AddRange(Stops.OrderBy(i => i.Offset));
            }

            _backgroundElement.Fill = new LinearGradientBrush
            {
                StartPoint = new RelativePoint(0, 0.5, RelativeUnit.Relative),
                EndPoint = new RelativePoint(1, 0.5, RelativeUnit.Relative),
                GradientStops = _backgroundStops
            };
        }
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == StopsProperty
            && _backgroundStops != null
            && change.NewValue is GradientStops stops)
        {
            _backgroundStops.Clear();
            _backgroundStops.AddRange(stops.OrderBy(i => i.Offset));
        }
        else if (change.Property == SelectedStopProperty)
        {
            if (change.OldValue is GradientStop oldValue)
            {
                GetThumbFromGradientStop(oldValue)?.Classes?.Set("selected", false);
            }

            if (change.NewValue is GradientStop newValue)
            {
                GetThumbFromGradientStop(newValue)?.Classes?.Set("selected", true);
            }
        }
    }

    private void OnItemsControlContainerClearing(object? sender, ContainerClearingEventArgs e)
    {
        if (e.Container is ContentPresenter presenter)
        {
            if (presenter.Child is Thumb thumb)
            {
                RemoveHandlers(thumb);
            }

            presenter.PropertyChanged += OnContentPresenterPropertyChanged;
        }
    }

    private void OnItemsControlContainerPrepared(object? sender, ContainerPreparedEventArgs e)
    {
        if (e.Container is ContentPresenter presenter)
        {
            if (presenter.Child is Thumb thumb)
            {
                AddHandlers(thumb);

                if (presenter.Content is GradientStop stop)
                {
                    UpdateThumb(thumb, stop, presenter);
                }
            }

            presenter.PropertyChanged += OnContentPresenterPropertyChanged;
        }
    }

    private void OnContentPresenterPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property == ContentPresenter.ChildProperty)
        {
            if (e.OldValue is Thumb oldThumb)
            {
                RemoveHandlers(oldThumb);
            }

            if (e.NewValue is Thumb newThumb)
            {
                AddHandlers(newThumb);

                if (sender is ContentPresenter { Content: GradientStop stop } presenter)
                {
                    UpdateThumb(newThumb, stop, presenter);
                }
            }
        }
        else if (e.Property == ContentPresenter.ContentProperty)
        {
            if (e.OldValue is GradientStop oldStop)
            {
                oldStop.PropertyChanged -= OnGradientStopPropertyChanged;
            }

            if (e.NewValue is GradientStop newStop)
            {
                newStop.PropertyChanged += OnGradientStopPropertyChanged;

                if (sender is ContentPresenter { Child: Thumb thumb } presenter)
                {
                    UpdateThumb(thumb, newStop, presenter);
                }
            }
        }
    }

    private Thumb? GetThumbFromGradientStop(GradientStop item)
    {
        return (_itemsControl?.ContainerFromItem(item) as ContentPresenter)?.Child as Thumb;
    }

    private void UpdateThumb(Thumb thumb, GradientStop item, ContentPresenter? presenter = null)
    {
        presenter ??= thumb.Parent as ContentPresenter;
        if (_itemsControl != null
            && presenter != null)
        {
            double relativeLeft = DragWidth * Math.Clamp(item.Offset, 0, 1);

            Canvas.SetLeft(presenter, relativeLeft);

            double y = _itemsControl.Bounds.Height / 2 - (18 / 2);
            //double y = _containerCanvas.Bounds.Height / 2 - presenter.Bounds.Height / 2;
            Canvas.SetTop(presenter, y);
        }

        thumb.Classes.Set("selected", ReferenceEquals(item, SelectedStop));

        thumb.Background = new ImmutableSolidColorBrush(item.Color);
    }

    private void OnGradientStopPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (sender is GradientStop item)
        {
            if (GetThumbFromGradientStop(item) is Thumb thumb)
            {
                if (e.Property == GradientStop.OffsetProperty)
                {
                    UpdateThumb(thumb, item);
                }
                else if (e.Property == GradientStop.ColorProperty)
                {
                    UpdateThumb(thumb, item);
                }
            }
        }
    }

    private void RemoveHandlers(Thumb thumb)
    {
        thumb.DragCompleted -= ThumbDragCompleted;
        thumb.DragDelta -= ThumbDragDelta;
        thumb.DragStarted -= ThumbDragStarted;
        thumb.KeyDown -= ThumbKeyDown;
        thumb.KeyUp -= ThumbKeyUp;
    }

    private void AddHandlers(Thumb thumb)
    {
        thumb.DragCompleted += ThumbDragCompleted;
        thumb.DragDelta += ThumbDragDelta;
        thumb.DragStarted += ThumbDragStarted;
        thumb.KeyDown += ThumbKeyDown;
        thumb.KeyUp += ThumbKeyUp;
    }

    private void ThumbKeyUp(object? sender, KeyEventArgs e)
    {
    }

    private void ThumbKeyDown(object? sender, KeyEventArgs e)
    {
    }

    private void ThumbDragStarted(object? sender, VectorEventArgs e)
    {
        if (sender is Thumb { DataContext: GradientStop stop })
        {
            SelectedStop = stop;
        }
    }

    private void ThumbDragDelta(object? sender, VectorEventArgs e)
    {
        if (sender is not Thumb { Parent: ContentPresenter presenter, DataContext: GradientStop stop } || _itemsControl == null) return;

        double old = Canvas.GetLeft(presenter);
        if (!double.IsFinite(old)) old = 0;

        double x = Math.Clamp(old + e.Vector.X, 0, DragWidth);
        Canvas.SetLeft(presenter, x);

        double y = _itemsControl.Bounds.Height / 2 - presenter.Bounds.Height / 2;
        Canvas.SetTop(presenter, y);

        stop.Offset = x / DragWidth;

        if (_backgroundStops != null)
        {
            int index = _backgroundStops.IndexOf(stop);
            if (index > 0)
            {
                GradientStop prev = _backgroundStops[index - 1];
                if (prev.Offset > stop.Offset)
                {
                    _backgroundStops.Move(index, index - 1);
                    index--;
                }
            }

            if (index + 1 < _backgroundStops.Count)
            {
                GradientStop next = _backgroundStops[index + 1];
                if (next.Offset < stop.Offset)
                {
                    _backgroundStops.Move(index, index + 1);
                }
            }
        }
    }

    private void ThumbDragCompleted(object? sender, VectorEventArgs e)
    {
        if (sender is Thumb { DataContext: GradientStop stop }
            && _backgroundStops != null
            && Stops != null
            && _itemsControl != null)
        {
            int newIndex = _backgroundStops.IndexOf(stop);
            int oldIndex = Stops.IndexOf(stop);
            if (newIndex != oldIndex)
            {
                Stops.Move(oldIndex, newIndex);
            }
        }
    }

    private void OnItemsControlPointerExited(object? sender, PointerEventArgs e)
    {
    }

    private void OnItemsControlPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (e.InitialPressMouseButton == MouseButton.Right
            && _itemsControl != null
            && Stops != null)
        {
            GradientStop? hovered = Stops.FirstOrDefault(i => _itemsControl.ContainerFromItem(i)?.IsPointerOver == true);

            if (_menuFlyout == null || _deleteMenuItem == null)
            {
                _menuFlyout = new FAMenuFlyout();
                _deleteMenuItem = new MenuFlyoutItem
                {
                    Text = Strings.Delete,
                    IconSource = new SymbolIconSource
                    {
                        Symbol = Symbol.Delete
                    }
                };
                _deleteMenuItem.Click += (s, e) =>
                {
                    if (s is MenuFlyoutItem { Tag: GradientStop obj } menu)
                    {
                        Stops.Remove(obj);
                        _backgroundStops?.Remove(obj);
                        menu.Tag = null;
                    }
                };
                _menuFlyout.ItemsSource = new[] { _deleteMenuItem };
            }

            if (hovered != null && Stops.Count > 2)
            {
                _deleteMenuItem.Tag = hovered;
                _menuFlyout.ShowAt(this, true);
            }
        }
    }

    private void OnItemsControlPointerMoved(object? sender, PointerEventArgs e)
    {
    }

    private void OnItemsControlPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        //https://github.com/AvaloniaUI/Avalonia/blob/master/src/Avalonia.Base/Animation/Animators/ColorAnimator.cs
        static double OECF_sRGB(double linear)
        {
            return linear <= 0.0031308d ? linear * 12.92d : (double)(Math.Pow(linear, 1.0d / 2.4d) * 1.055d - 0.055d);
        }
        static double EOCF_sRGB(double srgb)
        {
            return srgb <= 0.04045d ? srgb / 12.92d : (double)Math.Pow((srgb + 0.055d) / 1.055d, 2.4d);
        }
        static Color InterpolateCore(double progress, Color oldValue, Color newValue)
        {
            double oldA = oldValue.A / 255d;
            double oldR = oldValue.R / 255d;
            double oldG = oldValue.G / 255d;
            double oldB = oldValue.B / 255d;

            double newA = newValue.A / 255d;
            double newR = newValue.R / 255d;
            double newG = newValue.G / 255d;
            double newB = newValue.B / 255d;

            // convert from sRGB to linear
            oldR = EOCF_sRGB(oldR);
            oldG = EOCF_sRGB(oldG);
            oldB = EOCF_sRGB(oldB);

            newR = EOCF_sRGB(newR);
            newG = EOCF_sRGB(newG);
            newB = EOCF_sRGB(newB);

            // compute the interpolated color in linear space
            double a = oldA + progress * (newA - oldA);
            double r = oldR + progress * (newR - oldR);
            double g = oldG + progress * (newG - oldG);
            double b = oldB + progress * (newB - oldB);

            // convert back to sRGB in the [0..255] range
            a *= 255d;
            r = OECF_sRGB(r) * 255d;
            g = OECF_sRGB(g) * 255d;
            b = OECF_sRGB(b) * 255d;

            return new Color((byte)Math.Round(a), (byte)Math.Round(r), (byte)Math.Round(g), (byte)Math.Round(b));
        }
        static Color Interpolate(GradientStop prev, GradientStop next, double offset)
        {
            double progress = (offset - prev.Offset) / next.Offset - prev.Offset;
            return InterpolateCore(progress, prev.Color, next.Color);
        }

        PointerPoint point = e.GetCurrentPoint(_itemsControl);
        if (point.Properties.IsLeftButtonPressed
            && _itemsControl != null && Stops != null)
        {
            double width = DragWidth;
            double x = point.Position.X;
            double offset = x / width;
            Color? color = null;

            GradientStop? next = null;
            int index = 0;

            for (int i = 0; i < Stops.Count; i++)
            {
                GradientStop cur = Stops[i];
                if (MathUtilities.LessThanOrClose(cur.Offset, offset))
                {
                    color = cur.Color;
                    index = i + 1;
                    if (i < Stops.Count - 1)
                    {
                        next = Stops[i + 1];
                        if (MathUtilities.LessThanOrClose(offset, next.Offset))
                        {
                            color = Interpolate(cur, next, offset);
                            break;
                        }
                    }
                }
                else
                {
                    color = cur.Color;
                    index = i;
                    break;
                }
            }

            if (!color.HasValue)
            {
                color = next?.Color ?? default;
            }

            var stop = new GradientStop(color.Value, offset);
            Stops.Insert(index, stop);
            _backgroundStops?.Insert(index, stop);
            SelectedStop = stop;
        }
    }

    private void OnItemsControlSizeChanged(object? sender, SizeChangedEventArgs e)
    {
        if (_itemsControl is { } && Stops != null)
        {
            foreach (GradientStop item in Stops)
            {
                if (GetThumbFromGradientStop(item) is { } thumb)
                {
                    UpdateThumb(thumb, item);
                }
            }
        }
    }
}
