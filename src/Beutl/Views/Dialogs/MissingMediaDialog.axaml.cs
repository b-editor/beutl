using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Beutl.Configuration;
using Beutl.Language;
using Beutl.ViewModels.Dialogs;

namespace Beutl.Views.Dialogs;

public partial class MissingMediaDialog : Window
{
    public MissingMediaDialog()
    {
        InitializeComponent();
        Closing += (_, args) =>
        {
            if (DataContext is MissingMediaDialogViewModel { CanClose: false }) args.Cancel = true;
        };
    }

    private async Task<string?> PickFolderAsync()
    {
        var options = new FolderPickerOpenOptions { Title = MissingMediaStrings.FindInFolder };
        string? path = (DataContext as MissingMediaDialogViewModel)?.SearchDirectory.Value
                       ?? GlobalConfiguration.Instance.EditorConfig.LastMediaDirectory;
        if (path != null && Directory.Exists(path))
            options.SuggestedStartLocation = await StorageProvider.TryGetFolderFromPathAsync(path);
        var result = await StorageProvider.OpenFolderPickerAsync(options);
        return result.FirstOrDefault()?.TryGetLocalPath();
    }

    private async void FindFolderClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is MissingMediaDialogViewModel vm && await PickFolderAsync() is { } directory)
            await vm.FindInDirectoryAsync(directory);
    }

    private async void SearchClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is MissingMediaDialogViewModel vm && !string.IsNullOrWhiteSpace(vm.SearchDirectory.Value))
            await vm.FindInDirectoryAsync(vm.SearchDirectory.Value);
    }

    private async void MatchFolderClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is MissingMediaDialogViewModel vm && sender is Button { DataContext: MissingMediaRowViewModel row }
            && await PickFolderAsync() is { } directory)
            await vm.FindInDirectoryAsync(directory, row);
    }

    private async void ReplaceClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MissingMediaDialogViewModel vm || sender is not Button { DataContext: MissingMediaRowViewModel row }) return;
        var options = new FilePickerOpenOptions { Title = MissingMediaStrings.Replace, AllowMultiple = false };
        string? directory = GlobalConfiguration.Instance.EditorConfig.LastMediaDirectory;
        if (directory != null && Directory.Exists(directory))
            options.SuggestedStartLocation = await StorageProvider.TryGetFolderFromPathAsync(directory);
        var result = await StorageProvider.OpenFilePickerAsync(options);
        if (result.FirstOrDefault()?.TryGetLocalPath() is { } path)
            await vm.SetReplacementAsync(row, path);
    }

    private async void ApplyClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is MissingMediaDialogViewModel vm && await vm.ApplyAsync()) Close();
    }

    private void ContinueOfflineClick(object? sender, RoutedEventArgs e) => Close();
}
