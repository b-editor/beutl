using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Beutl.Editor.Components.UndisplayableDataTab.ViewModels;
using Beutl.Logging;
using Beutl.Utilities;
using Microsoft.Extensions.Logging;

namespace Beutl.Editor.Components.UndisplayableDataTab.Views;

public sealed partial class UndisplayableDataView : UserControl
{
    private static readonly ILogger s_logger = Log.CreateLogger<UndisplayableDataView>();

    public UndisplayableDataView() => InitializeComponent();

    private void RefreshClick(object? sender, RoutedEventArgs e)
        => (DataContext as UndisplayableDataViewModel)?.Refresh();

    private void LocateClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is UndisplayableDataViewModel viewModel && sender is Control { DataContext: UndisplayableDataRow row })
            viewModel.Locate(row);
    }

    private void DetailsCheckedChanged(object? sender, RoutedEventArgs e)
    {
        if (sender is ToggleButton { IsChecked: true, DataContext: UndisplayableDataRow { HasPreview: true } row })
            row.LoadPreview();
    }

    private async void CopyClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Control { DataContext: UndisplayableDataRow row }
            && TopLevel.GetTopLevel(this)?.Clipboard is { } clipboard)
        {
            try
            {
                await clipboard.SetTextAsync(row.LoadPreview());
            }
            catch (Exception ex) when (!ExceptionHelpers.ContainsFatalFailure(ex))
            {
                s_logger.LogWarning(ex, "Could not copy undisplayable data preview to the clipboard.");
            }
        }
    }
}
