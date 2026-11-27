using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Beutl.Editor.Components;
using Beutl.Language;
using Beutl.ViewModels.SettingsPages;

namespace Beutl.Pages.SettingsPages;

public sealed partial class AiAgentSettingsPage : UserControl
{
    public AiAgentSettingsPage()
    {
        InitializeComponent();
    }

    private async void BrowseClaudeExecutable_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is AiAgentSettingsPageViewModel vm && await PickExecutableAsync() is { } path && DataContext == vm)
            vm.RuntimeSettings.ClaudeCodeExecutablePath.Value = path;
    }

    private async void BrowseCodexExecutable_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is AiAgentSettingsPageViewModel vm && await PickExecutableAsync() is { } path && DataContext == vm)
            vm.RuntimeSettings.CodexExecutablePath.Value = path;
    }

    private async Task<string?> PickExecutableAsync()
    {
        if (TopLevel.GetTopLevel(this)?.StorageProvider is not { CanOpen: true } storage)
            return null;

        var files = await storage.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = SettingsStrings.AiAgents_RuntimeSelectExecutable,
            AllowMultiple = false,
        });
        using var ownership = SharedFilePickerOptions.OwnStorageFiles(files);
        return files.FirstOrDefault()?.TryGetLocalPath();
    }

    private void RemoveRuntimeProvider_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { DataContext: AgentCustomProviderItem item } && DataContext is AiAgentSettingsPageViewModel vm)
            vm.RuntimeSettings.RemoveProvider(item.Id);
    }

    private async void BrowseProjectRoot_Click(object? sender, RoutedEventArgs e)
    {
        await PickFolderAsync(SettingsStrings.AiAgents_SelectProjectFolder, path =>
        {
            if (DataContext is AiAgentSettingsPageViewModel vm)
            {
                vm.ProjectRoot.Value = path;
            }
        });
    }

    private async Task PickFolderAsync(string title, Action<string> setPath)
    {
        TopLevel? topLevel = TopLevel.GetTopLevel(this);
        if (topLevel is null)
        {
            return;
        }

        IReadOnlyList<IStorageFolder> folders = await topLevel.StorageProvider.OpenFolderPickerAsync(
            new FolderPickerOpenOptions
            {
                Title = title,
                AllowMultiple = false,
            });

        if (folders.Count > 0
            && folders[0].Path.LocalPath is { Length: > 0 } path)
        {
            setPath(path);
        }
    }
}
