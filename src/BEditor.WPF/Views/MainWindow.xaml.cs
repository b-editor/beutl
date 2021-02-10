using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;

using BEditor.Core.Data;
using BEditor.Core.Extensions;
using BEditor.Core.Plugin;
using BEditor.Core.Service;
using BEditor.Models;
using BEditor.ViewModels;
using BEditor.ViewModels.CreatePage;
using BEditor.Views;
using BEditor.Views.CreatePage;

using MahApps.Metro.Controls;

using MaterialDesignThemes.Wpf;

using Reactive.Bindings;

using Expression = System.Linq.Expressions.Expression;

namespace BEditor
{
    /// <summary>
    /// MainWindow.xaml の相互作用ロジック
    /// </summary>
    public partial class MainWindow : MetroWindow
    {
        enum ShowHideState : byte
        {
            Show,
            Hide
        }
        private static readonly Func<PluginManager, List<(string, IEnumerable<ICustomMenu>)>> GetMenus;
        private ShowHideState TimelineIsShown = ShowHideState.Show;
        private ShowHideState PropertyIsShown = ShowHideState.Show;

        static MainWindow()
        {
            var type = typeof(PluginManager);

            var param = Expression.Parameter(type);
            var expression = Expression.Lambda<Func<PluginManager, List<(string, IEnumerable<ICustomMenu>)>>>(
                Expression.Field(param, "_menus"), param);

            GetMenus = expression.Compile();
        }
        public MainWindow()
        {
            InitializeComponent();

            Activated += (_, _) => MainWindowViewModel.Current.MainWindowColor.Value = (System.Windows.Media.Brush)FindResource("PrimaryHueMidBrush");
            Deactivated += (_, _) => MainWindowViewModel.Current.MainWindowColor.Value = (System.Windows.Media.Brush)FindResource("PrimaryHueDarkBrush");
            EditModel.Current.ClipCreate += EditModel_ClipCreate;
            EditModel.Current.SceneCreate += EditModel_SceneCreate;
            EditModel.Current.EffectAddTo += EditModel_EffectAddTo;

            Focus();

            SetMostUsedFiles();
            SetPluginMenu();
        }

        private void EditModel_EffectAddTo(object? sender, ClipElement c)
        {
            var context = new EffectAddPageViewModel()
            {
                Scene =
                {
                    Value = c.Parent
                }
            };
            var dialog = new EffectAddPage(context);

            foreach(var i in context.ClipItems.Value)
            {
                i.IsSelected.Value = false;
                if (i.Clip == c)
                {
                    i.IsSelected.Value = true;
                }
            }

            new NoneDialog()
            {
                Content = dialog,
                MaxWidth = double.PositiveInfinity
            }.ShowDialog();
        }
        private void EditModel_SceneCreate(object? sender, EventArgs e)
        {
            new NoneDialog()
            {
                Content = new SceneCreatePage(),
                MaxWidth = double.PositiveInfinity,
            }.ShowDialog();
        }
        private void EditModel_ClipCreate(object? sender, EventArgs e)
        {
            var dialog = new ClipCreatePage(new ClipCreatePageViewModel()
            {
                Scene =
                {
                    Value = AppData.Current.Project!.PreviewScene
                }
            });

            new NoneDialog()
            {
                Content = dialog,
                MaxWidth = double.PositiveInfinity
            }.ShowDialog();
        }

        private void SetMostUsedFiles()
        {
            static void ProjectOpenCommand(string name)
            {
                try
                {
                    var project = new Project(name);
                    project.Load();
                    AppData.Current.Project = project;
                    AppData.Current.AppStatus = Status.Edit;

                    Settings.Default.MostRecentlyUsedList.Remove(name);
                    Settings.Default.MostRecentlyUsedList.Add(name);
                }
                catch
                {
                    Debug.Assert(false);
                    Message.Snackbar(string.Format(Core.Properties.Resources.FailedToLoad, "Project"));
                }
            }

            foreach (var file in Settings.Default.MostRecentlyUsedList)
            {
                var menu = new MenuItem()
                {
                    Header = file
                };
                menu.Click += (s, e) => ProjectOpenCommand(((s as MenuItem)!.Header as string)!);

                UsedFiles.Items.Insert(0, menu);
            }

            Settings.Default.MostRecentlyUsedList.CollectionChanged += (s, e) =>
            {
                if (s is null) return;
                if (e.Action is NotifyCollectionChangedAction.Add)
                {
                    var menu = new MenuItem()
                    {
                        Header = (s as ObservableCollection<string>)![e.NewStartingIndex]
                    };
                    menu.Click += (s, e) => ProjectOpenCommand(((s as MenuItem)!.Header as string)!);

                    UsedFiles.Items.Insert(0, menu);
                }
                else if (e.Action is NotifyCollectionChangedAction.Remove)
                {
                    var file = e.OldItems![0] as string;

                    foreach (var item in UsedFiles.Items)
                    {
                        if (item is MenuItem menuItem && menuItem.Header is string header && header == file)
                        {
                            UsedFiles.Items.Remove(item);

                            return;
                        }
                    }
                }
            };
        }
        private void SetPluginMenu()
        {
            var menus = GetMenus(PluginManager.Default);

            foreach (var item in menus)
            {
                var menu = new MenuItem()
                {
                    Header = item.Item1
                };

                foreach (var m in item.Item2)
                {
                    var command = new ReactiveCommand();
                    command.Subscribe(m.Execute);

                    var newItem = new MenuItem()
                    {
                        Command = command,
                        Header = m.Name
                    };

                    menu.Items.Add(newItem);
                }

                PluginMenu.Items.Add(menu);
            }
        }

        private void MetroWindow_Closing(object sender, CancelEventArgs e) { }

        private void ObjectMouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.LeftButton == MouseButtonState.Pressed)
            {
                PackIcon packIcon = (PackIcon)sender;
                Func<ObjectMetadata> s = () => ClipTypeIconConverter.ToClipMetadata(packIcon.Kind);
                DataObject dataObject = new DataObject(typeof(Func<ObjectMetadata>), s);
                // ドラッグ開始
                DragDrop.DoDragDrop(this, dataObject, DragDropEffects.Copy);
            }
        }

        private void Button_Click(object sender, RoutedEventArgs e)
        {
            var btn = (Button)sender;

            if (btn.ContextMenu == null) return;

            btn.ContextMenu.IsOpen = true;
            btn.ContextMenu.PlacementTarget = btn;
            btn.ContextMenu.Placement = PlacementMode.Bottom;
        }

        private void LoadedObjectMouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.LeftButton == MouseButtonState.Pressed)
            {
                var text = (TextBlock)sender;
                Func<ObjectMetadata> s = () => (ObjectMetadata)text.DataContext;
                DataObject dataObject = new DataObject(typeof(Func<ObjectMetadata>), s);
                // ドラッグ開始
                DragDrop.DoDragDrop(this, dataObject, DragDropEffects.Copy);
            }
        }

        private void TimelineShowHide(object sender, RoutedEventArgs e)
        {
            if (TimelineIsShown == ShowHideState.Show)
            {
                TimelineGrid.Height = new GridLength(0);
                TimelineIsShown = ShowHideState.Hide;
            }
            else
            {
                TimelineGrid.Height = new GridLength(1, GridUnitType.Star);
                TimelineIsShown = ShowHideState.Show;
            }
        }
        private void PropertyShowHide(object sender, RoutedEventArgs e)
        {
            if (PropertyIsShown == ShowHideState.Show)
            {
                PropertyGrid.Width = new GridLength(0);
                PropertyIsShown = ShowHideState.Hide;
            }
            else
            {
                PropertyGrid.Width = new GridLength(425);
                PropertyIsShown = ShowHideState.Show;
            }
        }
    }
}
