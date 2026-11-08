using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
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
    // Above the wheels, the sliders scroll rather than push the wheels out of a low tab. The wheels keep a pad of
    // MinPadSize with their header, jog and boxes, as the wheels measure them, the gap above them (the scroller's
    // bottom margin) and the tonal range row in its mode; the sliders keep at least two rows.
    private const double MinPadSize = 96;
    private const double AdjustmentsGap = 12;
    private const double TonalRangeHeight = 48;
    private const double MinAdjustmentsHeight = 72;
    private readonly CompositeDisposable _subscriptions = [];
    private readonly Control[] _sliderRows;
    private float _rangeStart;

    public ColorGradingTabView()
    {
        InitializeComponent();
        _sliderRows = [TemperatureEditor, TintEditor, ExposureEditor, ContrastEditor, ContrastPivotEditor, SaturationEditor, VibranceEditor, HueEditor];

        DataContextChanged += OnDataContextChanged;
        // The placement follows the content's size and, since whether the sliders fit above the wheels is known once
        // they are laid out, theirs; a scroll that only moves the sliders changes neither.
        ContentGrid.SizeChanged += (_, _) => UpdateAdjustmentsPlacement();
        AdjustmentGroups.SizeChanged += (_, _) => UpdateAdjustmentsPlacement();
        UpdateAdjustmentsPlacement();

        // The tonal range slider shows the two editors' values and edits through them, so that its drags preview,
        // confirm and undo as a scrub of either header does. Both ranges step alike.
        LowRangeEditor.GetObservable(NumberEditor<float>.ValueProperty).Subscribe(value => TonalRangeSlider.Low = value);
        HighRangeEditor.GetObservable(NumberEditor<float>.ValueProperty).Subscribe(value => TonalRangeSlider.High = value);
        LowRangeEditor.GetObservable(NumberEditor<float>.SmallChangeProperty).Subscribe(step => TonalRangeSlider.Step = step);
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
        // Both modes' wheels have the same header, jog and boxes, so whichever were laid out tell their height.
        double chrome = Math.Max(LiftWheels.ChromeHeight, LogWheels.ChromeHeight);
        bool log = (DataContext as ColorGradingTabViewModel)?.IsLogMode.Value == true;
        double reserve = MinPadSize + chrome + AdjustmentsGap + (log ? TonalRangeHeight : 0);
        double aboveHeight = Math.Max(MinAdjustmentsHeight, size.Height - reserve);
        AdjustmentsPlacement placement = Placement ?? ChoosePlacement(size, aboveHeight);
        bool compact = placement == AdjustmentsPlacement.Compact;
        CurrentPlacement = placement;
        ContentGrid.Classes.Set("beside", placement != AdjustmentsPlacement.Above);
        ContentGrid.Classes.Set("compact", compact);
        foreach (Control row in _sliderRows)
            row.Classes.Set("compact", compact);

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
    private static void BindEditor(PropertyEditor editor, IPropertyEditorContext? context, string? header = null)
    {
        if (context == null)
            return;

        PropertyEditorControls.Attach(context, editor);
        editor.MenuContent = new PropertyEditorMenu { DataContext = context };
        if (header != null)
            editor.Header = header;
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

    // The slider has already put the value on a step and stopped it at the other handle.
    private void OnRangeDragDelta(object? sender, TonalRangeEventArgs e)
    {
        NumberEditor<float> editor = RangeEditor(e.Handle);
        if (!editor.IsReadOnly)
            editor.ApplyValue((float)e.Value);
    }

    private void OnRangeDragCompleted(object? sender, TonalRangeEventArgs e)
    {
        NumberEditor<float> editor = RangeEditor(e.Handle);
        if (!editor.IsReadOnly)
            editor.ConfirmValue(_rangeStart);
    }
}
