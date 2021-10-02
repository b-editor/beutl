using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using Avalonia;
using Avalonia.Collections;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Markup.Xaml.Templates;
using Avalonia.Styling;
using Avalonia.Threading;

using BEditor.Data;
using BEditor.Models;
using BEditor.Plugin;
using BEditor.Properties;
using BEditor.ViewModels.DialogContent;
using BEditor.Views.DialogContent;

using FluentAvalonia.UI.Controls;

using static BEditor.IMessage;

namespace BEditor.Controls
{
    public sealed class ProjectTreeView : TreeView, IStyleable
    {
        private readonly FileSystemWatcher _watcher;
        private readonly Project _project;
        private readonly AvaloniaList<TreeViewItem> _items = new();
        private readonly DirectoryInfo _directoryInfo;
        private readonly MenuItem _open;
        private readonly MenuItem _copy;
        private readonly MenuItem _remove;
        private readonly MenuItem _addScene;
        private readonly MenuItem _addClip;
        private readonly MenuItem _addEffect;
        private readonly List<object> _menuItem;

        public ProjectTreeView(Project project, FileSystemWatcher watcher)
        {
            _project = project;
            _watcher = watcher;
            _directoryInfo = new DirectoryInfo(_project.DirectoryName);
            Items = _items;
            AddSubDirectory();

            _watcher.Renamed += Watcher_Renamed;
            _watcher.Deleted += Watcher_Deleted;
            _watcher.Created += Watcher_Created;

            _open = new MenuItem
            {
                Header = Strings.Open,
                Icon = new SymbolIcon
                {
                    Symbol = Symbol.Open,
                    FontSize = 20,
                }
            };
            _copy = new MenuItem
            {
                Header = Strings.Copy,
                Icon = new SymbolIcon
                {
                    Symbol = Symbol.Copy,
                    FontSize = 20,
                }
            };
            _remove = new MenuItem
            {
                Header = Strings.Remove,
                Icon = new SymbolIcon
                {
                    Symbol = Symbol.Delete,
                    FontSize = 20,
                }
            };
            _addScene = new MenuItem { Header = Strings.AddScene };
            _addClip = new MenuItem { Header = Strings.AddClip };
            _addEffect = new MenuItem { Header = Strings.AddEffect };

            _open.Click += Open;
            _copy.Click += Copy;
            _remove.Click += Remove;
            _addScene.Click += AddScene;
            _addClip.Click += AddClip;
            _addEffect.Click += AddEffect;

            _menuItem = new List<object>
            {
                _open,
                new MenuItem
                {
                    Header = Strings.Add,
                    Items = new object[]
                    {
                        _addScene,
                        _addClip,
                        _addEffect,
                    },
                },
                _copy,
                _remove,
            };

            _menuItem.Add(new Separator());

            foreach (var (asm, menus) in PluginManager.Default.FileMenus)
            {
                foreach (var menu in menus)
                {
                    var menuItem = new MenuItem
                    {
                        Header = menu.Name,
                        DataContext = menu,
                    };

                    menuItem.Click += PluginFileMenu_Click;

                    _menuItem.Add(menuItem);
                }
            }

            ContextMenu = new ContextMenu
            {
                Items = _menuItem
            };

            ContextMenu.ContextMenuOpening += ContextMenu_ContextMenuOpening;
        }

        private void PluginFileMenu_Click(object? sender, RoutedEventArgs e)
        {
            if (SelectedItem is FileTreeItem fileTree
                && sender is MenuItem menuItem
                && menuItem.DataContext is FileMenu fileMenu)
            {
                fileMenu.Execute(fileTree.Info.FullName);
            }
        }

        private void ContextMenu_ContextMenuOpening(object sender, System.ComponentModel.CancelEventArgs e)
        {
            _remove.IsEnabled = CanRemove();
            _open.IsEnabled = CanOpen();

            if (SelectedItem is FileTreeItem fileTree)
            {
                foreach (var (menu, model) in _menuItem.OfType<MenuItem>()
                    .Where(i => i.DataContext is FileMenu)
                    .Select(i => (Menu: i, Model: (FileMenu)i.DataContext!)))
                {
                    menu.IsVisible = model.IsMatch(fileTree.Info.FullName);
                }
            }
            else
            {
                foreach (var menu in _menuItem.OfType<MenuItem>()
                    .Where(i => i.DataContext is FileMenu))
                {
                    menu.IsVisible = false;
                }
            }
        }

        Type IStyleable.StyleKey => typeof(TreeView);

        private static IMessage Message => AppModel.Current.Message;

        private Scene? GetScene()
        {
            if (SelectedItem is IChild<object> obj) return obj.GetParent<Scene>();
            else return AppModel.Current.Project.CurrentScene;
        }

        private ClipElement? GetClip()
        {
            if (SelectedItem is IChild<object> obj) return obj.GetParent<ClipElement>();
            else return AppModel.Current.Project.CurrentScene.SelectItem;
        }

        private bool CanOpen()
        {
            return SelectedItem is DirectoryTreeItem or FileTreeItem;
        }

        private void Open(object? sender, RoutedEventArgs e)
        {
            if (SelectedItem is DirectoryTreeItem directoryTree)
            {
                directoryTree.IsExpanded = true;
            }
            else if (SelectedItem is FileTreeItem fileTree)
            {
                Process.Start(new ProcessStartInfo(fileTree.Info.FullName)
                {
                    UseShellExecute = true,
                });
            }
        }

        private async void Copy(object? sender, RoutedEventArgs e)
        {
            if (SelectedItem is IEditingObject obj)
            {
                await Application.Current.Clipboard.SetTextAsync(obj.Id.ToString());
            }
            else if (SelectedItem is DirectoryTreeItem directoryTree)
            {
                await Application.Current.Clipboard.SetTextAsync(directoryTree.Info.FullName);
            }
            else if (SelectedItem is FileTreeItem fileTree)
            {
                await Application.Current.Clipboard.SetTextAsync(fileTree.Info.FullName);
            }
        }

        private bool CanRemove()
        {
            return SelectedItem is EffectElement or ClipElement or Scene or DirectoryTreeItem or FileTreeItem;
        }

        private async void Remove(object? sender, RoutedEventArgs e)
        {
            if (SelectedItem is EffectElement effect)
            {
                RemoveEffect(effect);
            }
            else if (SelectedItem is ClipElement clip)
            {
                RemoveClip(clip);
            }
            else if (SelectedItem is Scene scene)
            {
                DeleteScene(scene);
            }
            else if (SelectedItem is DirectoryTreeItem directory)
            {
                if (await Message.DialogAsync(Strings.DoYouWantToRemoveThisDirectory, types: new ButtonType[] { ButtonType.Yes, ButtonType.No }) == ButtonType.Yes)
                {
                    directory.Info.Delete(true);
                }
            }
            else if (SelectedItem is FileTreeItem file)
            {
                if (await Message.DialogAsync(Strings.DoYouWantToRemoveThisFile, types: new ButtonType[] { ButtonType.Yes, ButtonType.No }) == ButtonType.Yes)
                {
                    file.Info.Delete();
                }
            }
        }

        private static async void DeleteScene(Scene scene)
        {
            try
            {
                if (scene is { Name: "root" })
                {
                    Message.Snackbar("RootScene は削除することができません", string.Empty);
                    return;
                }

                if (await Message.DialogAsync(
                    Strings.CommandQ1,
                    types: new ButtonType[] { ButtonType.Yes, ButtonType.No }) == ButtonType.Yes)
                {
                    scene.Parent!.CurrentScene = scene.Parent!.SceneList[0];
                    scene.Parent.SceneList.Remove(scene);
                    scene.Unload();

                    scene.ClearDisposable();
                }
            }
            catch (IndexOutOfRangeException)
            {
                Message.Snackbar(string.Format(Strings.ErrorObjectViewer1, nameof(Scene)), string.Empty, IconType.Error);
            }
        }

        private static void RemoveClip(ClipElement clip)
        {
            try
            {
                clip.Parent.RemoveClip(clip).Execute();
            }
            catch (IndexOutOfRangeException)
            {
                Message.Snackbar(string.Format(Strings.ErrorObjectViewer1, nameof(ClipElement)), string.Empty, IconType.Error);
            }
        }

        private static void RemoveEffect(EffectElement effect)
        {
            try
            {
                effect.Parent!.RemoveEffect(effect).Execute();
            }
            catch (IndexOutOfRangeException)
            {
                Message.Snackbar(string.Format(Strings.ErrorObjectViewer1, nameof(EffectElement)), string.Empty, IconType.Error);
            }
        }

        private async void AddScene(object? s, RoutedEventArgs e)
        {
            if (VisualRoot is Window window)
            {
                var dialog = new CreateScene { DataContext = new CreateSceneViewModel() };
                await dialog.ShowDialog(window);
            }
        }

        private async void AddClip(object? s, RoutedEventArgs e)
        {
            if (VisualRoot is Window window)
            {
                var vm = new CreateClipViewModel();
                var guess = GetScene();
                if (guess is not null) vm.Scene.Value = guess;

                var dialog = new CreateClip { DataContext = vm };
                await dialog.ShowDialog(window);
            }
        }

        private async void AddEffect(object? s, RoutedEventArgs e)
        {
            if (VisualRoot is Window window)
            {
                var vm = new AddEffectViewModel();
                var guess = GetClip();
                if (guess is not null) vm.ClipId.Value = guess.Id.ToString();

                var dialog = new AddEffect { DataContext = vm };
                await dialog.ShowDialog(window);
            }
        }

        private void Watcher_Created(object? sender, FileSystemEventArgs e)
        {
            Dispatcher.UIThread.InvokeAsync(() =>
            {
                var parent = Path.GetDirectoryName(e.FullPath);

                if (parent == _directoryInfo.FullName)
                {
                    if (Directory.Exists(e.FullPath))
                    {
                        var di = new DirectoryInfo(e.FullPath);
                        var last = _items.LastOrDefault(i => i is DirectoryTreeItem);
                        if (last != null)
                        {
                            var index = _items.IndexOf(last);
                            _items.Insert(index, new DirectoryTreeItem(di, _watcher));
                        }
                        else
                        {
                            _items.Add(new DirectoryTreeItem(di, _watcher));
                        }
                    }
                    else
                    {
                        _items.Add(new FileTreeItem(new FileInfo(e.FullPath)));
                    }
                }

                Sort();
            });
        }

        private void Watcher_Deleted(object? sender, FileSystemEventArgs e)
        {
            Dispatcher.UIThread.InvokeAsync(() =>
            {
                var parent = Path.GetDirectoryName(e.FullPath);
                var filename = Path.GetFileName(e.Name);

                if (parent == _directoryInfo.FullName)
                {
                    var item = _items.FirstOrDefault(i => i.Header is string str && str == filename);
                    if (item != null)
                    {
                        _items.Remove(item);
                    }
                }
            });
        }

        private void Watcher_Renamed(object? sender, RenamedEventArgs e)
        {
            Dispatcher.UIThread.InvokeAsync(() =>
            {
                var parent = Path.GetDirectoryName(e.FullPath);
                var oldFilename = Path.GetFileName(e.OldName);
                var newFilename = Path.GetFileName(e.Name);

                if (parent == _directoryInfo.FullName)
                {
                    var item = _items.FirstOrDefault(i => i.Header is string str && str == oldFilename);
                    if (item is DirectoryTreeItem dir)
                    {
                        dir.Info = new DirectoryInfo(e.FullPath);
                    }

                    if (item is FileTreeItem file)
                    {
                        file.Info = new FileInfo(e.FullPath);
                    }
                }

                Sort();
            });
        }

        //サブフォルダツリー追加
        public void AddSubDirectory()
        {
            //すべてのサブフォルダを追加
            foreach (var item in _directoryInfo.GetDirectories().Where(i => !i.Attributes.HasAnyFlag(FileAttributes.Hidden | FileAttributes.System)))
            {
                _items.Add(new DirectoryTreeItem(item, _watcher));
            }

            // 全てのファイル追加
            foreach (var item in _directoryInfo.GetFiles().Where(i => !i.Attributes.HasAnyFlag(FileAttributes.Hidden | FileAttributes.System)))
            {
                if (item.Name == $"{_project.Name}.bedit")
                {
                    _items.Add(new TreeViewItem
                    {
                        Items = _project.Children,
                        Header = new TextBlock { Text = item.Name },
                        DataContext = _project,
                        DataTemplates =
                        {
                            new TreeDataTemplate
                            {
                                ItemsSource = new Binding("Children") { TargetNullValue = Array.Empty<object>() },
                                Content = new Func<IServiceProvider, object>(_ =>
                                {
                                    return new ControlTemplateResult(new TextBlock
                                    {
                                        [TextBlock.TextProperty.Bind()] = new Binding("Name")
                                    }, this.FindNameScope());
                                }),
                            }
                        }
                    });
                }
                else
                {
                    _items.Add(new FileTreeItem(item));
                }
            }
        }

        public void Sort()
        {
            var fileArray = _items.Where(i => i is not DirectoryTreeItem).OrderBy(i =>
            {
                if (i.Header is string header)
                    return header;
                
                else if (i.Header is TextBlock tb)
                    return tb.Text;

                return i.Header.ToString();
            }).ToArray();
            var dirArray = _items.Where(i => i is DirectoryTreeItem).OrderBy(i =>
            {
                if (i.Header is string header)
                    return header;
                
                else if (i.Header is TextBlock tb)
                    return tb.Text;

                return i.Header.ToString();
            }).ToArray();
            _items.Clear();
            _items.AddRange(dirArray);
            _items.AddRange(fileArray);

            foreach (var item in dirArray.OfType<DirectoryTreeItem>())
            {
                item.Sort();
            }
        }
    }

    public sealed class FileTreeItem : TreeViewItem, IStyleable
    {
        private FileInfo _info;

        public FileTreeItem(FileInfo info)
        {
            _info = info;
            Header = Info.Name;
            DoubleTapped += FileTreeItem_DoubleTapped;
        }

        private void FileTreeItem_DoubleTapped(object? sender, RoutedEventArgs e)
        {
            Refresh();
            Process.Start(new ProcessStartInfo(Info.FullName)
            {
                UseShellExecute = true,
            });
        }

        public FileInfo Info
        {
            get => _info;
            set
            {
                _info = value;
                Header = _info.Name;
            }
        }

        Type IStyleable.StyleKey => typeof(TreeViewItem);

        protected override void OnPointerPressed(PointerPressedEventArgs e)
        {
            base.OnPointerPressed(e);
            if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            {
                var parent = this.FindLogicalAncestorOfType<TreeView>();
                parent.SelectedItem = this;
                Refresh();

                var dataObject = new DataObject();
                dataObject.Set(DataFormats.FileNames, new string[] { Info.FullName });

                // ドラッグ開始
                DragDrop.DoDragDrop(e, dataObject, DragDropEffects.Copy).ConfigureAwait(false);
            }
        }

        public void Refresh()
        {
            Info.Refresh();
            Header = Info.Name;
        }
    }

    public sealed class DirectoryTreeItem : TreeViewItem, IStyleable
    {
        private readonly AvaloniaList<TreeViewItem> _items = new();
        private readonly FileSystemWatcher _watcher;
        private bool _isAdd;//サブフォルダを作成済みかどうか
        private DirectoryInfo _info;

        public DirectoryTreeItem(DirectoryInfo info, FileSystemWatcher watcher)
        {
            _info = info;
            Header = info.Name;
            Items = _items;
            _watcher = watcher;

            this.GetObservable(IsExpandedProperty).Subscribe(_ =>
            {
                if (_isAdd) return;
                AddSubDirectory();
            });

            _watcher.Renamed += Watcher_Renamed;
            _watcher.Deleted += Watcher_Deleted;
            _watcher.Created += Watcher_Created;
        }

        public DirectoryInfo Info
        {
            get => _info;
            set
            {
                _info = value;
                Header = _info.Name;
            }
        }

        public void Refresh()
        {
            Info.Refresh();
            Header = Info.Name;
        }

        private void Watcher_Created(object? sender, FileSystemEventArgs e)
        {
            Dispatcher.UIThread.InvokeAsync(() =>
            {
                Refresh();
                var parent = Path.GetDirectoryName(e.FullPath);

                if (parent == Info.FullName)
                {
                    if (Directory.Exists(e.FullPath))
                    {
                        var di = new DirectoryInfo(e.FullPath);
                        var last = _items.LastOrDefault(i => i is DirectoryTreeItem);
                        if (last != null)
                        {
                            var index = _items.IndexOf(last);
                            _items.Insert(index, new DirectoryTreeItem(di, _watcher));
                        }
                        else
                        {
                            _items.Add(new DirectoryTreeItem(di, _watcher));
                        }
                    }
                    else
                    {
                        _items.Add(new FileTreeItem(new FileInfo(e.FullPath)));
                    }

                    Sort();
                }
            });
        }

        private void Watcher_Deleted(object? sender, FileSystemEventArgs e)
        {
            Dispatcher.UIThread.InvokeAsync(() =>
            {
                Refresh();
                var parent = Path.GetDirectoryName(e.FullPath);
                var filename = Path.GetFileName(e.Name);

                if (parent == Info.FullName)
                {
                    var item = _items.FirstOrDefault(i => i.Header is string str && str == filename);
                    if (item != null)
                    {
                        _items.Remove(item);
                    }
                }
            });
        }

        private void Watcher_Renamed(object? sender, RenamedEventArgs e)
        {
            Dispatcher.UIThread.InvokeAsync(() =>
            {
                Refresh();
                var parent = Path.GetDirectoryName(e.FullPath);
                var oldFilename = Path.GetFileName(e.OldName);
                var newFilename = Path.GetFileName(e.Name);

                if (parent == Info.FullName)
                {
                    var item = _items.FirstOrDefault(i => i.Header is string str && str == oldFilename);
                    if (item is DirectoryTreeItem dir)
                    {
                        dir.Info = new DirectoryInfo(e.FullPath);
                    }

                    if (item is FileTreeItem file)
                    {
                        file.Info = new FileInfo(e.FullPath);
                    }
                }

                Sort();
            });
        }

        Type IStyleable.StyleKey => typeof(TreeViewItem);

        //サブフォルダツリー追加
        public void AddSubDirectory()
        {
            Refresh();
            //すべてのサブフォルダを追加
            foreach (var item in Info.GetDirectories().Where(i => !i.Attributes.HasAnyFlag(FileAttributes.Hidden | FileAttributes.System)))
            {
                _items.Add(new DirectoryTreeItem(item, _watcher));
            }

            // 全てのファイル追加
            foreach (var item in Info.GetFiles().Where(i => !i.Attributes.HasAnyFlag(FileAttributes.Hidden | FileAttributes.System)))
            {
                _items.Add(new FileTreeItem(item));
            }

            _isAdd = true;
        }

        public void Sort()
        {
            var fileArray = _items.Where(i => i is not DirectoryTreeItem).OrderBy(i =>
            {
                if (i.Header is string header)
                    return header;

                else if (i.Header is TextBlock tb)
                    return tb.Text;

                return i.Header.ToString();
            }).ToArray();
            var dirArray = _items.Where(i => i is DirectoryTreeItem).OrderBy(i =>
            {
                if (i.Header is string header)
                    return header;

                else if (i.Header is TextBlock tb)
                    return tb.Text;

                return i.Header.ToString();
            }).ToArray();
            _items.Clear();
            _items.AddRange(dirArray);
            _items.AddRange(fileArray);

            foreach (var item in dirArray.OfType<DirectoryTreeItem>())
            {
                item.Sort();
            }
        }
    }
}