using Avalonia;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using Beutl.Controls.PropertyEditors;
using Beutl.Editor.Components.PropertyEditors.LutLibrary;
using Beutl.Editor.Components.PropertyEditors.ViewModels;
using Beutl.Media.Source;
using Beutl.Serialization;

namespace Beutl.Editor.Components.PropertyEditors.Views;

public partial class CubeSourceEditor : UserControl
{
    private LutLibraryViewModel? _library;
    private LutThumbnailCache? _thumbnails;

    public CubeSourceEditor()
    {
        InitializeComponent();

        FileEditor.OpenOptions = new FilePickerOpenOptions
        {
            FileTypeFilter = [new FilePickerFileType("LUT File") { Patterns = ["*.cube"] }]
        };
        FileEditor.ValueConfirmed += FileEditorOnValueConfirmed;
    }

    // Tests and hosts can point the library at a folder other than <BeutlHome>/luts.
    internal string? LibraryFolderPathOverride { get; set; }

    internal LutLibraryViewModel? LibraryViewModel => _library;

    internal LutLibraryView LibraryViewControl => LibraryView;

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);

        if (_library != null)
        {
            _library.ItemChosen -= LibraryItemChosen;
            _library.Dispose();
            _library = null;
        }

        _thumbnails?.Dispose();
        _thumbnails = null;
        LibraryView.DataContext = null;
    }

    private void FileEditorOnValueConfirmed(object? sender, PropertyEditorValueChangedEventArgs e)
    {
        if (DataContext is not CubeSourceEditorViewModel { IsDisposed: false } vm) return;
        if (e.NewValue is not FileInfo fi) return;

        CubeSource? oldValue = vm.PropertyAdapter.GetValue();
        var newValue = new CubeSource();
        newValue.ReadFrom(UriHelper.CreateFromPath(fi.FullName));
        vm.SetValueAndCommit(oldValue, newValue);
    }

    private void LibraryFlyout_Opened(object? sender, EventArgs e)
    {
        EnsureLibrary();
        _ = _library!.LoadAsync();
    }

    private void EnsureLibrary()
    {
        if (_library != null)
            return;

        string folder = LibraryFolderPathOverride is { Length: > 0 } overridePath
            ? overridePath
            : Path.Combine(BeutlEnvironment.GetHomeDirectoryPath(), "luts");
        _thumbnails = new LutThumbnailCache();
        _library = new LutLibraryViewModel(new LutLibraryStore(folder), _thumbnails, CanApplyLibraryItem);
        _library.ItemChosen += LibraryItemChosen;
        LibraryView.DataContext = _library;
    }

    private bool CanApplyLibraryItem()
        => DataContext is CubeSourceEditorViewModel { IsDisposed: false } vm && vm.CanEdit.Value;

    private void LibraryItemChosen(object? sender, LutLibraryItemViewModel item)
    {
        if (DataContext is not CubeSourceEditorViewModel { IsDisposed: false } vm) return;
        if (!vm.CanEdit.Value) return;

        CubeSource? oldValue = vm.PropertyAdapter.GetValue();
        var newValue = new CubeSource();
        newValue.ReadFrom(UriHelper.CreateFromPath(item.Path));
        vm.SetValueAndCommit(oldValue, newValue);
        LibraryButton.Flyout?.Hide();
    }
}
