using System.Collections.Immutable;

using Avalonia.Controls;

using BeUtl.ProjectSystem;
using BeUtl.ViewModels.Editors;

namespace BeUtl.Views.Editors;

public partial class FileInfoEditor : UserControl
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
        ImmutableArray<string> exts = vm.WrappedProperty.GetMetadataExt<FilePropertyMetadata>().Extensions;

        var dialog = new OpenFileDialog();

        if (!exts.IsDefaultOrEmpty)
        {
            dialog.Filters ??= new();
            dialog.Filters.Add(new FileDialogFilter()
            {
                Name = filterName,
                Extensions = exts.ToList()
            });
        }

        if (await dialog.ShowAsync(window) is string[] files && files.Length != 0)
        {
            vm.SetValue(vm.WrappedProperty.GetValue(), new FileInfo(files[0]));
        }
    }
}
