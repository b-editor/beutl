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

using BEditor.Data;
using BEditor.Models;
using BEditor.Plugin;
using BEditor.Properties;
using BEditor.ViewModels;
using BEditor.ViewModels.DialogContent;
using BEditor.Views.DialogContent;
using BEditor.Views.Dialogs;
using BEditor.Views.ManagePlugins;
using BEditor.Views.Settings;
using BEditor.Views.Tool;

namespace BEditor.Views.CustomTitlebars
{
    public sealed class WindowsTitlebar : UserControl
    {
        private readonly MenuItem _recentFiles;
        private readonly WindowsTitlebarButtons _titlebarbuttons;

        public WindowsTitlebar()
        {
            InitializeComponent();
            _recentFiles = this.FindControl<MenuItem>("RecentFiles");
            _titlebarbuttons = this.FindControl<WindowsTitlebarButtons>("titlebarbuttons");

            SetRecentUsedFiles();
            if (!OperatingSystem.IsWindows())
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
                    AppModel.Current.AppStatus = Status.Idle;
                    AppModel.Current.Project = null;
                    Debug.Fail(string.Empty);
                    AppModel.Current.Message.Snackbar(
                        string.Format(Strings.FailedToLoad, Strings.ProjectFile),
                        string.Empty,
                        IMessage.IconType.Error);
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

        public async void PackingProject(object s, RoutedEventArgs e)
        {
            if (VisualRoot is Window window)
            {
                var dialog = new CreateProjectPackage();
                await dialog.ShowDialog(window);
            }
        }

        public void ResetLayout(object s, RoutedEventArgs e)
        {
            if (VisualRoot is MainWindow window && window.Content is Grid grid)
            {
                grid.ColumnDefinitions = new("425,Auto,*,Auto,2*");
                grid.RowDefinitions = new("Auto,Auto,*,Auto,*,Auto");

                foreach (var item in System.IO.Directory.EnumerateFiles(WindowConfig.GetFolder())
                    .Where(i => System.IO.File.Exists(i)))
                {
                    System.IO.File.Delete(item);
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

        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
        }
    }
}