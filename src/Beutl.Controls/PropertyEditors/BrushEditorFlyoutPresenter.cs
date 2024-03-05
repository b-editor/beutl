#nullable enable

using System.Reactive.Disposables;
using System.Reactive.Linq;

using Avalonia;

using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.Immutable;

using Beutl.Reactive;

using FluentAvalonia.UI.Media;


namespace Beutl.Controls.PropertyEditors;

public class BrushEditorFlyoutPresenter : DraggablePickerFlyoutPresenter
{
    public static readonly StyledProperty<Brush?> BrushProperty =
        AvaloniaProperty.Register<BrushEditorFlyoutPresenter, Brush?>(nameof(Brush));

    private readonly CompositeDisposable _disposables = [];
    private const string Palette = ":palette";
    private const string Solid = ":solid";
    private const string Gradient = ":gradient";
    private ToggleButton? _solidBrushTabButton;
    private ToggleButton? _gradientBrushTabButton;
    private ToggleButton? _paletteTabButton;
    private Control? _paletteContent;
    private ContentPresenter? _contentPresenter;
    private ComboBox? _gradientTypeBox;
    private GradientStopsSlider? _gradientStopsSlider;
    private ToggleButton? _lastSelectedTab;

    // ドラッグ操作中
    public event EventHandler<(int OldIndex, int NewIndex, GradientStop Object)>? GradientStopChanged;

    // ドラッグ操作完了
    public event EventHandler<(int OldIndex, int NewIndex, GradientStop Object, ImmutableGradientStop OldObject)>? GradientStopConfirmed;

    public event EventHandler<(int Index, GradientStop Object)>? GradientStopDeleted;

    public event EventHandler<(int Index, GradientStop Object)>? GradientStopAdded;

    public event EventHandler<(Color2 OldValue, Color2 NewValue)>? ColorChanged;

    public event EventHandler<(Color2 OldValue, Color2 NewValue)>? ColorConfirmed;

    public event EventHandler<BrushType>? BrushTypeChanged;

    public Brush? Brush
    {
        get => GetValue(BrushProperty);
        set => SetValue(BrushProperty, value);
    }

    public void SetColorPaletteItem(Color2 color)
    {
        if (Content is SimpleColorPicker cp2)
        {
            cp2.SetColor(color);
            if (_lastSelectedTab != null)
                _lastSelectedTab.IsChecked = true;
            if (_paletteTabButton != null)
                _paletteTabButton.IsChecked = false;

            OnTabButtonIsCheckedChanged();
        }
    }

    private void UpdateTabCheckedState()
    {
        if (_solidBrushTabButton == null || _gradientBrushTabButton == null) return;

        switch (Brush)
        {
            case SolidColorBrush:
                _solidBrushTabButton.IsChecked = true;
                _gradientBrushTabButton.IsChecked = false;
                break;
            case GradientBrush:
                _solidBrushTabButton.IsChecked = false;
                _gradientBrushTabButton.IsChecked = true;
                break;
        }

        PseudoClasses.Set(Gradient, _gradientBrushTabButton.IsChecked == true);
        PseudoClasses.Set(Solid, _solidBrushTabButton.IsChecked == true);

        OnTabButtonIsCheckedChanged();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == BrushProperty)
        {
            UpdateTabCheckedState();
            if (change.OldValue is GradientBrush oldGradientBrush)
            {
                oldGradientBrush.PropertyChanged -= OnGradientBrushPropertyChanged;
                if (_gradientStopsSlider != null)
                {
                    _gradientStopsSlider.Stops = null;
                }
            }

            if (change.NewValue is GradientBrush newGradientBrush)
            {
                newGradientBrush.PropertyChanged += OnGradientBrushPropertyChanged;

                if (_gradientStopsSlider != null)
                {
                    _gradientStopsSlider.Stops = newGradientBrush.GradientStops;
                    _gradientStopsSlider.SelectedStop = newGradientBrush.GradientStops.FirstOrDefault();
                }

                if (_gradientTypeBox != null)
                {
                    _gradientTypeBox.SelectedIndex = GetGradientTabIndex(newGradientBrush.GetType());
                }
            }

            if (change.NewValue is SolidColorBrush newSolid)
            {
                if (Content is SimpleColorPicker colorPicker)
                {
                    colorPicker.Color = newSolid.Color;
                }
            }
        }
        else if (change.Property == ContentProperty)
        {
            if (change.OldValue is SimpleColorPicker oldValue)
            {
                oldValue.ColorChanged -= OnColorPickerColorChanged;
                oldValue.ColorConfirmed -= OnColorPickerColorConfirmed;
            }

            if (change.NewValue is SimpleColorPicker newValue)
            {
                newValue.ColorChanged += OnColorPickerColorChanged;
                newValue.ColorConfirmed += OnColorPickerColorConfirmed;
            }
        }
    }

    private void OnGradientBrushPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property == GradientBrush.GradientStopsProperty)
        {
            if (e.NewValue is GradientStops newValue && _gradientStopsSlider != null)
            {
                _gradientStopsSlider.Stops = newValue;
                _gradientStopsSlider.SelectedStop = newValue.FirstOrDefault();
            }
        }
    }

    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        _disposables.Clear();
        base.OnApplyTemplate(e);
        _solidBrushTabButton = e.NameScope.Find<ToggleButton>("SolidBrushTabButton");
        _gradientBrushTabButton = e.NameScope.Find<ToggleButton>("GradientBrushTabButton");
        _paletteTabButton = e.NameScope.Find<ToggleButton>("PaletteTabButton");
        _paletteContent = e.NameScope.Find<Control>("PaletteContent");
        _contentPresenter = e.NameScope.Find<ContentPresenter>("ContentPresenter");
        _gradientTypeBox = e.NameScope.Find<ComboBox>("GradientTypeBox");
        _gradientStopsSlider = e.NameScope.Find<GradientStopsSlider>("GradientStopsSlider");

        foreach (ToggleButton? item in new[] { _solidBrushTabButton, _gradientBrushTabButton, _paletteTabButton })
        {
            item?.AddDisposableHandler(Button.ClickEvent, OnTagButtonClicked)
                .DisposeWith(_disposables);
        }

        UpdateTabCheckedState();

        if (_gradientTypeBox != null)
        {
            _gradientTypeBox.SelectedIndex = GetGradientTabIndex(Brush?.GetType());

            _gradientTypeBox.GetObservable(ComboBox.SelectedIndexProperty)
                .Subscribe(OnGradientTypeChanged)
                .DisposeWith(_disposables);
        }

        if (_gradientStopsSlider != null)
        {
            if (Brush is GradientBrush { GradientStops: { } stops })
            {
                _gradientStopsSlider.Stops = stops;
                _gradientStopsSlider.SelectedStop = stops.FirstOrDefault();
            }

            _gradientStopsSlider.GetObservable(GradientStopsSlider.SelectedStopProperty)
                .Subscribe(OnSelectedStopChanged)
                .DisposeWith(_disposables);

            Observable.FromEventPattern<(int OldIndex, int NewIndex, GradientStop Object)>(_gradientStopsSlider, nameof(_gradientStopsSlider.Changed))
                .Subscribe(t => GradientStopChanged?.Invoke(this, t.EventArgs))
                .DisposeWith(_disposables);

            Observable.FromEventPattern<(int OldIndex, int NewIndex, GradientStop Object, ImmutableGradientStop OldObject)>(_gradientStopsSlider, nameof(_gradientStopsSlider.Confirmed))
                .Subscribe(t => GradientStopConfirmed?.Invoke(this, t.EventArgs))
                .DisposeWith(_disposables);

            Observable.FromEventPattern<(int Index, GradientStop Object)>(_gradientStopsSlider, nameof(_gradientStopsSlider.Added))
                .Subscribe(t => GradientStopAdded?.Invoke(this, t.EventArgs))
                .DisposeWith(_disposables);

            Observable.FromEventPattern<(int Index, GradientStop Object)>(_gradientStopsSlider, nameof(_gradientStopsSlider.Deleted))
                .Subscribe(t => GradientStopDeleted?.Invoke(this, t.EventArgs))
                .DisposeWith(_disposables);
        }
    }

    private static int GetGradientTabIndex(Type? type)
    {
        if (type == typeof(LinearGradientBrush))
        {
            return 0;
        }
        else if (type == typeof(ConicGradientBrush))
        {
            return 1;
        }
        else if (type == typeof(RadialGradientBrush))
        {
            return 2;
        }
        else
        {
            return 0;
        }
    }

    private static Type? GetGradientType(int index)
    {
        return index switch
        {
            0 => typeof(LinearGradientBrush),
            1 => typeof(ConicGradientBrush),
            2 => typeof(RadialGradientBrush),
            _ => null
        };
    }

    private static BrushType? GetGradientBrushType(int index)
    {
        return index switch
        {
            0 => BrushType.LinearGradientBrush,
            1 => BrushType.ConicGradientBrush,
            2 => BrushType.RadialGradientBrush,
            _ => null
        };
    }

    private void OnTagButtonClicked(object? sender, RoutedEventArgs e)
    {
        if (sender is ToggleButton self)
        {
            foreach (ToggleButton? item in new[] { _solidBrushTabButton, _gradientBrushTabButton, _paletteTabButton })
            {
                if (item != self && item != null)
                {
                    if (item.IsChecked == true)
                    {
                        _lastSelectedTab = item;
                    }

                    item.IsChecked = false;
                }
            }

            OnTabButtonIsCheckedChanged();
        }
    }

    private void OnGradientTypeChanged(int obj)
    {
        if (_gradientBrushTabButton?.IsChecked == true)
        {
            MakeGradientBrush();
        }
    }

    private void OnColorPickerColorConfirmed(SimpleColorPicker sender, (Color2 OldValue, Color2 NewValue) args)
    {
        if (_gradientStopsSlider?.SelectedStop is { } stop && _gradientStopsSlider.Stops != null)
        {
            stop.Color = args.NewValue;
            int index = _gradientStopsSlider.Stops.IndexOf(stop);
            GradientStopConfirmed?.Invoke(this, (index, index, stop, new(stop.Offset, args.OldValue)));
        }
        else if (Brush is SolidColorBrush)
        {
            ColorConfirmed?.Invoke(this, args);
        }
    }

    private void OnColorPickerColorChanged(SimpleColorPicker sender, (Color2 OldValue, Color2 NewValue) args)
    {
        if (_gradientStopsSlider?.SelectedStop is { } stop && _gradientStopsSlider.Stops != null)
        {
            if (stop.Color != (Color)args.NewValue)
            {
                stop.Color = args.NewValue;
                int index = _gradientStopsSlider.Stops!.IndexOf(stop);
                GradientStopChanged?.Invoke(this, (index, index, stop));
            }
        }
        else if (Brush is SolidColorBrush solid)
        {
            solid.Color = args.NewValue;
            ColorChanged?.Invoke(this, args);
        }
    }

    private void OnSelectedStopChanged(GradientStop? stop)
    {
        if (Content is SimpleColorPicker colorPicker
            && stop != null
            && _gradientBrushTabButton?.IsChecked == true)
        {
            colorPicker.Color = stop.Color;
        }
    }

    private void OnTabButtonIsCheckedChanged()
    {
        if (_paletteTabButton != null && _solidBrushTabButton != null && _gradientBrushTabButton != null)
        {
            if (_paletteTabButton.IsChecked == true
                && _paletteContent != null
                && _contentPresenter != null)
            {
                _paletteContent.MaxHeight = _contentPresenter.Bounds.Height;
            }

            PseudoClasses.Set(Palette, _paletteTabButton.IsChecked == true);
            PseudoClasses.Set(Gradient, _gradientBrushTabButton.IsChecked == true);
            PseudoClasses.Set(Solid, _solidBrushTabButton.IsChecked == true);
            if (_gradientBrushTabButton.IsChecked == true)
            {
                MakeGradientBrush();
            }
            else if (_solidBrushTabButton.IsChecked == true)
            {
                MakeSolidColorBrush();
            }
            else if (_paletteTabButton.IsChecked == true)
            {
            }
            else
            {
                BrushTypeChanged?.Invoke(this, BrushType.Null);
            }
        }
    }

    private void MakeGradientBrush()
    {
        if (_gradientTypeBox != null)
        {
            if (GetGradientType(_gradientTypeBox.SelectedIndex) != Brush?.GetType())
            {
                BrushType? brushType = GetGradientBrushType(_gradientTypeBox.SelectedIndex);
                if (brushType.HasValue)
                {
                    BrushTypeChanged?.Invoke(this, brushType.Value);
                }
            }
        }
    }

    private void MakeSolidColorBrush()
    {
        if (Brush is not SolidColorBrush)
        {
            BrushTypeChanged?.Invoke(this, BrushType.SolidColorBrush);
        }
    }
}
