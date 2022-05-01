using System.Collections;
using System.Collections.Specialized;

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
using Avalonia.Markup.Xaml.Templates;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;

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

        var dataTemplate = new MainViewDataTemplate();
        NaviContent.ContentTemplate = dataTemplate;
        NaviContent.PageTransition = new CustomPageTransition();

        // NavigationView‚ÌÝ’è
        Navi.SelectedItem = EditPageItem;
        Navi.ItemInvoked += NavigationView_ItemInvoked;

        NaviContent.Content = EditPageItem.Tag;

        _editPage = (EditPage)dataTemplate._maps[typeof(EditPageViewModel)];
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

                // Todo: Œã‚ÅŠg’£Žq‚ð•ÏX
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
                    // Todo: Œã‚ÅŠg’£Žq‚ð•ÏX
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
                        viewModel.EditPage.SelectOrAddTabItem(file);
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

    private sealed class MainViewDataTemplate : IDataTemplate
    {
        internal readonly Dictionary<Type, IControl> _maps = new()
        {
            [typeof(EditPageViewModel)] = new EditPage(),
            [typeof(SettingsPageViewModel)] = new SettingsPage(),
        };

        public IControl Build(object param)
        {
            if (_maps.TryGetValue(param.GetType(), out IControl? value))
            {
                return value;
            }
            else
            {
                throw new NotSupportedException();
            }
        }

        public bool Match(object data)
        {
            return data != null && _maps.ContainsKey(data.GetType());
        }
    }
}
