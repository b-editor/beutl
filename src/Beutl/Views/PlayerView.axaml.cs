using Avalonia;
using Avalonia.Controls;
using Beutl.Logging;
using Beutl.ViewModels;

using Microsoft.Extensions.Logging;

namespace Beutl.Views;

public partial class PlayerView : UserControl
{
    private readonly CompositeDisposable _disposables = [];
    private readonly ILogger _logger = Log.CreateLogger<PlayerView>();

    public PlayerView()
    {
        InitializeComponent();
        SetupEventHandlers();
    }

    protected override async void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        if (DataContext is PlayerViewModel { IsPlaying.Value: true } viewModel)
        {
            await viewModel.Pause();
        }
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        _disposables.Clear();
        if (DataContext is PlayerViewModel vm)
        {
            SetupDataContextBindings(vm);
        }
    }
}
