using System.Collections.Specialized;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Data;
using Avalonia.Interactivity;
using Avalonia.VisualTree;

using BeUtl.Controls;
using BeUtl.Framework;
using BeUtl.ProjectSystem;
using BeUtl.Services;
using BeUtl.ViewModels;
using BeUtl.Views;
using BeUtl.Views.Dialogs;

using Microsoft.Extensions.DependencyInjection;

namespace BeUtl.Pages;

public sealed partial class EditPage : UserControl
{
    public EditPage()
    {
        InitializeComponent();

        ProjectService service = ServiceLocator.Current.GetRequiredService<ProjectService>();
        service.ProjectObservable.Subscribe(item => ProjectChanged(item.New, item.Old));
    }

    // 開いているプロジェクトが変更された
    private void ProjectChanged(Project? @new, Project? old)
    {
        // プロジェクトが開いた
        if (@new != null)
        {
            @new.Children.CollectionChanged += Project_Children_CollectionChanged;
            foreach (Element item in @new.Children)
            {
                AddTabItem(item);
            }
        }

        // プロジェクトが閉じた
        if (old != null)
        {
            old.Children.CollectionChanged -= Project_Children_CollectionChanged;
            foreach (Element item in old.Children)
            {
                CloseTabItem(item);
            }
        }
    }

    // Project.Childrenが変更された
    private void Project_Children_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.Action == NotifyCollectionChangedAction.Add &&
            e.NewItems != null)
        {
            foreach (Element item in e.NewItems.OfType<Element>())
            {
                AddTabItem(item);
            }
        }
        else if (e.Action == NotifyCollectionChangedAction.Remove &&
                 e.OldItems != null)
        {
            foreach (Element item in e.OldItems.OfType<Element>())
            {
                CloseTabItem(item);
            }
        }
    }

    // DataContextからタブを追加
    private void AddTabItem(Element element)
    {
        if (element is Scene scene)
        {
            var view = new EditView
            {
                DataContext = new EditViewModel(scene)
            };
            var tabItem = new DraggableTabItem
            {
                DataContext = scene,
                Content = view,
                [!HeaderedContentControl.HeaderProperty] = new Binding("Name")
            };

            tabview.AddTab(tabItem);
        }
    }

    // DataContextからタブを削除
    private void CloseTabItem(Element element)
    {
        foreach (DraggableTabItem item in tabview.Items.OfType<DraggableTabItem>()
            .Where(i => i.DataContext == element)
            .ToArray())
        {
            item.Close();
        }
    }

    // '開く'がクリックされた
    private async void OpenClick(object? sender, RoutedEventArgs e)
    {
        if (VisualRoot is not Window root ||
            DataContext is not EditPageViewModel vm ||
            vm.Project.Value == null)
        {
            return;
        }

        var dialog = new OpenFileDialog
        {
            AllowMultiple = true,
            Filters =
            {
                new FileDialogFilter
                {
                    Name = Application.Current?.FindResource("S.EditPage.SceneFile") as string,
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
                    scene.Restore(file);
                    vm.Project.Value.Children.Add(scene);
                }
            }
        }
    }

    // '新規作成'がクリックされた
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

    private void AddButtonClick(object? sender, RoutedEventArgs e)
    {
        if (Resources["AddButtonFlyout"] is MenuFlyout flyout)
        {
            Button btn = tabview.FindDescendantOfType<Button>();

            flyout.ShowAt(btn);
        }
    }
}
