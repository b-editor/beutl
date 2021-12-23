using Avalonia.Controls;

using BEditorNext.ViewModels.Editors;

namespace BEditorNext.Views.Editors;

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

        ResourceReference<string> filterName = vm.Setter.Property.GetValueOrDefault<ResourceReference<string>>(PropertyMetaTableKeys.FilePickerName);
        string[]? exts = vm.Setter.Property.GetValueOrDefault<string[]>(PropertyMetaTableKeys.FilePickerExtensions);

        var dialog = new OpenFileDialog();

        if (filterName.Key != null && exts != null)
        {
            dialog.Filters.Add(new FileDialogFilter()
            {
                Name = filterName.FindOrDefault(),
                Extensions = exts.ToList()
            });
        }

        if (await dialog.ShowAsync(window) is string[] files && files.Length != 0)
        {
            vm.SetValue(vm.Setter.Value, new FileInfo(files[0]));
        }
    }
}
