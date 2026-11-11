using System.Reactive.Disposables;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Metadata;
using Avalonia.Controls.Primitives;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Interactivity;
using Beutl.Media;
using Beutl.Reactive;

namespace Beutl.Controls.PropertyEditors;

// A color wheel for a grading color, such as Lift or Shadows in the color grading tab. The pad's puck moves the
// color's chroma and keeps its luma, the jog under it moves R, G and B together, and the boxes take each channel.
// The reset button returns to Neutral; a double click on the pad removes only the chroma. With IsSideBySide the
// header, the jog and the boxes stand in a column beside the pad, for a place wider than it is high.
[TemplatePart("PART_Layout", typeof(Grid))]
[TemplatePart("PART_Header", typeof(Control))]
[TemplatePart("PART_Pad", typeof(ColorBalancePad))]
[TemplatePart("PART_MasterWheel", typeof(GradingWheel))]
[TemplatePart("PART_Components", typeof(GradingColorComponentsEditor))]
[TemplatePart("PART_ResetButton", typeof(Button))]
public class ColorGradingWheel : PropertyEditor
{
    public static readonly DirectProperty<ColorGradingWheel, GradingColor> ColorProperty =
        AvaloniaProperty.RegisterDirect<ColorGradingWheel, GradingColor>(
            nameof(Color),
            o => o.Color,
            (o, v) => o.Color = v,
            defaultBindingMode: BindingMode.TwoWay);

    // The color that changes nothing, such as (1, 1, 1) for Gain.
    public static readonly StyledProperty<GradingColor> NeutralProperty =
        AvaloniaProperty.Register<ColorGradingWheel, GradingColor>(nameof(Neutral));

    // The chroma, in Cb and Cr, at the edge of the pad.
    public static readonly StyledProperty<double> MaxChromaProperty =
        AvaloniaProperty.Register<ColorGradingWheel, double>(nameof(MaxChroma), 0.25);

    // What a drag across the whole jog adds to R, G and B.
    public static readonly StyledProperty<double> MasterRangeProperty =
        AvaloniaProperty.Register<ColorGradingWheel, double>(nameof(MasterRange), 1.0);

    public static readonly StyledProperty<bool> IsSideBySideProperty =
        AvaloniaProperty.Register<ColorGradingWheel, bool>(nameof(IsSideBySide));

    private readonly CompositeDisposable _partHandlers = [];
    private Grid? _layout;
    private Control? _header;
    private ColorBalancePad? _pad;
    private GradingWheel? _master;
    private GradingColorComponentsEditor? _components;
    private Button? _resetButton;
    private GradingColor _oldColor;
    private Vector _startChroma;
    private Vector _padChroma;
    private bool _padDragging;
    private bool _masterDragging;
    private bool _dragStopped;
    private bool _editingComponents;

    public GradingColor Color
    {
        get;
        set => SetAndRaise(ColorProperty, ref field, value);
    }

    public GradingColor Neutral
    {
        get => GetValue(NeutralProperty);
        set => SetValue(NeutralProperty, value);
    }

    public double MaxChroma
    {
        get => GetValue(MaxChromaProperty);
        set => SetValue(MaxChromaProperty, value);
    }

    public double MasterRange
    {
        get => GetValue(MasterRangeProperty);
        set => SetValue(MasterRangeProperty, value);
    }

    public bool IsSideBySide
    {
        get => GetValue(IsSideBySideProperty);
        set => SetValue(IsSideBySideProperty, value);
    }

    // What the wheel needs besides its pad, whether stacked or side by side: the height of the header, the jog and
    // the boxes with the spacing between them, and the width they would like. Measured with no limits, they are what
    // ColorWheelsPanel lays wheels out by. The boxes count at their MinWidth rather than at the width of the numbers
    // in them, so that a number that grows during a drag does not move the wheels.
    internal double ChromeHeight { get; private set; }

    internal double ChromeWidth { get; private set; }

    // Whether the pad or the jog is being dragged; a stopped drag no longer counts.
    public bool IsDragging => (_padDragging || _masterDragging) && !_dragStopped;

    // Ends the drag in progress without ValueConfirmed, for a host that ended the gesture itself. The pad's drag ends
    // with it, so that the cursor its pointer lock hid comes back without waiting for the release.
    public void StopDrag()
    {
        _dragStopped = _padDragging || _masterDragging;
        _pad?.StopDrag();
    }

    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        _partHandlers.Clear();
        base.OnApplyTemplate(e);
        _layout = e.NameScope.Find<Grid>("PART_Layout");
        _header = e.NameScope.Find<Control>("PART_Header");
        _pad = e.NameScope.Find<ColorBalancePad>("PART_Pad");
        _master = e.NameScope.Find<GradingWheel>("PART_MasterWheel");
        _components = e.NameScope.Find<GradingColorComponentsEditor>("PART_Components");
        _resetButton = e.NameScope.Find<Button>("PART_ResetButton");
        _pad?.AddDisposableHandler(ColorBalancePad.DragStartedEvent, OnPadDragStarted).DisposeWith(_partHandlers);
        _pad?.AddDisposableHandler(ColorBalancePad.DragDeltaEvent, OnPadDragDelta).DisposeWith(_partHandlers);
        _pad?.AddDisposableHandler(ColorBalancePad.DragCompletedEvent, OnPadDragCompleted).DisposeWith(_partHandlers);
        _pad?.AddDisposableHandler(ColorBalancePad.ResetRequestedEvent, OnPadResetRequested).DisposeWith(_partHandlers);
        _master?.AddDisposableHandler(Thumb.DragStartedEvent, OnMasterDragStarted).DisposeWith(_partHandlers);
        _master?.AddDisposableHandler(Thumb.DragDeltaEvent, OnMasterDragDelta).DisposeWith(_partHandlers);
        _master?.AddDisposableHandler(Thumb.DragCompletedEvent, OnMasterDragCompleted).DisposeWith(_partHandlers);
        _components?.AddDisposableHandler(ValueChangedEvent, OnComponentsValueChanged).DisposeWith(_partHandlers);
        _components?.AddDisposableHandler(ValueConfirmedEvent, OnComponentsValueConfirmed).DisposeWith(_partHandlers);
        _resetButton?.AddDisposableHandler(Button.ClickEvent, OnResetClick).DisposeWith(_partHandlers);
        UpdateParts();
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        Size size = base.MeasureOverride(availableSize);
        if (_layout != null && _header != null && _master != null && _components != null)
        {
            ChromeHeight = _header.DesiredSize.Height + _master.DesiredSize.Height + _components.DesiredSize.Height
                           + (3 * _layout.RowSpacing);
            ChromeWidth = Math.Max(Inner(_header), _components.MinWidth);
        }

        return size;

        // A part beside the pad keeps a margin from it, which is not the part's own width.
        static double Inner(Control part) => part.DesiredSize.Width - part.Margin.Left - part.Margin.Right;
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == ColorProperty
            || change.Property == NeutralProperty
            || change.Property == MaxChromaProperty
            || change.Property == IsReadOnlyProperty)
        {
            UpdateParts();
        }
    }

    private void UpdateParts()
    {
        GradingColor color = Color;
        bool neutral = color == Neutral;
        _pad?.Chroma = ColorBalance.GetChroma(color, Neutral) / MaxChroma;
        if (!_editingComponents)
            _components?.Color = color;

        _resetButton?.IsEnabled = !neutral && !IsReadOnly;
    }

    private void OnPadDragStarted(object? sender, VectorEventArgs e)
    {
        e.Handled = true;
        if (IsReadOnly)
            return;

        _padDragging = true;
        _dragStopped = false;
        _oldColor = Color;
        _startChroma = _padChroma = ColorBalance.GetChroma(_oldColor, Neutral);
    }

    private void OnPadDragDelta(object? sender, VectorEventArgs e)
    {
        e.Handled = true;
        if (!_padDragging || _dragStopped)
            return;

        // The puck stops at the edge, or at a chroma beyond it that the drag started from.
        Vector next = _padChroma + (e.Vector * MaxChroma);
        double limit = Math.Max(MaxChroma, _startChroma.Length);
        double length = next.Length;
        if (length > limit)
            next *= limit / length;

        _padChroma = next;
        SetColor(_oldColor + ColorBalance.ChromaOffset(next - _startChroma));
    }

    private void OnPadDragCompleted(object? sender, VectorEventArgs e)
    {
        e.Handled = true;
        if (!_padDragging)
            return;

        _padDragging = false;
        if (!_dragStopped)
            Confirm(_oldColor);
    }

    private void OnPadResetRequested(object? sender, RoutedEventArgs e)
    {
        e.Handled = true;
        Replace(ColorBalance.WithoutChroma(Color, Neutral));
    }

    private void OnMasterDragStarted(object? sender, VectorEventArgs e)
    {
        if (IsReadOnly)
            return;

        _masterDragging = true;
        _dragStopped = false;
        _oldColor = Color;
    }

    private void OnMasterDragDelta(object? sender, VectorEventArgs e)
    {
        if (!_masterDragging || _dragStopped || _master is not { Bounds.Width: > 0 } master)
            return;

        // The jog reports the move since the press.
        float delta = (float)(e.Vector.X / master.Bounds.Width * MasterRange);
        SetColor(_oldColor + new GradingColor(delta, delta, delta));
    }

    private void OnMasterDragCompleted(object? sender, VectorEventArgs e)
    {
        if (!_masterDragging)
            return;

        _masterDragging = false;
        if (!_dragStopped)
            Confirm(_oldColor);
    }

    private void OnComponentsValueChanged(object? sender, PropertyEditorValueChangedEventArgs e)
    {
        e.Handled = true;
        if (_components == null || e is not PropertyEditorValueChangedEventArgs<(float, float, float)> args)
            return;

        _editingComponents = true;
        try
        {
            SetColor(_components.ToGradingColorFromTuple(args.NewValue));
        }
        finally
        {
            _editingComponents = false;
        }
    }

    private void OnComponentsValueConfirmed(object? sender, PropertyEditorValueChangedEventArgs e)
    {
        e.Handled = true;
        if (_components == null || e is not PropertyEditorValueChangedEventArgs<(float, float, float)> args)
            return;

        GradingColor oldValue = _components.ToGradingColorFromTuple(args.OldValue);
        GradingColor newValue = _components.ToGradingColorFromTuple(args.NewValue);
        if (oldValue != newValue)
            RaiseEvent(new PropertyEditorValueChangedEventArgs<GradingColor>(newValue, oldValue, ValueConfirmedEvent));
    }

    private void OnResetClick(object? sender, RoutedEventArgs e) => Replace(Neutral);

    // Sets value and confirms it against the color it replaces, as one edit.
    private void Replace(GradingColor value)
    {
        if (IsReadOnly)
            return;

        GradingColor oldColor = Color;
        SetColor(value);
        Confirm(oldColor);
    }

    private void SetColor(GradingColor value)
    {
        GradingColor previous = Color;
        if (previous == value)
            return;

        Color = value;
        RaiseEvent(new PropertyEditorValueChangedEventArgs<GradingColor>(value, previous, ValueChangedEvent));
    }

    private void Confirm(GradingColor oldColor)
    {
        GradingColor current = Color;
        if (oldColor != current)
            RaiseEvent(new PropertyEditorValueChangedEventArgs<GradingColor>(current, oldColor, ValueConfirmedEvent));
    }
}
