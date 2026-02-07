using Avalonia.Controls;

using Beutl.Controls.PropertyEditors;
using Beutl.Editor.Components.ColorGradingTab.ViewModels;

namespace Beutl.Editor.Components.ColorGradingTab.Views;

public partial class ColorGradingTabView : UserControl
{
    private readonly CompositeDisposable _subscriptions = [];

    public ColorGradingTabView()
    {
        InitializeComponent();

        DataContextChanged += OnDataContextChanged;
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
            _subscriptions.Add(vm.ContrastPivotEditor.Subscribe(v => BindEditor(ContrastPivotEditor, v)));
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

    private static void BindEditor(IPropertyEditorContextVisitor editor, IPropertyEditorContext? context)
    {
        if (context != null)
        {
            context.Accept(editor);
        }
    }
}
