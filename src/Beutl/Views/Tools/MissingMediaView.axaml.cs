using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Beutl.Configuration;
using Beutl.Language;
using Beutl.ViewModels.Tools;

namespace Beutl.Views.Tools;

public partial class MissingMediaView : UserControl
{
    public MissingMediaView()
    {
        InitializeComponent();
    }

    private async Task<string?> PickFolderAsync()
    {
        if (TopLevel.GetTopLevel(this)?.StorageProvider is not { } storage) return null;
        var options = new FolderPickerOpenOptions { Title = MissingMediaStrings.FindInFolder };
        string? path = (DataContext as MissingMediaViewModel)?.SearchDirectory.Value
                       ?? GlobalConfiguration.Instance.EditorConfig.LastMediaDirectory;
        if (path != null && Directory.Exists(path))
            options.SuggestedStartLocation = await storage.TryGetFolderFromPathAsync(path);
        var result = await storage.OpenFolderPickerAsync(options);
        return result.FirstOrDefault()?.TryGetLocalPath();
    }

    private async void FindFolderClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is MissingMediaViewModel vm && await PickFolderAsync() is { } directory)
            await vm.FindInDirectoryAsync(directory);
    }

    private async void SearchClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MissingMediaViewModel vm) return;
        string? directory = vm.SearchDirectory.Value;
        if (string.IsNullOrWhiteSpace(directory)) directory = await PickFolderAsync();
        if (directory != null) await vm.FindInDirectoryAsync(directory);
    }

    private async void MatchFolderClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is MissingMediaViewModel vm && sender is Control { DataContext: MissingMediaRowViewModel row }
            && await PickFolderAsync() is { } directory)
            await vm.FindInDirectoryAsync(directory, row);
    }

    private async void ReplaceClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MissingMediaViewModel vm || sender is not Control { DataContext: MissingMediaRowViewModel row }
            || TopLevel.GetTopLevel(this)?.StorageProvider is not { } storage) return;
        var options = new FilePickerOpenOptions { Title = MissingMediaStrings.Replace, AllowMultiple = false };
        string? directory = GlobalConfiguration.Instance.EditorConfig.LastMediaDirectory;
        if (directory != null && Directory.Exists(directory))
            options.SuggestedStartLocation = await storage.TryGetFolderFromPathAsync(directory);
        var result = await storage.OpenFilePickerAsync(options);
        if (result.FirstOrDefault()?.TryGetLocalPath() is { } path)
            await vm.SetReplacementAsync(row, path);
    }

    private async void ApplyClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is MissingMediaViewModel vm) await vm.ApplyAsync();
    }

    private async void RefreshClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is MissingMediaViewModel vm) await vm.RefreshAsync();
    }

    private void DismissErrorClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is MissingMediaViewModel vm) vm.Error.Value = null;
    }
}
