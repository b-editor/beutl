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
    private bool _attached;

    public CurvesTabView()
    {
        InitializeComponent();

        // The boxes edit through the view model, which shapes the curve, keeps the values in their ranges and shows
        // them back.
        WireRangeBox(
            RangeWidthEditor,
            vm => vm.RangeWidth.Value,
            (vm, value) => vm.PreviewRangeWidth(value),
            (vm, value) => vm.ConfirmRangeWidth(value));
        WireRangeBox(
            RangeValueEditor,
            vm => vm.RangeValue.Value,
            (vm, value) => vm.PreviewRangeValue(value),
            (vm, value) => vm.ConfirmRangeValue(value));
    }

    // A box confirms a scrub or its typing only when the value ends away from where it started, yet each step has
    // already shaped the curve, which can lose hand-made points inside the range on the way. So a scrub's release, or
    // the box's loss of the focus, confirms what was previewed when the box did not, making it an undo step of its own.
    private void WireRangeBox(
        NumberEditor<float> box,
        Func<CurvesTabViewModel, float> shown,
        Action<CurvesTabViewModel, float> preview,
        Action<CurvesTabViewModel, float> confirm)
    {
        bool previewed = false;
        box.ValueChanged += (_, e) => previewed |= WithFloat(e, preview);
        box.ValueConfirmed += (_, e) =>
        {
            previewed = false;
            WithFloat(e, confirm);
        };
        box.AddHandler(PointerReleasedEvent, (_, _) => EndUnconfirmed(), RoutingStrategies.Bubble, handledEventsToo: true);
        box.LostFocus += (_, _) =>
        {
            EndUnconfirmed();
            ShowValue(box, DataContext is CurvesTabViewModel viewModel ? shown(viewModel) : null);
        };

        void EndUnconfirmed()
        {
            if (!previewed || box.IsKeyboardFocusWithin)
                return;

            previewed = false;
            if (DataContext is CurvesTabViewModel viewModel)
                confirm(viewModel, box.Value);
        }
    }

    // The dock makes a new view each time the tab is shown and leaves the old one holding its data context, so the
    // subscriptions to the long-lived view model last only while the view is in a window, and the old view can go.
    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _attached = true;
        Observe();
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (_attached)
            Observe();
    }

    private void Observe()
    {
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
        _attached = false;
        if (DataContext is CurvesTabViewModel viewModel)
            viewModel.IsPickingColor.Value = false;
        _subscriptions.Clear();
        OnPickingColorChanged(false);
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

    private bool WithFloat(PropertyEditorValueChangedEventArgs e, Action<CurvesTabViewModel, float> apply)
    {
        if (DataContext is not CurvesTabViewModel viewModel || e is not PropertyEditorValueChangedEventArgs<float> args)
            return false;

        apply(viewModel, args.NewValue);
        return true;
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
