using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Beutl.Controls.PropertyEditors;
using Beutl.Editor.Components.PropertyEditors.LutLibrary;
using Beutl.Editor.Components.PropertyEditors.ViewModels;
using Beutl.Media.Source;
using Beutl.Serialization;

namespace Beutl.Editor.Components.PropertyEditors.Views;

public partial class CubeSourceEditor : UserControl
{
    private LutLibraryPickerViewModel? _picker;
    private LutThumbnailCache? _thumbnails;
    private string? _libraryFolderPathOverride;

    public CubeSourceEditor()
    {
        InitializeComponent();

        FileEditor.OpenOptions = new FilePickerOpenOptions
        {
            FileTypeFilter = [new FilePickerFileType("LUT File") { Patterns = ["*.cube"] }]
        };
        FileEditor.ValueConfirmed += FileEditorOnValueConfirmed;
    }

    // Tests and hosts can point the library at a folder other than <BeutlHome>/luts before it is first used.
    internal string? LibraryFolderPathOverride
    {
        get => _libraryFolderPathOverride
            ?? (DataContext as CubeSourceEditorViewModel)?.LibraryFolderPathOverride;
        set
        {
            _libraryFolderPathOverride = value;
            if (DataContext is CubeSourceEditorViewModel viewModel)
            {
                viewModel.LibraryFolderPathOverride = value;
            }
        }
    }

    internal LutLibraryPickerViewModel? PickerViewModel => _picker;

    internal bool IsPickerOpen { get; private set; }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);

        _picker?.Dispose();
        _picker = null;
        _thumbnails?.Dispose();
        _thumbnails = null;
    }

    private void FileEditorOnValueConfirmed(object? sender, PropertyEditorValueChangedEventArgs e)
    {
        if (e.NewValue is not FileInfo file)
            return;

        SetCubeSource(file.FullName);
    }

    private async void LibraryButton_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not CubeSourceEditorViewModel { IsDisposed: false } viewModel
            || !viewModel.CanEdit.Value)
        {
            return;
        }

        _picker?.Dispose();
        var picker = new LutLibraryPickerViewModel(
            GetLibraryStore(viewModel), _thumbnails ??= new LutThumbnailCache());
        _picker = picker;
        _ = picker.LoadAsync();

        PinnableLibraryItem? item;
        IsPickerOpen = true;
        try
        {
            item = await PickerGuard.RunAsync<PinnableLibraryItem>(
                this,
                sender as Control,
                () => LibraryItemPickerHelper.ShowItemAsync(this, picker));
        }
        finally
        {
            IsPickerOpen = false;
            picker.CancelThumbnailWork();
        }

        if (item?.UserData is LutLibraryPickerItem { IsError: false } picked)
        {
            SetCubeSource(picked.Path);
        }
    }

    // The file picker and the library both write the same kind of source, so a pick is one undo step either way.
    private void SetCubeSource(string path)
    {
        if (DataContext is not CubeSourceEditorViewModel { IsDisposed: false } viewModel
            || !viewModel.CanEdit.Value)
        {
            return;
        }

        CubeSource? oldValue = viewModel.PropertyAdapter.GetValue();
        var newValue = new CubeSource();
        newValue.ReadFrom(UriHelper.CreateFromPath(path));
        viewModel.SetValueAndCommit(oldValue, newValue);
    }

    private LutLibraryStore GetLibraryStore(CubeSourceEditorViewModel viewModel)
    {
        if (_libraryFolderPathOverride is { Length: > 0 } path)
        {
            viewModel.LibraryFolderPathOverride ??= path;
        }

        return viewModel.GetLibraryStore();
    }
}
