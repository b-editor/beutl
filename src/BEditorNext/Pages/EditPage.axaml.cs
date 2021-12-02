using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.VisualTree;

using BEditorNext.ProjectItems;
using BEditorNext.ViewModels;
using BEditorNext.Views.Dialogs;

namespace BEditorNext.Pages;

public sealed partial class EditPage : UserControl
{
    public EditPage()
    {
        InitializeComponent();
    }

    private async void OpenClick(object? sender, RoutedEventArgs e)
    {
        if (VisualRoot is not Window root ||
            DataContext is not EditPageViewModel vm ||
            vm.Project.Value == null) return;

        var dialog = new OpenFileDialog
        {
            AllowMultiple = true,
            Filters =
            {
                new FileDialogFilter
                {
                    Name = Application.Current.FindResource("SceneFileString") as string,
                    Extensions =
                    {
                        "scene"
                    }
                }
            }
        };

        string[]? files = await dialog.ShowAsync(root);
        if (files != null)
        {
            foreach (string file in files)
            {
                if (File.Exists(file))
                {
                    var scene = new Scene();
                    scene.Save(file);
                    vm.Project.Value.Children.Add(scene);
                }
            }
        }
    }

    private async void NewClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not EditPageViewModel vm) return;

        if (vm.IsProjectOpened.Value)
        {
            var dialog = new CreateNewScene();
            await dialog.ShowAsync();
        }
        else
        {
            var dialog = new CreateNewProject();
            await dialog.ShowAsync();
        }
    }

    private void AddSceneClick(object? sender, RoutedEventArgs e)
    {
        if (Resources["AddButtonFlyout"] is MenuFlyout flyout)
        {
            Button btn = tabview.FindDescendantOfType<Button>();

            flyout.ShowAt(btn);
        }
    }
}
