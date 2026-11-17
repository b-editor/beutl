using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Beutl.Controls.Curves;
using Beutl.Controls.PropertyEditors;
using Beutl.Editor.Components.CurvesTab.ViewModels;
using Beutl.Editor.Components.Helpers;

namespace Beutl.Editor.Components.CurvesTab.Views;

public partial class CurvesTabView : UserControl
{
    private readonly CompositeDisposable _subscriptions = [];
    private DragSession? _drag;
    private IDisposable? _pickEscape;

    public CurvesTabView()
    {
        InitializeComponent();

        // The boxes edit through the view model, which shapes the curve, keeps the values in their ranges and shows
        // them back.
        RangeWidthEditor.ValueChanged += (_, e) => WithFloat(e, (vm, value) => vm.PreviewRangeWidth(value));
        RangeWidthEditor.ValueConfirmed += (_, e) => WithFloat(e, (vm, value) => vm.ConfirmRangeWidth(value));
        RangeValueEditor.ValueChanged += (_, e) => WithFloat(e, (vm, value) => vm.PreviewRangeValue(value));
        RangeValueEditor.ValueConfirmed += (_, e) => WithFloat(e, (vm, value) => vm.ConfirmRangeValue(value));
        RangeWidthEditor.LostFocus += (_, _) => ShowValue(RangeWidthEditor, (DataContext as CurvesTabViewModel)?.RangeWidth.Value);
        RangeValueEditor.LostFocus += (_, _) => ShowValue(RangeValueEditor, (DataContext as CurvesTabViewModel)?.RangeValue.Value);
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        _subscriptions.Clear();
        if (DataContext is CurvesTabViewModel viewModel)
        {
            viewModel.IsPickingColor
                .Subscribe(OnPickingColorChanged)
                .DisposeWith(_subscriptions);
            // NumberEditor<float> takes no binding from XAML.
            viewModel.RangeWidth
                .Subscribe(width => ShowValue(RangeWidthEditor, width))
                .DisposeWith(_subscriptions);
            viewModel.RangeValue
                .Subscribe(value => ShowValue(RangeValueEditor, value))
                .DisposeWith(_subscriptions);
        }
        else
        {
            OnPickingColorChanged(false);
        }
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        if (DataContext is CurvesTabViewModel viewModel)
            viewModel.IsPickingColor.Value = false;
    }

    // Escape ends the pick wherever the focus is: it is in this tab or in the preview, rarely in the frame itself.
    private void OnPickingColorChanged(bool picking)
    {
        _pickEscape?.Dispose();
        _pickEscape = picking
            ? InputElement.KeyDownEvent.AddClassHandler<InputElement>(
                (_, e) =>
                {
                    if (e.Key == Key.Escape && DataContext is CurvesTabViewModel { IsPickingColor.Value: true } viewModel)
                    {
                        e.Handled = true;
                        viewModel.IsPickingColor.Value = false;
                    }
                },
                RoutingStrategies.Tunnel,
                handledEventsToo: true)
            : null;
    }

    // A value reaches a box as text, and a box with the keyboard focus takes a change of its text for typing, which it
    // reports as an edit and confirms when it loses the focus. A box being typed in therefore keeps its text until then,
    // so that a curve changed meanwhile, as by a drag of its points, is not shaped again with a value nobody entered.
    private static void ShowValue(NumberEditor<float> editor, float? value)
    {
        if (value is { } shown && !editor.IsKeyboardFocusWithin)
            editor.Value = shown;
    }

    private void WithFloat(PropertyEditorValueChangedEventArgs e, Action<CurvesTabViewModel, float> apply)
    {
        if (DataContext is CurvesTabViewModel viewModel && e is PropertyEditorValueChangedEventArgs<float> args)
            apply(viewModel, args.NewValue);
    }

    private void ClearPickedColor_Click(object? sender, RoutedEventArgs e)
    {
        (DataContext as CurvesTabViewModel)?.ClearPickedColor();
    }

    private void CurveEditor_DragStarted(object? sender, EventArgs e)
    {
        if (sender is CurveEditor editor
            && DataContext is CurvesTabViewModel viewModel
            && GetCurvePresenter(editor, viewModel) is { } presenter)
        {
            _drag = DragSession.Begin(
                viewModel, editor, CommandNames.EditProperty, () => EndDrag(editor, presenter), () => EndDrag(editor, presenter));
            presenter.Drag = _drag;
        }
    }

    private void CurveEditor_DragCompleted(object? sender, EventArgs e)
    {
        if (_drag is { } drag)
        {
            drag.Confirm();
        }
        else if (sender is CurveEditor editor && DataContext is CurvesTabViewModel viewModel)
        {
            GetCurvePresenter(editor, viewModel)?.EndEdit();
        }
    }

    // The drag's session keeps or reverts the curve; the editor stops a drag that is still in progress.
    private void EndDrag(CurveEditor editor, CurvePresenterViewModel presenter)
    {
        _drag = null;
        presenter.Drag = null;
        editor.StopDrag();
    }

    private static CurvePresenterViewModel? GetCurvePresenter(CurveEditor editor, CurvesTabViewModel viewModel)
    {
        return editor.Visualization switch
        {
            CurveVisualization.Master => viewModel.MasterCurve.Value,
            CurveVisualization.Red => viewModel.RedCurve.Value,
            CurveVisualization.Green => viewModel.GreenCurve.Value,
            CurveVisualization.Blue => viewModel.BlueCurve.Value,
            CurveVisualization.HueVsHue => viewModel.HueVsHue.Value,
            CurveVisualization.HueVsSaturation => viewModel.HueVsSaturation.Value,
            CurveVisualization.HueVsLuminance => viewModel.HueVsLuminance.Value,
            CurveVisualization.LuminanceVsSaturation => viewModel.LuminanceVsSaturation.Value,
            CurveVisualization.SaturationVsSaturation => viewModel.SaturationVsSaturation.Value,
            _ => null
        };
    }
}
