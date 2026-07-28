using Avalonia.Controls;
using Avalonia.Interactivity;
using Beutl.Editor.Components.VersionControlTab.ViewModels;
using Beutl.Extensibility;

namespace Beutl.Editor.Components.VersionControlTab.Views;

public sealed partial class VersionControlTabView : UserControl
{
    public VersionControlTabView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
        ConfigureCallbacks();
    }

    private async void OnCommitSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (DataContext is VersionControlTabViewModel viewModel
            && sender is ListBox listBox)
        {
            await viewModel.SelectCommitAsync(
                listBox.SelectedItem as VersionControlCommitViewModel);
        }
    }

    private async void OnChangedFileSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (DataContext is VersionControlTabViewModel viewModel
            && sender is ListBox listBox)
        {
            await viewModel.SelectFileAsync(
                listBox.SelectedItem as VersionControlFileChangeViewModel);
        }
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        ConfigureCallbacks();
    }

    private void ConfigureCallbacks()
    {
        if (DataContext is VersionControlTabViewModel viewModel)
        {
            viewModel.RequestEnableVersionControlAsync = ExecuteEnableVersionControlAsync;
            viewModel.LaunchUriAsync = LaunchUriAsync;
        }
    }

    private Task ExecuteEnableVersionControlAsync()
    {
        if (TopLevel.GetTopLevel(this)?.DataContext is IContextCommandHandler handler)
        {
            var execution = new ContextCommandExecution("EnableVersionControl");
            if (handler.CanExecute(execution))
            {
                handler.Execute(execution);
            }
        }

        return Task.CompletedTask;
    }

    private Task<bool> LaunchUriAsync(Uri uri)
    {
        return TopLevel.GetTopLevel(this)?.Launcher.LaunchUriAsync(uri)
               ?? Task.FromResult(false);
    }
}
