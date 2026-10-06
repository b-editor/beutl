using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Beutl.Language;
using Beutl.ViewModels.SettingsPages;

namespace Beutl.Pages.SettingsPages;

public sealed partial class AiAgentSettingsPage : UserControl
{
    public AiAgentSettingsPage()
    {
        InitializeComponent();
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

    private async void BrowseWorkspaceRoot_Click(object? sender, RoutedEventArgs e)
    {
        await PickFolderAsync(SettingsStrings.AiAgents_SelectWorkspaceRoot, path =>
        {
            if (DataContext is AiAgentSettingsPageViewModel vm)
            {
                vm.WorkspaceRoot.Value = path;
            }
        });
    }

    private void RefreshLiveMcp_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is AiAgentSettingsPageViewModel vm)
        {
            vm.RefreshLiveMcp.Execute();
        }
    }

    private async void CopyLiveMcpUrl_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is AiAgentSettingsPageViewModel vm)
        {
            await CopyToClipboardAsync(vm.LiveMcpUrl.Value);
        }
    }

    private async void CopyLiveMcpAuthHeader_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is AiAgentSettingsPageViewModel vm)
        {
            await CopyToClipboardAsync(vm.LiveMcpAuthHeader.Value);
        }
    }

    private async Task CopyToClipboardAsync(string? text)
    {
        if (text is { Length: > 0 }
            && TopLevel.GetTopLevel(this)?.Clipboard is { } clipboard)
        {
            await clipboard.SetTextAsync(text);
        }
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
