using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Beutl.Editor.Components.TimelineTab.ViewModels;

namespace Beutl.Editor.Components.TimelineTab.Views;

public partial class GenerationPlaceholderView : UserControl
{
    public GenerationPlaceholderView()
    {
        InitializeComponent();
    }

    private void Border_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (DataContext is not GenerationPlaceholderViewModel viewModel
            || !e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            return;
        }

        // The placeholder stands in for its job, so a click opens the job and nothing under it.
        e.Handled = true;
        viewModel.Timeline.ShowGeneration(viewModel.Job);
    }

    private void Open_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is GenerationPlaceholderViewModel viewModel)
            viewModel.Timeline.ShowGeneration(viewModel.Job);
    }

    private void Cancel_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is GenerationPlaceholderViewModel viewModel)
            viewModel.Timeline.GenerationService?.Cancel(viewModel.Job);
    }

    private void Discard_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is GenerationPlaceholderViewModel viewModel)
            viewModel.Timeline.GenerationService?.Remove(viewModel.Job);
    }
}
