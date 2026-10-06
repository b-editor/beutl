using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Beutl.PackageTools.UI.ViewModels;

using FluentAvalonia.UI.Controls;
using FluentAvalonia.UI.Controls.Primitives;
using FluentAvalonia.UI.Navigation;

namespace Beutl.PackageTools.UI.Views;


public partial class UninstallPage : PackageToolPage
{
    private readonly Lazy<Control> _buttons;

    private readonly Lazy<Control> _cancelButton;

    private CancellationTokenSource? _cts;

    public UninstallPage()
    {
        _buttons = new(() => TaskDialogButtons.CreateBackPanel(this));

        _cancelButton = new(() => TaskDialogButtons.CreatePanel(Strings.Cancel, (_, _) =>
        {
            _cts?.Cancel();
        }));

        AddHandler(FAFrame.NavigatedToEvent, OnNavigatedTo, RoutingStrategies.Direct);
        InitializeComponent();
    }

    private async void OnNavigatedTo(object? sender, FANavigationEventArgs e)
    {
        Scroll.SetCurrentValue(ScrollViewer.OffsetProperty, new Vector(0, 0));
        if (e.Parameter is UninstallViewModel)
        {
            DataContext = e.Parameter;
        }

        if (DataContext is UninstallViewModel viewModel)
        {
            if (viewModel.Finished.Value)
            {
                ButtonsContainer = _buttons.Value;
            }
            else
            {
                ButtonsContainer = _cancelButton.Value;
                _cts?.Cancel();
                _cts = new CancellationTokenSource();
                CancellationToken token = _cts.Token;
                await PackageActionNavigation.RunThenNavigateAsync(
                    this,
                    viewModel,
                    operationToken => Task.Run(() => viewModel.Run(operationToken)),
                    token);
            }
        }
    }
}
