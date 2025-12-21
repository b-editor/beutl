using Avalonia.Controls;
using Avalonia.Interactivity;
using Beutl.ViewModels;
using Beutl.ViewModels.Editors;
using Beutl.ViewModels.Tools;
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
            && viewModel.GetService<EditViewModel>() is { } editViewModel)
        {
            CurvesTabViewModel context = editViewModel.FindToolTab<CurvesTabViewModel>()
                ?? new CurvesTabViewModel(editViewModel);

            var prop = viewModel.PropertyAdapter.GetEngineProperty();
            if (prop != null)
            {
                context.SelectCurveByPropertyName(prop.Name);
            }

            editViewModel.OpenToolTab(context);
        }
    }
}
