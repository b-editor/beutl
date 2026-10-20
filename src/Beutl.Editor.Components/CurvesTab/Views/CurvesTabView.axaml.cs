using Avalonia.Controls;
using Beutl.Controls.Curves;
using Beutl.Editor.Components.CurvesTab.ViewModels;
using Beutl.Editor.Components.Helpers;

namespace Beutl.Editor.Components.CurvesTab.Views;

public partial class CurvesTabView : UserControl
{
    private DragSession? _drag;

    public CurvesTabView()
    {
        InitializeComponent();
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
