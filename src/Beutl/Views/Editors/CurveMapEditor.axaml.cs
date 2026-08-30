using Avalonia.Controls;
using Avalonia.Interactivity;
using Beutl.Editor.Components.CurvesTab.ViewModels;
using Beutl.Editor.Components.Helpers;
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

    private async void OpenCurvesTab_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is CurveMapEditorViewModel { IsDisposed: false } viewModel
            && viewModel.GetService<EditViewModel>() is { } editViewModel
            && viewModel.TryGetCurves() is { } curves)
        {
            CurvesTabViewModel context = ToolTabReuse.Find<CurvesTabViewModel>(
                                             editViewModel,
                                             t => t.Effect.Value == curves,
                                             t => t.Effect.Value is null,
                                             retargetAnyOpen: true)
                                         ?? new CurvesTabViewModel(editViewModel);

            context.Effect.Value = curves;
            var prop = viewModel.PropertyAdapter.GetEngineProperty();
            if (prop != null)
            {
                context.SelectCurveByPropertyName(prop.Name);
            }

            await editViewModel.OpenToolTabAsync(context);
        }
    }
}
