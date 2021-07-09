using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;

using Avalonia;
using Avalonia.Collections;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;

using BEditor.Models;
using BEditor.Plugin;
using BEditor.Properties;
using BEditor.ViewModels;
using BEditor.ViewModels.DialogContent;
using BEditor.Views.DialogContent;
using BEditor.Views.ManagePlugins;
using BEditor.Views.Settings;
using BEditor.Views.Tool;

namespace BEditor.Views.CustomTitlebars
{
    public sealed class WindowsTitlebar : UserControl
    {
        private readonly Button _minimizeButton;
        private readonly Button _maximizeButton;
        private readonly Path _maximizeIcon;
        private readonly ToolTip _maximizeToolTip;
        private readonly Button _closeButton;
        private readonly Menu _menu;
        private readonly MenuItem _recentFiles;
        private readonly StackPanel _titlebarbuttons;

        public WindowsTitlebar()
        {
            InitializeComponent();
            _minimizeButton = this.FindControl<Button>("MinimizeButton");
            _maximizeButton = this.FindControl<Button>("MaximizeButton");
            _maximizeIcon = this.FindControl<Path>("MaximizeIcon");
            _maximizeToolTip = this.FindControl<ToolTip>("MaximizeToolTip");
            _closeButton = this.FindControl<Button>("CloseButton");
            _menu = this.FindControl<Menu>("menu");
            _recentFiles = this.FindControl<MenuItem>("RecentFiles");
            _titlebarbuttons = this.FindControl<StackPanel>("titlebarbuttons");

            if (OperatingSystem.IsWindows())
            {
                SetRecentUsedFiles();
                _minimizeButton.Click += MinimizeWindow;
                _maximizeButton.Click += MaximizeWindow;
                _closeButton.Click += CloseWindow;

                PointerPressed += WindowsTitlebar_PointerPressed;

                _menu.MenuOpened += (s, e) => PointerPressed -= WindowsTitlebar_PointerPressed;
                _menu.MenuClosed += (s, e) => PointerPressed += WindowsTitlebar_PointerPressed;
            }
            else
            {
                _titlebarbuttons.IsVisible = false;
            }
        }

        private void SetRecentUsedFiles()
        {
            static async Task ProjectOpenCommand(string name)
            {
                ProgressDialog? dialog = null;
                try
                {
                    dialog = new ProgressDialog
                    {
                        IsIndeterminate = { Value = true }
                    };
                    dialog.Show(App.GetMainWindow());

                    await MainWindowViewModel.DirectOpenAsync(name);
                }
                catch
                {
                    Debug.Fail(string.Empty);
                    AppModel.Current.Message.Snackbar(string.Format(Strings.FailedToLoad, Strings.ProjectFile));
                }
                finally
                {
                    dialog?.Close();
                }
            }

            var items = new AvaloniaList<MenuItem>(BEditor.Settings.Default.RecentFiles.Reverse().Select(i => new MenuItem
            {
                Header = i
            }));

            _recentFiles.Items = items;
            foreach (var item in items)
            {
                item.Click += async (s, e) => await ProjectOpenCommand(((s as MenuItem)!.Header as string)!);
            }

            BEditor.Settings.Default.RecentFiles.CollectionChanged += async (s, e) =>
            {
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    if (s is null) return;
                    if (e.Action is NotifyCollectionChangedAction.Add)
                    {
                        var menu = new MenuItem
                        {
                            Header = (s as ObservableCollection<string>)![e.NewStartingIndex]
                        };
                        menu.Click += async (s, e) => await ProjectOpenCommand(((s as MenuItem)!.Header as string)!);

                        ((AvaloniaList<MenuItem>)_recentFiles.Items).Insert(0, menu);
                    }
                    else if (e.Action is NotifyCollectionChangedAction.Remove)
                    {
                        var file = e.OldItems![0] as string;

                        foreach (var item in _recentFiles.Items)
                        {
                            if (item is MenuItem menuItem && menuItem.Header is string header && header == file)
                            {
                                ((AvaloniaList<MenuItem>)_recentFiles.Items).Remove(menuItem);

                                return;
                            }
                        }
                    }
                });
            };
        }

        public void InitializePluginMenu()
        {
            var menu = this.FindControl<MenuItem>("Plugins");

            if (menu.Items is AvaloniaList<object> list)
            {
                foreach (var (header, items) in PluginManager.Default._menus)
                {
                    var item = new MenuItem
                    {
                        Header = header,
                        Items = items.Select(i =>
                        {
                            var item = new MenuItem
                            {
                                Header = i.Name,
                                DataContext = i,
                            };
                            item.Click += (s, e) =>
                            {
                                if (s is MenuItem item && item.DataContext is ICustomMenu cmenu)
                                {
                                    cmenu.Execute();
                                }
                            };

                            return item;
                        }).ToArray()
                    };
                    list.Add(item);
                }
            }
        }

        public async void ConvertVideo(object s, RoutedEventArgs e)
        {
            if (VisualRoot is Window window)
            {
                var dialog = new ConvertVideo();
                await dialog.ShowDialog(window);
            }
        }

        public void ZoomIn(object s, RoutedEventArgs e)
        {
            if (VisualRoot is Window window)
            {
                var dialog = new ZoomWindow();
                dialog.Show(window);
            }
        }

        public async void ManagePlugins_Click(object s, RoutedEventArgs e)
        {
            if (VisualRoot is Window window)
            {
                await new ManagePluginsWindow().ShowDialog(window);
            }
        }

        public async void ShowInfomation(object s, RoutedEventArgs e)
        {
            if (VisualRoot is Window window)
            {
                await new Infomation().ShowDialog(window);
            }
        }

        public async void ShowSettings(object s, RoutedEventArgs e)
        {
            if (VisualRoot is Window window)
            {
                await new SettingsWindow().ShowDialog(window);
            }
        }

        public async void CreateScene(object s, RoutedEventArgs e)
        {
            if (VisualRoot is Window window)
            {
                var dialog = new CreateScene { DataContext = new CreateSceneViewModel() };
                await dialog.ShowDialog(window);
            }
        }

        public async void CreateClip(object s, RoutedEventArgs e)
        {
            if (VisualRoot is Window window)
            {
                var dialog = new CreateClip { DataContext = new CreateClipViewModel() };
                await dialog.ShowDialog(window);
            }
        }

        public async void AddEffect(object s, RoutedEventArgs e)
        {
            if (VisualRoot is Window window)
            {
                var dialog = new AddEffect { DataContext = new AddEffectViewModel() };
                await dialog.ShowDialog(window);
            }
        }

        public void WindowsTitlebar_PointerPressed(object? sender, Avalonia.Input.PointerPressedEventArgs e)
        {
            if (VisualRoot is Window window)
            {
                window.BeginMoveDrag(e);
            }
        }

        protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
        {
            base.OnAttachedToVisualTree(e);

            if (!OperatingSystem.IsWindows()) return;

            ((Window)e.Root).GetObservable(Window.WindowStateProperty).Subscribe(s =>
            {
                var window = (Window)e.Root;
                if (s is not WindowState.Maximized)
                {
                    _maximizeIcon.Data = Avalonia.Media.Geometry.Parse("M2048 2048v-2048h-2048v2048h2048zM1843 1843h-1638v-1638h1638v1638z");
                    window.Padding = new Thickness(0, 0, 0, 0);
                    _maximizeToolTip.Content = "Maximize";
                }
                if (s is WindowState.Maximized)
                {
                    _maximizeIcon.Data = Avalonia.Media.Geometry.Parse("M2048 1638h-410v410h-1638v-1638h410v-410h1638v1638zm-614-1024h-1229v1229h1229v-1229zm409-409h-1229v205h1024v1024h205v-1229z");
                    window.Padding = new Thickness(7, 7, 7, 7);
                    _maximizeToolTip.Content = "Restore Down";
                }
            });
        }

        private void CloseWindow(object? sender, RoutedEventArgs e)
        {
            if (VisualRoot is Window window)
            {
                window.Close();
            }
        }

        private void MaximizeWindow(object? sender, RoutedEventArgs e)
        {
            if (VisualRoot is Window window)
            {
                if (window.WindowState is WindowState.Normal)
                {
                    window.WindowState = WindowState.Maximized;
                }
                else
                {
                    window.WindowState = WindowState.Normal;
                }
            }
        }

        private void MinimizeWindow(object? sender, RoutedEventArgs e)
        {
            if (VisualRoot is Window window)
            {
                window.WindowState = WindowState.Minimized;
            }
        }

        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
        }
    }
}