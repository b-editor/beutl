using Avalonia.Controls;
using Beutl.ViewModels.Dialogs;

namespace Beutl.Views.Tools;

public partial class AiVideoGenerationView : UserControl
{
    private IDisposable? _planReturnRefresh;

    public AiVideoGenerationView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        _planReturnRefresh?.Dispose();
        _planReturnRefresh = DataContext is AiVideoGenerationDialogViewModel viewModel
            ? AiPlanReturnRefresh.Attach(
                this,
                viewModel.AiPlanCoordinator,
                () =>
                {
                    viewModel.RefreshAvailability();
                    viewModel.RefreshModels();
                })
            : null;
    }

    protected override void OnLoaded(Avalonia.Interactivity.RoutedEventArgs e)
    {
        base.OnLoaded(e);
        (DataContext as AiVideoGenerationDialogViewModel)?.RefreshAvailability();
    }
}
