using System.Collections;
using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Reactive.Disposables;

using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Collections;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Media;
using Avalonia.Styling;

using BeUtl.Collections;
using BeUtl.Framework;
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

using Reactive.Bindings;
using Reactive.Bindings.Extensions;

using FATabViewItem = FluentAvalonia.UI.Controls.TabViewItem;
using PathIcon = Avalonia.Controls.PathIcon;

namespace BeUtl.Views;

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

public partial class MainView : UserControl
{
    private readonly AvaloniaList<MenuItem> _rawRecentFileItems = new();
    private readonly AvaloniaList<MenuItem> _rawRecentProjItems = new();
    private readonly Cache<MenuItem> _menuItemCache = new(4);
    private readonly CompositeDisposable _disposables = new();
    // 拡張機能対応の時にこれをサービス化する
    private readonly Dictionary<Type, Type> _contextToView = new()
    {
        [typeof(EditPageViewModel)] = typeof(EditPage),
        [typeof(SettingsPageViewModel)] = typeof(SettingsPage),
    };
    private readonly List<IControl> _controls = new();

    public MainView()
    {
        InitializeComponent();

        NaviContent.PageTransition = new CustomPageTransition();

        // NavigationViewの設定
        Navi.SelectedItem = EditPageItem;
        Navi.ItemInvoked += NavigationView_ItemInvoked;

        recentFiles.Items = _rawRecentFileItems;
        recentProjects.Items = _rawRecentProjItems;
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
            _controls.Clear();
            viewModel.Pages.ForEachItem(
                (idx, item) =>
                {
                    IControl? view = null;
                    Exception? exception = null;
                    if (item != null && _contextToView.TryGetValue(item.GetType(), out Type? viewType))
                    {
                        try
                        {
                            view = Activator.CreateInstance(viewType) as IControl;
                        }
                        catch (Exception e)
                        {
                            exception = e;
                        }
                    }

                    view ??= new TextBlock()
                    {
                        VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
                        HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
                        Text = exception != null ? @$"
Error:
    Viewのインスタンスを作成できませんでした。
Message:
    {exception.Message}
StackTrace:
    {exception.StackTrace}
" : @"
Error:
    このコンテキストを表示する拡張機能が見つかりません。
"
                    };

                    view.DataContext = item;

                    _controls.Insert(idx, view);
                },
                (idx, item) =>
                {
                    (item as IDisposable)?.Dispose();
                    _controls.RemoveAt(idx);
                },
                () => throw new Exception("'MainViewModel.Pages'は'Clear'メソッドに対応していません。"))
                .AddTo(_disposables);

            viewModel.SelectedPage.Subscribe(obj =>
            {
                if (DataContext is MainViewModel viewModel)
                {
                    int idx = viewModel.Pages.IndexOf(obj);
                    if (idx < 0) return;
                    NaviContent.Content = _controls[idx];
                }
            }).AddTo(_disposables);

            InitCommands(viewModel);

            await viewModel._packageLoadTask;
            InitExtMenuItems();

            InitRecentItems(viewModel);
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
        if (e.InvokedItemContainer is NavigationViewItem item && DataContext is MainViewModel viewModel)
        {
            viewModel.SelectedPage.Value = item.Tag;
        }
    }

    private bool TryGetSelectedEditViewModel([NotNullWhen(true)] out EditViewModel? viewModel)
    {
        if (DataContext is MainViewModel { EditPage.SelectedTabItem.Value.Context: EditViewModel editViewModel })
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

    private void InitCommands(MainViewModel viewModel)
    {
        viewModel.CreateNewProject.Subscribe(async () =>
        {
            var dialog = new CreateNewProject();
            await dialog.ShowAsync();
        }).AddTo(_disposables);

        viewModel.OpenProject.Subscribe(async () =>
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
        }).AddTo(_disposables);

        viewModel.OpenFile.Subscribe(async () =>
        {
            if (VisualRoot is not Window root || DataContext is not MainViewModel viewModel)
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
                    // Todo: プロジェクトに追加するかのダイアログを表示する
                    viewModel.EditPage.SelectOrAddTabItem(file, EditPageViewModel.TabOpenMode.YourSelf);
                }
            }
        }).AddTo(_disposables);

        viewModel.AddToProject.Subscribe(() =>
        {
            IProjectService service = ServiceLocator.Current.GetRequiredService<IProjectService>();
            Project? project = service.CurrentProject.Value;
            IWorkspaceItemContainer resolver = ServiceLocator.Current.GetRequiredService<IWorkspaceItemContainer>();

            if (project != null && DataContext is MainViewModel viewModel && viewModel.EditPage.SelectedTabItem.Value != null)
            {
                EditPageViewModel.TabViewModel tabViewModel = viewModel.EditPage.SelectedTabItem.Value;
                if (project.Items.Any(i => i.FileName == tabViewModel.FilePath))
                    return;

                if (resolver.TryGetOrCreateItem(tabViewModel.FilePath, out IWorkspaceItem? workspaceItem))
                {
                    project.Items.Add(workspaceItem);
                }
            }
        }).AddTo(_disposables);

        viewModel.RemoveFromProject.Subscribe(async () =>
        {
            IProjectService service = ServiceLocator.Current.GetRequiredService<IProjectService>();
            Project? project = service.CurrentProject.Value;

            if (project != null && DataContext is MainViewModel viewModel && viewModel.EditPage.SelectedTabItem.Value != null)
            {
                EditPageViewModel.TabViewModel tabViewModel = viewModel.EditPage.SelectedTabItem.Value;
                IWorkspaceItem? wsItem = project.Items.FirstOrDefault(i => i.FileName == tabViewModel.FilePath);
                if (wsItem == null)
                    return;

                var dialog = new ContentDialog
                {
                    [!ContentDialog.CloseButtonTextProperty] = new DynamicResourceExtension("S.Common.Cancel"),
                    [!ContentDialog.PrimaryButtonTextProperty] = new DynamicResourceExtension("S.Common.OK"),
                    DefaultButton = ContentDialogButton.Primary,
                    Content = (Application.Current?.FindResource("S.Message.DoYouWantToExcludeThisItemFromProject") as string ?? "") + "\n" + tabViewModel.FileName
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
            if (TryGetSelectedEditViewModel(out EditViewModel? viewModel))
            {
                var dialog = new AddLayer
                {
                    DataContext = new AddLayerViewModel(viewModel.Scene,
                        new LayerDescription(viewModel.Timeline.ClickedFrame, TimeSpan.FromSeconds(5), viewModel.Timeline.ClickedLayer))
                };
                await dialog.ShowAsync();
            }
        }).AddTo(_disposables);

        viewModel.DeleteLayer.Subscribe(async () =>
        {
            if (TryGetSelectedEditViewModel(out EditViewModel? viewModel)
                && viewModel.Scene is Scene scene
                && scene.SelectedItem is Layer layer)
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
        }).AddTo(_disposables);

        viewModel.ExcludeLayer.Subscribe(() =>
        {
            if (TryGetSelectedEditViewModel(out EditViewModel? viewModel)
                && viewModel.Scene is Scene scene
                && scene.SelectedItem is Layer layer)
            {
                scene.RemoveChild(layer).DoAndRecord(CommandRecorder.Default);
            }
        }).AddTo(_disposables);

        viewModel.CutLayer.Subscribe(async () =>
        {
            if (TryGetSelectedEditViewModel(out EditViewModel? viewModel)
                && viewModel.Scene is Scene scene
                && scene.SelectedItem is Layer layer)
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
        }).AddTo(_disposables);

        viewModel.CopyLayer.Subscribe(async () =>
        {
            if (TryGetSelectedEditViewModel(out EditViewModel? viewModel)
                && viewModel.Scene is Scene scene
                && scene.SelectedItem is Layer layer)
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
        }).AddTo(_disposables);

        viewModel.PasteLayer.Subscribe(() =>
        {
            if (TryGetSelectedEditViewModel(out EditViewModel? viewModel))
            {
                viewModel.Timeline.Paste.Execute();
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

    private void InitExtMenuItems()
    {
        PackageManager manager = PackageManager.Instance;
        if (viewMenu.Items is not IList items)
        {
            items = new AvaloniaList<object>();
            viewMenu.Items = items;
        }

        // Todo: Extensionの実行時アンロードの実現時、
        //       ForEachItemメソッドを使うかeventにする
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
                if (TryGetSelectedEditViewModel(out EditViewModel? editViewModel)
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
            () => _rawRecentFileItems.Clear())
            .AddTo(_disposables);

        viewModel.RecentProjectItems.ForEachItem(
            item => AddItem(_rawRecentProjItems, item, viewModel.OpenRecentProject),
            item => RemoveItem(_rawRecentProjItems, item),
            () => _rawRecentProjItems.Clear())
            .AddTo(_disposables);
    }

    private sealed class CustomPageTransition : IPageTransition
    {
        private readonly Avalonia.Animation.Animation _animation;

        public CustomPageTransition()
        {
            _animation = new Avalonia.Animation.Animation
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
        }

        public async Task Start(Visual from, Visual to, bool forward, CancellationToken cancellationToken)
        {
            if (to != null)
            {
                _animation.FillMode = forward ? FillMode.Forward : FillMode.Backward;
                await _animation.RunAsync(to, null, cancellationToken);

                to.Opacity = forward ? 1 : 0;
            }
        }
    }
}
