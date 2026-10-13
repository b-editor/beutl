using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.VisualTree;

using Beutl.PackageTools.UI.ViewModels;

using FluentAvalonia.UI.Controls;
using FluentAvalonia.UI.Navigation;

namespace Beutl.PackageTools.UI.Views;

internal static class PackageActionNavigation
{
    // The NavigatedTo handling of the install and uninstall pages: a finished action shows the back button,
    // an unfinished one shows the cancel button and runs.
    public static void HandleNavigatedTo<TViewModel>(
        PackageToolPage page,
        ScrollViewer scroll,
        Func<TViewModel, CancellationToken, Task> operation)
        where TViewModel : ActionViewModel
    {
        CancellationTokenSource? cts = null;
        var buttons = new Lazy<Control>(() => TaskDialogButtons.CreateBackPanel(page));
        var cancelButton = new Lazy<Control>(() => TaskDialogButtons.CreatePanel(Strings.Cancel, (_, _) =>
        {
            cts?.Cancel();
        }));

        page.AddHandler(FAFrame.NavigatedToEvent, async (object? _, FANavigationEventArgs e) =>
        {
            scroll.SetCurrentValue(ScrollViewer.OffsetProperty, new Vector(0, 0));
            if (e.Parameter is TViewModel)
            {
                page.DataContext = e.Parameter;
            }

            if (page.DataContext is TViewModel viewModel)
            {
                if (viewModel.Finished.Value)
                {
                    page.ButtonsContainer = buttons.Value;
                }
                else
                {
                    page.ButtonsContainer = cancelButton.Value;
                    cts?.Cancel();
                    cts = new CancellationTokenSource();
                    CancellationToken token = cts.Token;
                    await RunThenNavigateAsync(
                        page,
                        viewModel,
                        operationToken => operation(viewModel, operationToken),
                        token);
                }
            }
        }, RoutingStrategies.Direct);
    }

    public static async Task RunThenNavigateAsync(
        Visual page,
        ActionViewModel viewModel,
        Func<CancellationToken, Task> operation,
        CancellationToken token)
    {
        FAFrame? frame = page.FindAncestorOfType<FAFrame>();
        if (frame is not { DataContext: MainViewModel main })
            return;

        try
        {
            await main.RunOperationAsync(
                operation,
                () =>
                {
                    object? nextViewModel = main.Next(viewModel, token);
                    frame.NavigateFromObject(nextViewModel);
                },
                token);
        }
        catch (OperationCanceledException)
        {
            return;
        }
        catch (ObjectDisposedException)
        {
            return;
        }
    }
}
