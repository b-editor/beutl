using System.Collections;
using System.Collections.Specialized;

using Avalonia;
using Avalonia.Collections;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Markup.Xaml.Templates;
using Avalonia.Threading;

using BeUtl.Configuration;
using BeUtl.Framework;
using BeUtl.Framework.Service;
using BeUtl.Framework.Services;
using BeUtl.Models;
using BeUtl.Pages;
using BeUtl.ProjectSystem;
using BeUtl.ViewModels;
using BeUtl.ViewModels.Dialogs;
using BeUtl.Views.Dialogs;

using FluentAvalonia.Core.ApplicationModel;
using FluentAvalonia.UI.Controls;

using Microsoft.Extensions.DependencyInjection;

using FATabViewItem = FluentAvalonia.UI.Controls.TabViewItem;
using PathIcon = Avalonia.Controls.PathIcon;

namespace BeUtl.Views;

public partial class MainView : UserControl
{
    private readonly EditPage _editPage;

    public MainView()
    {
        InitializeComponent();

        // NavigationViewの設定
        EditPageItem.Tag = _editPage = new EditPage();
        SettingsPageItem.Tag = new SettingsPage();

        Navi.SelectedItem = EditPageItem;
        Navi.ItemInvoked += NavigationView_ItemInvoked;

        NaviContent.Content = EditPageItem.Tag;

        _editPage.tabview.SelectionChanged += TabView_SelectionChanged;

        ViewConfig viewConfig = GlobalConfiguration.Instance.ViewConfig;
        var recentFileItems = new AvaloniaList<MenuItem>(viewConfig.RecentFiles.Select(i => new MenuItem
        {
            Header = i
        }));
        var recentProjectItems = new AvaloniaList<MenuItem>(viewConfig.RecentProjects.Select(i => new MenuItem
        {
            Header = i
        }));

        recentFiles.Items = recentFileItems;
        recentProjects.Items = recentProjectItems;
        foreach (MenuItem item in recentFileItems)
        {
            item.Click += (s, e) => _editPage.SelectOrAddTabItem((s as MenuItem)?.Header as string);
        }

        foreach (MenuItem item in recentProjectItems)
        {
            item.Click += (s, e) => TryOpenProject((s as MenuItem)?.Header as string);
        }

        viewConfig.RecentFiles.CollectionChanged += RecentFiles_CollectionChanged;
        viewConfig.RecentProjects.CollectionChanged += RecentProjects_CollectionChangedAsync;
    }

    private async void SceneSettings_Click(object? sender, RoutedEventArgs e)
    {
        if (_editPage.tabview.SelectedItem is FATabViewItem { Content: EditView { DataContext: EditViewModel viewModel } })
        {
            var dialog = new SceneSettings()
            {
                DataContext = new SceneSettingsViewModel(viewModel.Scene)
            };
            await dialog.ShowAsync();
        }
    }

    private async void RecentFiles_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            if (e.Action is NotifyCollectionChangedAction.Add)
            {
                var menu = new MenuItem
                {
                    Header = e.NewItems![0] as string
                };
                menu.Click += (s, e) => _editPage.SelectOrAddTabItem((s as MenuItem)?.Header as string);

                ((AvaloniaList<MenuItem>)recentFiles.Items).Insert(0, menu);
            }
            else if (e.Action is NotifyCollectionChangedAction.Remove)
            {
                string? file = e.OldItems![0] as string;

                foreach (object? item in recentFiles.Items)
                {
                    if (item is MenuItem menuItem && menuItem.Header is string header && header == file)
                    {
                        ((AvaloniaList<MenuItem>)recentFiles.Items).Remove(menuItem);

                        return;
                    }
                }
            }
            else if (e.Action is NotifyCollectionChangedAction.Reset)
            {
                ((AvaloniaList<MenuItem>)recentFiles.Items).Clear();
            }
        });
    }

    private static void TryOpenProject(string? file)
    {
        IProjectService service = ServiceLocator.Current.GetRequiredService<IProjectService>();
        INotificationService noticeService = ServiceLocator.Current.GetRequiredService<INotificationService>();

        if (!File.Exists(file))
        {
            // Todo: リソースに置き換え
            noticeService.Show(new Notification(
                Title: "",
                Message: "ファイルが存在しない"));
        }
        else if (service.OpenProject(file) == null)
        {
            // Todo: リソースに置き換え
            noticeService.Show(new Notification(
                Title: "",
                Message: "プロジェクトが開けなかった"));
        }
    }

    private async void RecentProjects_CollectionChangedAsync(object? sender, NotifyCollectionChangedEventArgs e)
    {
        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            if (e.Action is NotifyCollectionChangedAction.Add)
            {
                var menu = new MenuItem
                {
                    Header = e.NewItems![0] as string
                };
                menu.Click += (s, e) => TryOpenProject((s as MenuItem)?.Header as string);

                ((AvaloniaList<MenuItem>)recentProjects.Items).Insert(0, menu);
            }
            else if (e.Action is NotifyCollectionChangedAction.Remove)
            {
                string? file = e.OldItems![0] as string;

                foreach (object? item in recentProjects.Items)
                {
                    if (item is MenuItem menuItem && menuItem.Header is string header && header == file)
                    {
                        ((AvaloniaList<MenuItem>)recentProjects.Items).Remove(menuItem);

                        return;
                    }
                }
            }
            else if (e.Action is NotifyCollectionChangedAction.Reset)
            {
                ((AvaloniaList<MenuItem>)recentProjects.Items).Clear();
            }
        });
    }

    protected override async void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (DataContext is MainViewModel vm)
        {
            vm.CreateNewProject.Subscribe(async () =>
            {
                var dialog = new CreateNewProject();
                await dialog.ShowAsync();
            });

            vm.OpenProject.Subscribe(async () =>
            {
                IProjectService service = ServiceLocator.Current.GetRequiredService<IProjectService>();

                // Todo: 後で拡張子を変更
                var dialog = new OpenFileDialog
                {
                    Filters =
                    {
                        new FileDialogFilter
                        {
                            Name = Application.Current?.FindResource("S.Common.ProjectFile") as string,
                            Extensions =
                            {
                                "bep"
                            }
                        }
                    }
                };

                if (VisualRoot is Window window)
                {
                    string[]? files = await dialog.ShowAsync(window);
                    if ((files?.Any() ?? false) && File.Exists(files[0]))
                    {
                        service.OpenProject(files[0]);
                    }
                }
            });

            vm.OpenScene.Subscribe(async () =>
            {
                IProjectService service = ServiceLocator.Current.GetRequiredService<IProjectService>();
                Project? project = service.CurrentProject.Value;

                if (VisualRoot is Window window && project != null)
                {
                    // Todo: 後で拡張子を変更
                    var dialog = new OpenFileDialog
                    {
                        Filters =
                        {
                            new FileDialogFilter
                            {
                                Name = Application.Current?.FindResource("S.Common.SceneFile") as string,
                                Extensions =
                                {
                                    "scene"
                                }
                            }
                        }
                    };
                    string[]? files = await dialog.ShowAsync(window);
                    if ((files?.Any() ?? false) && File.Exists(files[0]))
                    {
                        var scene = new Scene();
                        scene.Restore(files[0]);
                        project.Children.Add(scene);
                    }
                }
            });

            vm.AddScene.Subscribe(async () =>
            {
                var dialog = new CreateNewScene();
                await dialog.ShowAsync();
            });

            vm.RemoveScene.Subscribe(async () =>
            {
                IProjectService service = ServiceLocator.Current.GetRequiredService<IProjectService>();
                Project? project = service.CurrentProject.Value;

                if (project != null
                    && _editPage.tabview.SelectedItem is FATabViewItem { Content: EditView { DataContext: EditViewModel viewModel } })
                {
                    string name = Path.GetFileName(viewModel.Scene.FileName);
                    var dialog = new ContentDialog
                    {
                        [!ContentDialog.CloseButtonTextProperty] = new DynamicResourceExtension("S.Common.Cancel"),
                        [!ContentDialog.PrimaryButtonTextProperty] = new DynamicResourceExtension("S.Common.OK"),
                        DefaultButton = ContentDialogButton.Primary,
                        Content = (Application.Current?.FindResource("S.Message.DoYouWantToExcludeThisSceneFromProject") as string ?? "") + "\n" + name
                    };

                    if (await dialog.ShowAsync() == ContentDialogResult.Primary)
                    {
                        project.Children.Remove(viewModel.Scene);
                    }
                }
            });

            vm.AddLayer.Subscribe(async () =>
            {
                if (_editPage.tabview.SelectedItem is FATabViewItem { Content: EditView { DataContext: EditViewModel viewModel } editView })
                {
                    var dialog = new AddLayer
                    {
                        DataContext = new AddLayerViewModel(viewModel.Scene,
                            new LayerDescription(editView.timeline._clickedFrame, TimeSpan.FromSeconds(5), editView.timeline._clickedLayer))
                    };
                    await dialog.ShowAsync();
                }
            });

            vm.DeleteLayer.Subscribe(async () =>
            {
                if (_editPage.tabview.SelectedItem is FATabViewItem { Content: EditView { DataContext: EditViewModel { Scene: { SelectedItem: { } layer } scene } } })
                {
                    string name = Path.GetFileName(layer.FileName);
                    var dialog = new ContentDialog
                    {
                        [!ContentDialog.CloseButtonTextProperty] = new DynamicResourceExtension("S.Common.Cancel"),
                        [!ContentDialog.PrimaryButtonTextProperty] = new DynamicResourceExtension("S.Common.OK"),
                        DefaultButton = ContentDialogButton.Primary,
                        Content = (Application.Current?.FindResource("S.Message.DoYouWantToDeleteThisFile") as string ?? "") + "\n" + name
                    };

                    if (await dialog.ShowAsync() == ContentDialogResult.Primary)
                    {
                        scene.RemoveChild(layer).Do();
                        if (File.Exists(layer.FileName))
                        {
                            File.Delete(layer.FileName);
                        }
                    }
                }
            });

            vm.ExcludeLayer.Subscribe(() =>
            {
                if (_editPage.tabview.SelectedItem is FATabViewItem { Content: EditView { DataContext: EditViewModel { Scene: { SelectedItem: { } layer } scene } } })
                {
                    scene.RemoveChild(layer).DoAndRecord(CommandRecorder.Default);
                }
            });

            vm.CutLayer.Subscribe(async () =>
            {
                if (_editPage.tabview.SelectedItem is FATabViewItem { Content: EditView { DataContext: EditViewModel { Scene: { SelectedItem: { } layer } scene } } })
                {
                    IClipboard? clipboard = Application.Current?.Clipboard;
                    if (clipboard != null)
                    {
                        string json = layer.ToJson().ToJsonString(JsonHelper.SerializerOptions);
                        var data = new DataObject();
                        data.Set(DataFormats.Text, json);
                        data.Set(BeUtlDataFormats.Layer, json);

                        await clipboard.SetDataObjectAsync(data);
                        scene.RemoveChild(layer).DoAndRecord(CommandRecorder.Default);
                    }
                }
            });

            vm.CopyLayer.Subscribe(async () =>
            {
                if (_editPage.tabview.SelectedItem is FATabViewItem { Content: EditView { DataContext: EditViewModel { Scene: { SelectedItem: { } layer } scene } } })
                {
                    IClipboard? clipboard = Application.Current?.Clipboard;
                    if (clipboard != null)
                    {
                        string json = layer.ToJson().ToJsonString(JsonHelper.SerializerOptions);
                        var data = new DataObject();
                        data.Set(DataFormats.Text, json);
                        data.Set(BeUtlDataFormats.Layer, json);

                        await clipboard.SetDataObjectAsync(data);
                    }
                }
            });

            vm.PasteLayer.Subscribe(() =>
            {
                if (_editPage.tabview.SelectedItem is FATabViewItem { Content: EditView { timeline.ViewModel.Paste: { } paste } })
                {
                    paste.Execute();
                }
            });

            vm.OpenFile.Subscribe(async () =>
            {
                if (VisualRoot is not Window root)
                {
                    return;
                }

                var dialog = new OpenFileDialog
                {
                    AllowMultiple = true,
                };

                dialog.Filters.AddRange(PackageManager.Instance.ExtensionProvider.AllExtensions
                    .OfType<EditorExtension>()
                    .Select(e => new FileDialogFilter()
                    {
                        Extensions = e.FileExtensions.ToList(),
                        Name = Application.Current?.FindResource(e.FileTypeName.Key) as string
                    }));

                string[]? files = await dialog.ShowAsync(root);
                if (files != null)
                {
                    foreach (string file in files)
                    {
                        _editPage.SelectOrAddTabItem(file);
                    }
                }
            });

            vm.SaveAll.Subscribe(async () =>
            {
                IProjectService service = ServiceLocator.Current.GetRequiredService<IProjectService>();

                Project? project = service.CurrentProject.Value;
                INotificationService nservice = ServiceLocator.Current.GetRequiredService<INotificationService>();
                int itemsCount = 0;

                try
                {
                    project?.Save(project.FileName);
                    itemsCount++;

                    foreach (FATabViewItem? item in _editPage.tabview.TabItems.OfType<FATabViewItem>())
                    {
                        if (item.Content is IEditor editor
                            && editor.Commands != null
                            && await editor.Commands.OnSave())
                        {
                            itemsCount++;
                        }
                    }

                    string message = new ResourceReference<string>("S.Message.ItemsSaved").FindOrDefault(string.Empty);
                    nservice.Show(new Notification(
                        string.Empty,
                        string.Format(message, itemsCount.ToString()),
                        NotificationType.Success));
                }
                catch
                {
                    string message = new ResourceReference<string>("S.Message.OperationCouldNotBeExecuted").FindOrDefault(string.Empty);
                    nservice.Show(new Notification(
                        string.Empty,
                        message,
                        NotificationType.Error));
                }
            });

            vm.Save.Subscribe(async () =>
            {
                if (_editPage.tabview.SelectedItem is FATabViewItem { Content: IEditor { Commands: { } } editor })
                {
                    INotificationService nservice = ServiceLocator.Current.GetRequiredService<INotificationService>();
                    try
                    {
                        bool result = await editor.Commands.OnSave();

                        if (result)
                        {
                            string message = new ResourceReference<string>("S.Message.ItemSaved").FindOrDefault("{0}");
                            nservice.Show(new Notification(
                                string.Empty,
                                string.Format(message, Path.GetFileName(editor.EdittingFile) ?? editor.EdittingFile),
                                NotificationType.Success));
                        }
                        else
                        {
                            string message = new ResourceReference<string>("S.Message.OperationCouldNotBeExecuted").FindOrDefault(string.Empty);
                            nservice.Show(new Notification(
                                string.Empty,
                                message,
                                NotificationType.Information));
                        }
                    }
                    catch
                    {
                        string message = new ResourceReference<string>("S.Message.OperationCouldNotBeExecuted").FindOrDefault(string.Empty);
                        nservice.Show(new Notification(
                            string.Empty,
                            message,
                            NotificationType.Error));
                    }
                }
            });

            vm.Exit.Subscribe(() =>
            {
                if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime applicationLifetime)
                {
                    applicationLifetime.Shutdown();
                }
            });

            await vm._packageLoadTask;

            PackageManager manager = PackageManager.Instance;
            if (viewMenu.Items is not IList items)
            {
                items = new AvaloniaList<object>();
                viewMenu.Items = items;
            }

            foreach (SceneEditorTabExtension item in manager.ExtensionProvider.AllExtensions.OfType<SceneEditorTabExtension>())
            {
                var menuItem = new MenuItem()
                {
                    [!HeaderedSelectingItemsControl.HeaderProperty] = new DynamicResourceExtension(item.Header.Key),
                    DataContext = item
                };

                if (item.Icon != null)
                {
                    menuItem.Icon = new PathIcon
                    {
                        Data = item.Icon,
                        Width = 18,
                        Height = 18,
                    };
                }

                menuItem.Click += (s, e) =>
                {
                    if (_editPage.tabview.SelectedItem is FATabViewItem { Content: EditView { DataContext: EditViewModel editViewModel } editView }
                        && s is MenuItem { DataContext: SceneEditorTabExtension ext })
                    {
                        ExtendedEditTabViewModel? tabViewModel = editViewModel.UsingExtensions.FirstOrDefault(i => i.Extension == ext);

                        if (tabViewModel != null)
                        {
                            tabViewModel.IsSelected.Value = true;
                        }
                        else
                        {
                            tabViewModel = new ExtendedEditTabViewModel(ext)
                            {
                                IsSelected =
                                {
                                    Value = true
                                }
                            };
                            editViewModel.UsingExtensions.Add(tabViewModel);
                        }
                    }
                };

                items.Add(menuItem);
            }
        }
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);

        if (e.Root is Window b)
        {
            b.Opened += OnParentWindowOpened;
        }
    }

    private void TabView_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (DataContext is MainViewModel viewModel && _editPage.tabview.SelectedItem is FATabViewItem { Content: IEditor editor })
        {
            viewModel.KnownCommands = editor.Commands;
        }
    }

    private void OnParentWindowOpened(object? sender, EventArgs e)
    {
        if (sender is Window window)
        {
            window.Opened -= OnParentWindowOpened;
        }

        if (sender is CoreWindow cw)
        {
            CoreApplicationViewTitleBar titleBar = cw.TitleBar;
            if (titleBar != null)
            {
                titleBar.ExtendViewIntoTitleBar = true;

                titleBar.LayoutMetricsChanged += OnApplicationTitleBarLayoutMetricsChanged;

                cw.SetTitleBar(Titlebar);
                Titlebar.Margin = new Thickness(0, 0, titleBar.SystemOverlayRightInset, 0);
            }
        }
    }

    private void OnApplicationTitleBarLayoutMetricsChanged(CoreApplicationViewTitleBar sender, object args)
    {
        Titlebar.Margin = new Thickness(0, 0, sender.SystemOverlayRightInset, 0);
    }

    private void NavigationView_ItemInvoked(object? sender, NavigationViewItemInvokedEventArgs e)
    {
        if (e.InvokedItemContainer is NavigationViewItem item)
        {
            NaviContent.Content = item.Tag;
            e.RecommendedNavigationTransitionInfo.RunAnimation(NaviContent);
        }
    }

    private sealed class MainViewDataTemplate : IDataTemplate
    {
        public IControl Build(object param)
        {
            return param switch
            {
                EditPageViewModel => new EditPage(),
                SettingsPageViewModel => new SettingsPage(),
                _ => throw new NotSupportedException(),
            };
        }

        public bool Match(object data)
        {
            return data is EditPageViewModel or SettingsPageViewModel;
        }
    }
}
