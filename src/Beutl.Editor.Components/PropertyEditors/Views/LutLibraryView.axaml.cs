using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Beutl.Editor.Components.PropertyEditors.LutLibrary;

namespace Beutl.Editor.Components.PropertyEditors.Views;

public partial class LutLibraryView : UserControl
{
    public LutLibraryView()
    {
        InitializeComponent();
    }

    internal LutLibraryViewModel? ViewModel => DataContext as LutLibraryViewModel;

    private void Entry_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: LutLibraryItemViewModel item })
        {
            ViewModel?.Choose(item);
        }
    }

    private void Favorite_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: LutLibraryItemViewModel item })
        {
            ViewModel?.ToggleFavorite(item);
        }
    }

    private void RemoveFolder_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: LutLibraryFolderViewModel folder })
        {
            ViewModel?.RemoveFolder(folder.Path);
        }
    }

    private async void AddFolder_Click(object? sender, RoutedEventArgs e)
    {
        if (ViewModel is not { } viewModel)
            return;

        if (TopLevel.GetTopLevel(this)?.StorageProvider is not { } storage)
            return;

        IReadOnlyList<IStorageFolder> folders = await storage.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            AllowMultiple = false,
            Title = Strings.LutLibraryAddFolder
        });
        if (folders.Count > 0 && folders[0].TryGetLocalPath() is { } path)
        {
            viewModel.AddFolder(path);
        }
    }
}
