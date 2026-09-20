using Avalonia.Controls;
using Beutl.ViewModels.Dialogs;

namespace Beutl.Views.Tools;

public partial class AiVideoEditingView : UserControl
{
    private IDisposable? _planReturnRefresh;

    public AiVideoEditingView()
    {
        InitializeComponent();
        DataContextChanged += (_, _) =>
        {
            _planReturnRefresh?.Dispose();
            _planReturnRefresh = DataContext is AiVideoEditingViewModel group && group.ActiveContent.Value is { } page
                ? AiPlanReturnRefresh.Attach(this, page.AiPlanCoordinator, () =>
                {
                    group.ActiveContent.Value?.RefreshAvailability();
                    group.RefreshModels();
                }) : null;
        };
    }

    protected override void OnLoaded(Avalonia.Interactivity.RoutedEventArgs e)
    {
        base.OnLoaded(e);
        (DataContext as AiVideoEditingViewModel)?.ActiveContent.Value?.RefreshAvailability();
    }
}
