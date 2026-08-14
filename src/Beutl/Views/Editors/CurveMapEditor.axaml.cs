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
            // Prefer the tab already showing this effect, then an idle one, and only then retarget
            // any open tab. A plain FindToolTab would always hand back the first tab and strand every
            // other instance; dropping straight to `new` would spawn a tab per object instead.
            CurvesTabViewModel context =
                editViewModel.FindToolTab<CurvesTabViewModel>(t => t.Effect.Value == curves)
                ?? editViewModel.FindToolTab<CurvesTabViewModel>(t => t.Effect.Value is null)
                ?? editViewModel.FindToolTab<CurvesTabViewModel>()
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
