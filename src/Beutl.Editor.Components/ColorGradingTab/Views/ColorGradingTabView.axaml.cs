using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Beutl.Controls.PropertyEditors;
using Beutl.Editor.Components.ColorGradingTab.ViewModels;
using Beutl.Editor.Components.PropertyEditors.Services;
using Beutl.Editor.Components.PropertyEditors.Views;

namespace Beutl.Editor.Components.ColorGradingTab.Views;

public partial class ColorGradingTabView : UserControl
{
    // A tab this much wider than high, and wide enough to spare the column, leaves the wheels more room with the
    // sliders beside them than above them, as one docked at the bottom does.
    private const double BesideAspectRatio = 2.4;
    private const double BesideMinWidth = 860;
    // Above the wheels, the sliders scroll rather than push the wheels out of a low tab. The wheels keep a pad of 96
    // with their header, jog and boxes and the gap above them, and the tonal range row in its mode; the sliders keep
    // at least two rows.
    private const double WheelsMinHeight = 202;
    private const double TonalRangeHeight = 48;
    private const double MinAdjustmentsHeight = 72;
    private readonly CompositeDisposable _subscriptions = [];
    private float _rangeStart;

    public ColorGradingTabView()
    {
        InitializeComponent();

        DataContextChanged += OnDataContextChanged;
        ContentGrid.GetObservable(BoundsProperty).Subscribe(_ => UpdateAdjustmentsPlacement());
        // Whether the sliders fit above the wheels is known once they are laid out, as when they are first shown.
        AdjustmentGroups.GetObservable(BoundsProperty).Subscribe(_ => UpdateAdjustmentsPlacement());

        // The tonal range slider shows the two editors' values and edits through them, so that its drags preview,
        // confirm and undo as a scrub of either header does.
        LowRangeEditor.GetObservable(NumberEditor<float>.ValueProperty).Subscribe(value => TonalRangeSlider.Low = value);
        HighRangeEditor.GetObservable(NumberEditor<float>.ValueProperty).Subscribe(value => TonalRangeSlider.High = value);
        TonalRangeSlider.DragStarted += OnRangeDragStarted;
        TonalRangeSlider.DragDelta += OnRangeDragDelta;
        TonalRangeSlider.DragCompleted += OnRangeDragCompleted;
    }

    // Where the sliders stand: above the wheels, in a column beside them, or beside them as names and boxes only.
    internal enum AdjustmentsPlacement
    {
        Above,
        Beside,
        Compact,
    }

    // Where the sliders stand; null decides by the shape of the tab, and tests fix it.
    internal AdjustmentsPlacement? Placement
    {
        get;
        set
        {
            field = value;
            UpdateAdjustmentsPlacement();
        }
    }

    internal AdjustmentsPlacement CurrentPlacement { get; private set; }

    // The content's size comes from the tab rather than from what it holds, so moving the sliders does not change it.
    private void UpdateAdjustmentsPlacement()
    {
        Size size = ContentGrid.Bounds.Size;
        double reserve = WheelsMinHeight + ((DataContext as ColorGradingTabViewModel)?.IsLogMode.Value == true ? TonalRangeHeight : 0);
        double aboveHeight = Math.Max(MinAdjustmentsHeight, size.Height - reserve);
        AdjustmentsPlacement placement = Placement ?? ChoosePlacement(size, aboveHeight);
        bool compact = placement == AdjustmentsPlacement.Compact;
        CurrentPlacement = placement;
        ContentGrid.Classes.Set("beside", placement != AdjustmentsPlacement.Above);
        ContentGrid.Classes.Set("compact", compact);
        foreach (StringEditorBase editor in AdjustmentGroups.GetLogicalDescendants().OfType<StringEditorBase>())
            editor.Classes.Set("compact", compact);

        AdjustmentsScroller.MaxHeight = placement == AdjustmentsPlacement.Above ? aboveHeight : double.PositiveInfinity;
    }

    private AdjustmentsPlacement ChoosePlacement(Size size, double aboveHeight)
    {
        if (size.Width >= BesideMinWidth && size.Width >= size.Height * BesideAspectRatio)
            return AdjustmentsPlacement.Beside;

        // A tab no higher than wide that cannot show all the sliders above the wheels shows their names and boxes
        // beside the wheels instead, which leaves the wheels the whole height and the numbers in view.
        return size.Width >= size.Height && AdjustmentGroups.HeightAt(size.Width) > aboveHeight
            ? AdjustmentsPlacement.Compact
            : AdjustmentsPlacement.Above;
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        _subscriptions.Clear();
        if (DataContext is ColorGradingTabViewModel vm)
        {
            _subscriptions.Add(vm.TemperatureEditor.Subscribe(v => BindEditor(TemperatureEditor, v)));
            _subscriptions.Add(vm.TintEditor.Subscribe(v => BindEditor(TintEditor, v)));
            _subscriptions.Add(vm.ExposureEditor.Subscribe(v => BindEditor(ExposureEditor, v)));
            _subscriptions.Add(vm.ContrastEditor.Subscribe(v => BindEditor(ContrastEditor, v)));
            // Beside Contrast in the tone group, the pivot needs no more than its own name.
            _subscriptions.Add(vm.ContrastPivotEditor.Subscribe(v => BindEditor(ContrastPivotEditor, v, Strings.ColorGrading_Pivot)));
            _subscriptions.Add(vm.SaturationEditor.Subscribe(v => BindEditor(SaturationEditor, v)));
            _subscriptions.Add(vm.VibranceEditor.Subscribe(v => BindEditor(VibranceEditor, v)));
            _subscriptions.Add(vm.HueEditor.Subscribe(v => BindEditor(HueEditor, v)));
            _subscriptions.Add(vm.LowRangeEditor.Subscribe(v => BindEditor(LowRangeEditor, v)));
            _subscriptions.Add(vm.HighRangeEditor.Subscribe(v => BindEditor(HighRangeEditor, v)));

            _subscriptions.Add(vm.ShadowsEditor.Subscribe(v => BindEditor(ShadowsPicker, v)));
            _subscriptions.Add(vm.MidtonesEditor.Subscribe(v => BindEditor(MidtonesPicker, v)));
            _subscriptions.Add(vm.HighlightsEditor.Subscribe(v => BindEditor(HighlightsPicker, v)));
            _subscriptions.Add(vm.LiftEditor.Subscribe(v => BindEditor(LiftPicker, v)));
            _subscriptions.Add(vm.GammaEditor.Subscribe(v => BindEditor(GammaPicker, v)));
            _subscriptions.Add(vm.GainEditor.Subscribe(v => BindEditor(GainPicker, v)));
            _subscriptions.Add(vm.OffsetEditor.Subscribe(v => BindEditor(OffsetPicker, v)));
            _subscriptions.Add(vm.IsLogMode.Subscribe(_ => UpdateAdjustmentsPlacement()));
        }
    }

    // The menu gives the tab what the properties panel offers: keyframes, reset and expressions.
    private static void BindEditor(Control editor, IPropertyEditorContext? context, string? header = null)
    {
        if (context == null)
            return;

        PropertyEditorControls.Attach(context, editor);
        if (editor is PropertyEditor propertyEditor)
        {
            propertyEditor.MenuContent = new PropertyEditorMenu { DataContext = context };
            if (header != null)
                propertyEditor.Header = header;
        }
    }

    private void OnResetAllClick(object? sender, RoutedEventArgs e)
    {
        (DataContext as ColorGradingTabViewModel)?.ResetAll();
    }

    private NumberEditor<float> RangeEditor(TonalRangeHandle handle)
        => handle == TonalRangeHandle.Low ? LowRangeEditor : HighRangeEditor;

    private void OnRangeDragStarted(object? sender, TonalRangeEventArgs e)
    {
        _rangeStart = RangeEditor(e.Handle).Value;
    }

    private void OnRangeDragDelta(object? sender, TonalRangeEventArgs e)
    {
        NumberEditor<float> editor = RangeEditor(e.Handle);
        if (editor.IsReadOnly)
            return;

        float step = editor.SmallChange > 0 ? editor.SmallChange : 1;
        float next = MathF.Round((float)e.Value / step) * step;
        // Rounding must not carry a handle past the other, whose value can lie between steps when typed or animated.
        if (LowRangeEditor.Value <= HighRangeEditor.Value)
        {
            next = e.Handle == TonalRangeHandle.Low
                ? MathF.Min(next, HighRangeEditor.Value)
                : MathF.Max(next, LowRangeEditor.Value);
        }

        float previous = editor.Value;
        if (next == previous)
            return;

        editor.Value = next;
        editor.RaiseEvent(new PropertyEditorValueChangedEventArgs<float>(next, previous, PropertyEditor.ValueChangedEvent));
    }

    private void OnRangeDragCompleted(object? sender, TonalRangeEventArgs e)
    {
        NumberEditor<float> editor = RangeEditor(e.Handle);
        if (editor.IsReadOnly || editor.Value == _rangeStart)
            return;

        editor.RaiseEvent(new PropertyEditorValueChangedEventArgs<float>(editor.Value, _rangeStart, PropertyEditor.ValueConfirmedEvent));
    }
}
