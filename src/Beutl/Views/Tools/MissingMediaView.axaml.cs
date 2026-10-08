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

    private void UseCandidateClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is MissingMediaViewModel vm && sender is Control { DataContext: MissingMediaRowViewModel row })
            vm.UseCandidate(row);
    }

    private void UseAllCandidatesClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is MissingMediaViewModel vm) vm.UseAllCandidates();
    }

    private void DismissErrorClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is MissingMediaViewModel vm) vm.Error.Value = null;
    }
}
