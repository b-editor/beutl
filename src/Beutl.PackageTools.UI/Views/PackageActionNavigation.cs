using Avalonia;
using Avalonia.Threading;
using Avalonia.VisualTree;

using Beutl.PackageTools.UI.ViewModels;

using FluentAvalonia.UI.Controls;

namespace Beutl.PackageTools.UI.Views;

internal static class PackageActionNavigation
{
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
                    // Navigation must run on the UI thread.
                    Dispatcher.UIThread.Invoke(() =>
                    {
                        object? nextViewModel = main.Next(viewModel, token);
                        frame.NavigateFromObject(nextViewModel);
                    });
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
