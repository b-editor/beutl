using Avalonia;
using Avalonia.Controls;

using Beutl.Controls.PropertyEditors;
using Beutl.ViewModels.Editors;
using Beutl.ViewModels.Tools;
using Beutl.Views.Editors;

namespace Beutl.Views.Tools;

public partial class ColorGradingTab : UserControl
{
    private readonly CompositeDisposable _subscriptions = [];

    public ColorGradingTab()
    {
        InitializeComponent();

        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        _subscriptions.Clear();
        if (DataContext is ColorGradingTabViewModel vm)
        {
            _subscriptions.Add(vm.TemperatureEditor.Subscribe(v => BindNumberEditor(TemperatureEditor, v)));
            _subscriptions.Add(vm.TintEditor.Subscribe(v => BindNumberEditor(TintEditor, v)));
            _subscriptions.Add(vm.ExposureEditor.Subscribe(v => BindNumberEditor(ExposureEditor, v)));
            _subscriptions.Add(vm.ContrastEditor.Subscribe(v => BindNumberEditor(ContrastEditor, v)));
            _subscriptions.Add(vm.ContrastPivotEditor.Subscribe(v => BindNumberEditor(ContrastPivotEditor, v)));
            _subscriptions.Add(vm.SaturationEditor.Subscribe(v => BindNumberEditor(SaturationEditor, v)));
            _subscriptions.Add(vm.VibranceEditor.Subscribe(v => BindNumberEditor(VibranceEditor, v)));
            _subscriptions.Add(vm.HueEditor.Subscribe(v => BindNumberEditor(HueEditor, v)));
            _subscriptions.Add(vm.LowRangeEditor.Subscribe(v => BindNumberEditor(LowRangeEditor, v)));
            _subscriptions.Add(vm.HighRangeEditor.Subscribe(v => BindNumberEditor(HighRangeEditor, v)));

            _subscriptions.Add(vm.ShadowsEditor.Subscribe(v => BindColorPicker(ShadowsPicker, v)));
            _subscriptions.Add(vm.MidtonesEditor.Subscribe(v => BindColorPicker(MidtonesPicker, v)));
            _subscriptions.Add(vm.HighlightsEditor.Subscribe(v => BindColorPicker(HighlightsPicker, v)));
            _subscriptions.Add(vm.LiftEditor.Subscribe(v => BindColorPicker(LiftPicker, v)));
            _subscriptions.Add(vm.GammaEditor.Subscribe(v => BindColorPicker(GammaPicker, v)));
            _subscriptions.Add(vm.GainEditor.Subscribe(v => BindColorPicker(GainPicker, v)));
            _subscriptions.Add(vm.OffsetEditor.Subscribe(v => BindColorPicker(OffsetPicker, v)));
        }
    }

    private void BindNumberEditor(NumberEditor<float> editor, NumberEditorViewModel<float>? viewModel)
    {
        editor.MenuContent = new PropertyEditorMenu();
        if (viewModel != null)
        {
            viewModel.Accept(editor);
        }
    }

    private void BindColorPicker(ColorGradingWheel picker, GradingColorEditorViewModel? viewModel)
    {
        if (viewModel == null)
        {
            picker.IsEnabled = false;
            return;
        }

        picker.IsEnabled = true;
        picker.MenuContent = new PropertyEditorMenu();
        viewModel.Accept(picker);
    }
}
