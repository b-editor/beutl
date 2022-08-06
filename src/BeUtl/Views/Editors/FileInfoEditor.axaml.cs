using Avalonia.Controls;

using BeUtl.Validation;
using BeUtl.ViewModels.Editors;

namespace BeUtl.Views.Editors;

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

        string? filterName = vm.Header.Value;
        string[] exts = vm.WrappedProperty
            .GetMetadataExt<CorePropertyMetadata<FileInfo?>>()?
            .FindValidator<FileInfoExtensionValidator>()?
            .FileExtensions ?? Array.Empty<string>();

        var dialog = new OpenFileDialog();

        if (exts.Length > 0)
        {
            dialog.Filters ??= new();
            dialog.Filters.Add(new FileDialogFilter()
            {
                Name = filterName,
                Extensions = exts.ToList()
            });
        }

        if (await dialog.ShowAsync(window) is string[] files && files.Length > 0)
        {
            vm.SetValue(vm.WrappedProperty.GetValue(), new FileInfo(files[0]));
        }
    }
}
