using Avalonia.Controls;
using Beutl.Controls.Curves;
using Beutl.Editor.Components.CurvesTab.ViewModels;

namespace Beutl.Editor.Components.CurvesTab.Views;

public partial class CurvesTabView : UserControl
{
    public CurvesTabView()
    {
        InitializeComponent();
    }

    private void CurveEditor_DragStarted(object? sender, EventArgs e)
    {
        if (sender is CurveEditor editor && DataContext is CurvesTabViewModel viewModel)
        {
            GetCurvePresenter(editor, viewModel)?.BeginEdit();
        }
    }

    private void CurveEditor_DragCompleted(object? sender, EventArgs e)
    {
        if (sender is CurveEditor editor && DataContext is CurvesTabViewModel viewModel)
        {
            GetCurvePresenter(editor, viewModel)?.EndEdit();
        }
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
