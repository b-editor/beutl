using System.Collections;
using System.Collections.Specialized;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json.Nodes;

using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Collections;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Controls.Primitives;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.Xaml.Interactivity;

using Beutl.Controls;
using Beutl.Extensibility;
using Beutl.Extensibility.Services;
using Beutl.Models;
using Beutl.Pages;
using Beutl.ProjectSystem;
using Beutl.Services;
using Beutl.Utilities;
using Beutl.ViewModels;
using Beutl.ViewModels.Dialogs;
using Beutl.Views.Dialogs;

using DynamicData;

using FluentAvalonia.UI.Controls;
using FluentAvalonia.UI.Windowing;

using Microsoft.Extensions.DependencyInjection;

using Reactive.Bindings;
using Reactive.Bindings.Extensions;

namespace Beutl.Views;

internal readonly struct Cache<T>
    where T : class
{
    public readonly T?[] Items;

    public Cache(int size)
    {
        Items = new T?[size];
    }

    public bool Set(T item)
    {
        foreach (ref T? item0 in Items.AsSpan())
        {
            if (item0 == null)
            {
                item0 = item;
                return true;
            }
        }

        return false;
    }

    public T? Get()
    {
        foreach (ref T? item in Items.AsSpan())
        {
            if (item != null)
            {
                T? tmp = item;
                item = null;
                return tmp;
            }
        }

        return null;
    }
}

public sealed partial class MainView : UserControl
{
    private static readonly Binding s_headerBinding = new("Context.Header");
    private readonly AvaloniaList<MenuItem> _rawRecentFileItems = new();
    private readonly AvaloniaList<MenuItem> _rawRecentProjItems = new();
    private readonly Cache<MenuItem> _menuItemCache = new(4);
    private readonly CompositeDisposable _disposables = new();
    private readonly AvaloniaList<NavigationViewItem> _navigationItems = new();
    private readonly EditorService _editorService = ServiceLocator.Current.GetRequiredService<EditorService>();
    private readonly IProjectService _projectService = ServiceLocator.Current.GetRequiredService<IProjectService>();
    private readonly INotificationService _notificationService = ServiceLocator.Current.GetRequiredService<INotificationService>();
    private readonly IProjectItemContainer _projectItemContainer = ServiceLocator.Current.GetRequiredService<IProjectItemContainer>();
    private readonly Avalonia.Animation.Animation _animation = new()
    {
        Easing = new SplineEasing(0.1, 0.9, 0.2, 1.0),
        Children =
        {
            new KeyFrame
            {
                Setters =
                {
                    new Setter(OpacityProperty, 0.0),
                    new Setter(TranslateTransform.YProperty, 28.0)
                },
                Cue = new Cue(0d)
            },
            new KeyFrame
            {
                Setters =
                {
                    new Setter(OpacityProperty, 1.0),
                    new Setter(TranslateTransform.YProperty, 0.0)
                },
                Cue = new Cue(1d)
            }
        },
        Duration = TimeSpan.FromSeconds(0.67),
        FillMode = FillMode.Forward
    };
    private readonly TaskCompletionSource _windowOpenedTask = new();
    private Control? _settingsView;

    public MainView()
    {
        InitializeComponent();

        // NavigationViewの設定
        Navi.MenuItemsSource = _navigationItems;
        Navi.ItemInvoked += NavigationView_ItemInvoked;

        recentFiles.ItemsSource = _rawRecentFileItems;
        recentProjects.ItemsSource = _rawRecentProjItems;
    }

    private async void SceneSettings_Click(object? sender, RoutedEventArgs e)
    {
        if (TryGetSelectedEditViewModel(out EditViewModel? viewModel))
        {
            var dialog = new SceneSettings()
            {
                DataContext = new SceneSettingsViewModel(viewModel.Scene)
            };
            await dialog.ShowAsync();
        }
    }

    protected override async void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        _disposables.Clear();
        if (DataContext is MainViewModel viewModel)
        {
            Task splachScreenTask = viewModel.RunSplachScreenTask(async items =>
            {
                await _windowOpenedTask.Task;
                return await Dispatcher.UIThread.InvokeAsync(async () =>
                {
                    var dialog = new ContentDialog
                    {
                        Title = Message.DoYouWantToLoadSideloadExtensions,
                        Content = new ListBox
                        {
                            ItemsSource = items.Select(x => x.Name).ToArray(),
                            SelectedIndex = 0
                        },
                        PrimaryButtonText = Strings.Yes,
                        CloseButtonText = Strings.No,
                    };

                    return await dialog.ShowAsync() == ContentDialogResult.Primary;
                });
            });
            InitPages(viewModel);
            InitCommands(viewModel);
            InitRecentItems(viewModel);

            await splachScreenTask;
            InitExtMenuItems(viewModel);
        }
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);

        if (e.Root is TopLevel b)
        {
            b.Opened += OnParentWindowOpened;
        }
    }

    private void OnParentWindowOpened(object? sender, EventArgs e)
    {
        _windowOpenedTask.SetResult();
        if (sender is TopLevel window)
        {
            window.Opened -= OnParentWindowOpened;
        }

        if (sender is AppWindow cw)
        {
            AppWindowTitleBar titleBar = cw.TitleBar;
            if (titleBar != null)
            {
                titleBar.ExtendsContentIntoTitleBar = true;

                Titlebar.Margin = new Thickness(0, 0, titleBar.LeftInset, 0);
                AppWindow.SetAllowInteractionInTitleBar(MenuBar, true);
            }
        }
    }

    private void NavigationView_ItemInvoked(object? sender, NavigationViewItemInvokedEventArgs e)
    {
        if (e.InvokedItemContainer.DataContext is MainViewModel.NavItemViewModel itemViewModel
            && DataContext is MainViewModel viewModel)
        {
            viewModel.SelectedPage.Value = itemViewModel;
        }
    }

    private bool TryGetSelectedEditViewModel([NotNullWhen(true)] out EditViewModel? viewModel)
    {
        if (_editorService.SelectedTabItem.Value?.Context.Value is EditViewModel editViewModel)
        {
            viewModel = editViewModel;
            return true;
        }
        else
        {
            viewModel = null;
            return false;
        }
    }

    private void InitPages(MainViewModel viewModel)
    {
        _settingsView = new Pages.SettingsPage
        {
            IsVisible = false,
            DataContext = viewModel.SettingsPage.Context
        };
        NaviContent.Children.Clear();
        NaviContent.Children.Add(_settingsView);
        _navigationItems.Clear();

        Control[] pageViews = viewModel.Pages.Select(CreateView).ToArray();
        NaviContent.Children.InsertRange(0, pageViews);

        NavigationViewItem[] navItems = viewModel.Pages.Select(item =>
        {
            return new NavigationViewItem()
            {
                Classes = { "SideNavigationViewItem" },
                DataContext = item,
                [!ContentProperty] = s_headerBinding,
                [Interaction.BehaviorsProperty] = new BehaviorCollection
                {
                    new NavItemHelper()
                    {
                        FilledIcon = item.Extension.GetFilledIcon(),
                        RegularIcon = item.Extension.GetRegularIcon(),
                    }
                }
            };
        }).ToArray();
        _navigationItems.InsertRange(0, navItems);

        viewModel.Pages.CollectionChanged += OnPagesCollectionChanged;
        _disposables.Add(Disposable.Create(viewModel.Pages, obj => obj.CollectionChanged -= OnPagesCollectionChanged));
        viewModel.SelectedPage.Subscribe(async obj =>
        {
            if (DataContext is MainViewModel viewModel)
            {
                int idx = obj == null ? -1 : viewModel.Pages.IndexOf(obj);

                Control? oldControl = null;
                for (int i = 0; i < NaviContent.Children.Count; i++)
                {
                    if (NaviContent.Children[i] is Control { IsVisible: true } control)
                    {
                        control.IsVisible = false;
                        oldControl = control;
                    }
                }

                Navi.SelectedItem = idx >= 0 ? _navigationItems[idx] : Navi.FooterMenuItems.Cast<object>().First();
                Control newControl = idx >= 0 ? NaviContent.Children[idx] : _settingsView;

                newControl.IsVisible = true;
                newControl.Opacity = 0;
                await _animation.RunAsync((Animatable)newControl);
                newControl.Opacity = 1;

                newControl.Focus();
            }
        }).AddTo(_disposables);
    }

    private static Control CreateView(MainViewModel.NavItemViewModel item)
    {
        Control? view = null;
        Exception? exception = null;
        try
        {
            view = item.Extension.CreateControl();
        }
        catch (Exception e)
        {
            exception = e;
        }

        view ??= new TextBlock()
        {
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
            Text = exception != null ? @$"
Error:
    {Message.CouldNotCreateInstanceOfView}
Message:
    {exception.Message}
StackTrace:
    {exception.StackTrace}
" : @$"
Error:
    {Message.CannotDisplayThisContext}
"
        };

        view.IsVisible = false;
        view.DataContext = item.Context;

        return view;
    }

    private void OnPagesCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        void Add(int index, IList items)
        {
            foreach (MainViewModel.NavItemViewModel item in items)
            {
                if (item != null)
                {
                    int idx = index++;
                    Control view = CreateView(item);

                    NaviContent.Children.Insert(idx, view);
                    _navigationItems.Insert(idx, new NavigationViewItem()
                    {
                        Classes = { "SideNavigationViewItem" },
                        DataContext = item,
                        [!ContentProperty] = s_headerBinding,
                        [Interaction.BehaviorsProperty] = new BehaviorCollection
                        {
                            new NavItemHelper()
                            {
                                FilledIcon = item.Extension.GetFilledIcon(),
                                RegularIcon = item.Extension.GetRegularIcon(),
                            }
                        }
                    });
                }
            }
        }

        void Remove(int index, IList items)
        {
            for (int i = items.Count - 1; i >= 0; --i)
            {
                var item = (MainViewModel.NavItemViewModel)items[i]!;
                if (item != null)
                {
                    int idx = index + i;

                    (item.Context as IDisposable)?.Dispose();
                    NaviContent.Children.RemoveAt(idx);
                    _navigationItems.RemoveAt(idx);
                }
            }
        }

        switch (e.Action)
        {
            case NotifyCollectionChangedAction.Add:
                Add(e.NewStartingIndex, e.NewItems!);
                break;

            case NotifyCollectionChangedAction.Move:
            case NotifyCollectionChangedAction.Replace:
                Remove(e.OldStartingIndex, e.OldItems!);
                Add(e.NewStartingIndex, e.NewItems!);
                break;

            case NotifyCollectionChangedAction.Remove:
                Remove(e.OldStartingIndex, e.OldItems!);
                break;

            case NotifyCollectionChangedAction.Reset:
                throw new Exception("'MainViewModel.Pages' does not support the 'Clear' method.");
        }
    }

    private void InitCommands(MainViewModel viewModel)
    {
        viewModel.CreateNewProject.Subscribe(async () =>
        {
            var dialog = new CreateNewProject();
            await dialog.ShowAsync();
        }).AddTo(_disposables);

        viewModel.OpenProject.Subscribe(async () =>
        {
            if (VisualRoot is Window window)
            {
                var options = new FilePickerOpenOptions
                {
                    FileTypeFilter = new FilePickerFileType[]
                    {
                        new FilePickerFileType(Strings.ProjectFile)
                        {
                            Patterns = new[] { $"*.{Constants.ProjectFileExtension}" }
                        }
                    }
                };

                IReadOnlyList<IStorageFile> result = await window.StorageProvider.OpenFilePickerAsync(options);
                if (result.Count > 0
                    && result[0].TryGetLocalPath() is string localPath)
                {
                    _projectService.OpenProject(localPath);
                }
            }
        }).AddTo(_disposables);

        viewModel.OpenFile.Subscribe(async () =>
        {
            if (VisualRoot is not Window root || DataContext is not MainViewModel viewModel)
            {
                return;
            }

            var filters = new List<FilePickerFileType>();

            filters.AddRange(viewModel.GetEditorExtensions()
                .Select(e => e.GetFilePickerFileType())
                .ToArray());
            var options = new FilePickerOpenOptions
            {
                AllowMultiple = true,
                FileTypeFilter = filters
            };

            IReadOnlyList<IStorageFile> files = await root.StorageProvider.OpenFilePickerAsync(options);
            if (files.Count > 0)
            {
                bool? addToProject = null;
                Project? project = _projectService.CurrentProject.Value;

                foreach (IStorageFile file in files)
                {
                    if (file.TryGetLocalPath() is string path)
                    {
                        if (project != null && _projectItemContainer.TryGetOrCreateItem(path, out ProjectItem? item))
                        {
                            if (!addToProject.HasValue)
                            {
                                var checkBox = new CheckBox
                                {
                                    IsChecked = false,
                                    Content = Message.RememberThisChoice
                                };
                                var contentDialog = new ContentDialog
                                {
                                    PrimaryButtonText = Strings.Yes,
                                    CloseButtonText = Strings.No,
                                    DefaultButton = ContentDialogButton.Primary,
                                    Content = new StackPanel
                                    {
                                        Children =
                                        {
                                            new TextBlock
                                            {
                                                Text = Message.DoYouWantToAddThisItemToCurrentProject + "\n" + Path.GetFileName(path)
                                            },
                                            checkBox
                                        }
                                    }
                                };

                                ContentDialogResult result = await contentDialog.ShowAsync();
                                // 選択を記憶する
                                if (checkBox.IsChecked.Value)
                                {
                                    addToProject = result == ContentDialogResult.Primary;
                                }

                                if (result == ContentDialogResult.Primary)
                                {
                                    project.Items.Add(item);
                                    _editorService.ActivateTabItem(path, TabOpenMode.FromProject);
                                }
                            }
                            else if (addToProject.Value)
                            {
                                project.Items.Add(item);
                                _editorService.ActivateTabItem(path, TabOpenMode.FromProject);
                            }
                        }

                        _editorService.ActivateTabItem(path, TabOpenMode.YourSelf);
                    }
                }
            }
        }).AddTo(_disposables);

        viewModel.AddToProject.Subscribe(() =>
        {
            Project? project = _projectService.CurrentProject.Value;
            EditorTabItem? selectedTabItem = _editorService.SelectedTabItem.Value;

            if (project != null && selectedTabItem != null)
            {
                string filePath = selectedTabItem.FilePath.Value;
                if (project.Items.Any(i => i.FileName == filePath))
                    return;

                if (_projectItemContainer.TryGetOrCreateItem(filePath, out ProjectItem? workspaceItem))
                {
                    project.Items.Add(workspaceItem);
                }
            }
        }).AddTo(_disposables);

        viewModel.RemoveFromProject.Subscribe(async () =>
        {
            Project? project = _projectService.CurrentProject.Value;
            EditorTabItem? selectedTabItem = _editorService.SelectedTabItem.Value;

            if (project != null && selectedTabItem != null)
            {
                string filePath = selectedTabItem.FilePath.Value;
                ProjectItem? wsItem = project.Items.FirstOrDefault(i => i.FileName == filePath);
                if (wsItem == null)
                    return;

                var dialog = new ContentDialog
                {
                    CloseButtonText = Strings.Cancel,
                    PrimaryButtonText = Strings.OK,
                    DefaultButton = ContentDialogButton.Primary,
                    Content = Message.DoYouWantToExcludeThisItemFromProject + "\n" + filePath
                };

                if (await dialog.ShowAsync() == ContentDialogResult.Primary)
                {
                    project.Items.Remove(wsItem);
                }
            }
        }).AddTo(_disposables);

        viewModel.NewScene.Subscribe(async () =>
        {
            var dialog = new CreateNewScene();
            await dialog.ShowAsync();
        }).AddTo(_disposables);

        viewModel.AddLayer.Subscribe(async () =>
        {
            if (TryGetSelectedEditViewModel(out EditViewModel? viewModel)
                && viewModel.FindToolTab<TimelineViewModel>() is TimelineViewModel timeline)
            {
                var dialog = new AddElementDialog
                {
                    DataContext = new AddElementDialogViewModel(viewModel.Scene,
                        new ElementDescription(timeline.ClickedFrame, TimeSpan.FromSeconds(5), timeline.ClickedLayer))
                };
                await dialog.ShowAsync();
            }
        }).AddTo(_disposables);

        viewModel.DeleteLayer.Subscribe(async () =>
        {
            if (TryGetSelectedEditViewModel(out EditViewModel? viewModel)
                && viewModel.Scene is Scene scene
                && viewModel.SelectedObject.Value is Element layer)
            {
                string name = Path.GetFileName(layer.FileName);
                var dialog = new ContentDialog
                {
                    CloseButtonText = Strings.Cancel,
                    PrimaryButtonText = Strings.OK,
                    DefaultButton = ContentDialogButton.Primary,
                    Content = Message.DoYouWantToDeleteThisFile + "\n" + name
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
        }).AddTo(_disposables);

        viewModel.ExcludeLayer.Subscribe(() =>
        {
            if (TryGetSelectedEditViewModel(out EditViewModel? viewModel)
                && viewModel.Scene is Scene scene
                && viewModel.SelectedObject.Value is Element layer)
            {
                scene.RemoveChild(layer).DoAndRecord(CommandRecorder.Default);
            }
        }).AddTo(_disposables);

        viewModel.CutLayer.Subscribe(async () =>
        {
            if (TopLevel.GetTopLevel(this) is { Clipboard: IClipboard clipboard }
                && TryGetSelectedEditViewModel(out EditViewModel? viewModel)
                && viewModel.Scene is Scene scene
                && viewModel.SelectedObject.Value is Element layer)
            {
                var jsonNode = new JsonObject();
                layer.WriteToJson(jsonNode);
                string json = jsonNode.ToJsonString(JsonHelper.SerializerOptions);
                var data = new DataObject();
                data.Set(DataFormats.Text, json);
                data.Set(Constants.Element, json);

                await clipboard.SetDataObjectAsync(data);
                scene.RemoveChild(layer).DoAndRecord(CommandRecorder.Default);
            }
        }).AddTo(_disposables);

        viewModel.CopyLayer.Subscribe(async () =>
        {
            if (TopLevel.GetTopLevel(this) is { Clipboard: IClipboard clipboard }
                && TryGetSelectedEditViewModel(out EditViewModel? viewModel)
                && viewModel.Scene is Scene scene
                && viewModel.SelectedObject.Value is Element layer)
            {
                var jsonNode = new JsonObject();
                layer.WriteToJson(jsonNode);
                string json = jsonNode.ToJsonString(JsonHelper.SerializerOptions);
                var data = new DataObject();
                data.Set(DataFormats.Text, json);
                data.Set(Constants.Element, json);

                await clipboard.SetDataObjectAsync(data);
            }
        }).AddTo(_disposables);

        viewModel.PasteLayer.Subscribe(() =>
        {
            if (TryGetSelectedEditViewModel(out EditViewModel? viewModel)
                && viewModel.FindToolTab<TimelineViewModel>() is TimelineViewModel timeline)
            {
                timeline.Paste.Execute();
            }
        }).AddTo(_disposables);

        viewModel.Exit.Subscribe(() =>
        {
            if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime applicationLifetime)
            {
                applicationLifetime.Shutdown();
            }
        }).AddTo(_disposables);
    }

    private void InitExtMenuItems(MainViewModel viewModel)
    {
        if (toolTabMenuItem.Items is not IList items1)
        {
            items1 = new AvaloniaList<object>();
            toolTabMenuItem.ItemsSource = items1;
        }

        // Todo: Extensionの実行時アンロードの実現時、
        //       ForEachItemメソッドを使うかeventにする
        foreach (ToolTabExtension item in viewModel.GetToolTabExtensions())
        {
            if (item.Header == null)
                continue;

            var menuItem = new MenuItem()
            {
                Header = item.Header,
                DataContext = item
            };

            menuItem.Click += (s, e) =>
            {
                if (_editorService.SelectedTabItem.Value?.Context.Value is IEditorContext editorContext
                    && s is MenuItem { DataContext: ToolTabExtension ext }
                    && ext.TryCreateContext(editorContext, out IToolContext? toolContext))
                {
                    bool result = editorContext.OpenToolTab(toolContext);
                    if (!result)
                    {
                        toolContext.Dispose();
                    }
                }
            };

            items1.Add(menuItem);
        }

        if (editorTabMenuItem.Items is not IList items2)
        {
            items2 = new AvaloniaList<object>();
            editorTabMenuItem.ItemsSource = items2;
        }

        viewMenuItem.SubmenuOpened += (s, e) =>
        {
            EditorTabItem? selectedTab = _editorService.SelectedTabItem.Value;
            if (selectedTab != null)
            {
                foreach (MenuItem item in items2.OfType<MenuItem>())
                {
                    if (item.DataContext is EditorExtension editorExtension)
                    {
                        item.IsVisible = editorExtension.IsSupported(selectedTab.FilePath.Value);
                    }
                }
            }
        };

        foreach (EditorExtension item in viewModel.GetEditorExtensions())
        {
            var menuItem = new MenuItem()
            {
                Header = item.DisplayName,
                DataContext = item,
                IsVisible = false,
                Icon = item.GetIcon()
            };

            menuItem.Click += async (s, e) =>
            {
                EditorTabItem? selectedTab = _editorService.SelectedTabItem.Value;
                if (s is MenuItem { DataContext: EditorExtension editorExtension } menuItem
                    && selectedTab != null)
                {
                    IKnownEditorCommands? commands = selectedTab.Commands.Value;
                    if (commands != null)
                    {
                        await commands.OnSave();
                    }

                    string file = selectedTab.FilePath.Value;
                    if (editorExtension.TryCreateContext(file, out IEditorContext? context))
                    {
                        selectedTab.Context.Value.Dispose();
                        selectedTab.Context.Value = context;
                    }
                    else
                    {
                        _notificationService.Show(new Notification(
                            Title: Message.ContextNotCreated,
                            Message: string.Format(
                                format: Message.CouldNotOpenFollowingFileWithExtension,
                                arg0: editorExtension.DisplayName,
                                arg1: selectedTab.FileName.Value)));
                    }
                }
            };

            items2.Add(menuItem);
        }
    }

    private void InitRecentItems(MainViewModel viewModel)
    {
        void AddItem(AvaloniaList<MenuItem> list, string item, ReactiveCommand<string> command)
        {
            MenuItem menuItem = _menuItemCache.Get() ?? new MenuItem();
            menuItem.Command = command;
            menuItem.CommandParameter = item;
            menuItem.Header = item;
            list.Add(menuItem);
        }

        void RemoveItem(AvaloniaList<MenuItem> list, string item)
        {
            for (int i = list.Count - 1; i >= 0; i--)
            {
                MenuItem menuItem = list[i];
                if (menuItem.Header is string header && header == item)
                {
                    list.Remove(menuItem);
                    _menuItemCache.Set(menuItem);
                }
            }
        }

        viewModel.RecentFileItems.ForEachItem(
            item => AddItem(_rawRecentFileItems, item, viewModel.OpenRecentFile),
            item => RemoveItem(_rawRecentFileItems, item),
            _rawRecentFileItems.Clear)
            .AddTo(_disposables);

        viewModel.RecentProjectItems.ForEachItem(
            item => AddItem(_rawRecentProjItems, item, viewModel.OpenRecentProject),
            item => RemoveItem(_rawRecentProjItems, item),
            _rawRecentProjItems.Clear)
            .AddTo(_disposables);
    }

    [Conditional("DEBUG")]
    private void GC_Collect_Click(object? sender, RoutedEventArgs e)
    {
        DateTime dateTime = DateTime.UtcNow;
        long totalBytes = GC.GetTotalMemory(false);
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        TimeSpan elapsed = DateTime.UtcNow - dateTime;

        long deltaBytes = GC.GetTotalMemory(false) - totalBytes;
        string str = StringFormats.ToHumanReadableSize(Math.Abs(deltaBytes));
        str = (deltaBytes >= 0 ? "+" : "-") + str;

        _notificationService.Show(new Notification(
            "結果",
            $"""
                    経過時間: {elapsed.TotalMilliseconds}ms
                    差: {str}
                    """));
    }
}
