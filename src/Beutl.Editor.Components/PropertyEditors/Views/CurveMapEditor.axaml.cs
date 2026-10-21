using Avalonia.Controls;
using Avalonia.Interactivity;
using Beutl.Editor.Components.CurvesTab.ViewModels;
using Beutl.Editor.Components.Helpers;
using Beutl.Editor.Components.PropertyEditors.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace Beutl.Editor.Components.PropertyEditors.Views;

public partial class CurveMapEditor : UserControl
{
    public CurveMapEditor()
    {
        InitializeComponent();
    }

    private void OpenCurvesTab_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is CurveMapEditorViewModel { IsDisposed: false } viewModel
            && viewModel.GetService<IEditorContext>() is { } editorContext
            && viewModel.TryGetCurves() is { } curves)
        {
            CurvesTabViewModel context = ToolTabReuse.Find<CurvesTabViewModel>(
                                             editorContext,
                                             t => t.Effect.Value == curves,
                                             t => t.Effect.Value is null,
                                             retargetAnyOpen: true)
                                         ?? new CurvesTabViewModel(editorContext);

            context.Effect.Value = curves;
            var prop = viewModel.PropertyAdapter.GetEngineProperty();
            if (prop != null)
            {
                context.SelectCurveByPropertyName(prop.Name);
            }

            editorContext.OpenToolTab(context);
        }
    }
}
