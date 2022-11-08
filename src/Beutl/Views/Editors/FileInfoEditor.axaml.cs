using Avalonia.Controls;
using Avalonia.Platform.Storage;

using Beutl.Validation;
using Beutl.ViewModels.Editors;

namespace Beutl.Views.Editors;

public sealed partial class FileInfoEditor : UserControl
{
    public FileInfoEditor()
    {
        InitializeComponent();
        button.Click += Button_Click;
    }

    private async void Button_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (DataContext is not FileInfoEditorViewModel vm || VisualRoot is not Window window) return;

        string filterName = vm.Header;
        string[]? patterns = vm.WrappedProperty.Property
            .GetMetadata<CorePropertyMetadata<FileInfo?>>(vm.WrappedProperty.ImplementedType)?
            .FindValidator<FileInfoExtensionValidator>()?
            .FileExtensions
            .Select(x => $"*.{x}")
            .ToArray();

        var options = new FilePickerOpenOptions
        {
            FileTypeFilter = new FilePickerFileType[]
            {
                new FilePickerFileType(filterName ?? "Unknown")
                {
                    Patterns = patterns
                }
            }
        };

        IReadOnlyList<IStorageFile> result = await window.StorageProvider.OpenFilePickerAsync(options);
        if (result.Count > 0
            && result[0].TryGetUri(out Uri? uri)
            && uri.IsFile)
        {
            vm.SetValue(vm.WrappedProperty.GetValue(), new FileInfo(uri.LocalPath));
        }
    }
}
