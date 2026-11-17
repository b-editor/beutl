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
    private readonly CompositeDisposable _subscriptions = [];
    private float _rangeStart;

    public ColorGradingTabView()
    {
        InitializeComponent();
        ContentPanel.SliderRows = [TemperatureEditor, TintEditor, ExposureEditor, ContrastEditor, ContrastPivotEditor, SaturationEditor, VibranceEditor, HueEditor];

        DataContextChanged += OnDataContextChanged;
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
        get => ContentPanel.Placement;
        set => ContentPanel.Placement = value;
    }

    internal AdjustmentsPlacement CurrentPlacement => ContentPanel.CurrentPlacement;

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
