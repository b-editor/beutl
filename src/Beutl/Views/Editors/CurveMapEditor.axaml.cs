using Avalonia.Controls;
using Avalonia.Interactivity;
using Beutl.Editor.Components.CurvesTab.ViewModels;
using Beutl.ViewModels;
using Beutl.ViewModels.Editors;
using Microsoft.Extensions.DependencyInjection;

namespace Beutl.Views.Editors;

public partial class CurveMapEditor : UserControl
{
    public CurveMapEditor()
    {
        InitializeComponent();
    }

    private void OpenCurvesTab_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is CurveMapEditorViewModel { IsDisposed: false } viewModel
            && viewModel.GetService<EditViewModel>() is { } editViewModel
            && viewModel.TryGetCurves() is { } curves)
        {
            CurvesTabViewModel context = editViewModel.FindToolTab<CurvesTabViewModel>()
                ?? new CurvesTabViewModel(editViewModel);

            context.Effect.Value = curves;
            var prop = viewModel.PropertyAdapter.GetEngineProperty();
            if (prop != null)
            {
                context.SelectCurveByPropertyName(prop.Name);
            }

            editViewModel.OpenToolTab(context);
        }
    }
}
