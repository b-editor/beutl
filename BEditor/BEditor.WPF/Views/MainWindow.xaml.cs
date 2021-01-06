using System;
using System.Collections.Specialized;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;

using BEditor.Core.Data;
using BEditor.Core.Data.Primitive.Properties;
using BEditor.Core.Plugin;
using BEditor.Core.Service;
using BEditor.Models;
using BEditor.ViewModels;
using BEditor.ViewModels.MessageContent;
using BEditor.Views;
using BEditor.Views.MessageContent;

using MahApps.Metro.Controls;

using MaterialDesignThemes.Wpf;

namespace BEditor
{
    /// <summary>
    /// MainWindow.xaml の相互作用ロジック
    /// </summary>
    public partial class MainWindow : MetroWindow
    {
        public MainWindow()
        {
            InitializeComponent();

            Activated += (_, _) => MainWindowViewModel.Current.MainWindowColor.Value = (System.Windows.Media.Brush)FindResource("PrimaryHueMidBrush");
            Deactivated += (_, _) => MainWindowViewModel.Current.MainWindowColor.Value = (System.Windows.Media.Brush)FindResource("PrimaryHueDarkBrush");
            
            Focus();

            SetMostUsedFiles();
        }

        private void SetMostUsedFiles()
        {
            foreach (var file in Settings.Default.MostRecentlyUsedList)
            {
                var menu = new MenuItem()
                {
                    Header = file
                };
                menu.Click += (s, e) => MainWindowViewModel.ProjectOpenCommand((s as MenuItem).Header as string);

                UsedFiles.Items.Insert(0, menu);
            }

            Settings.Default.MostRecentlyUsedList.CollectionChanged += (s, e) =>
            {
                if (e.Action is NotifyCollectionChangedAction.Add)
                {
                    var menu = new MenuItem()
                    {
                        Header = e.NewItems[e.NewStartingIndex]
                    };
                    menu.Click += (s, e) => MainWindowViewModel.ProjectOpenCommand((s as MenuItem).Header as string);

                    UsedFiles.Items.Insert(0, menu);
                }
                else if (e.Action is NotifyCollectionChangedAction.Remove)
                {
                    var file = e.OldItems[e.OldStartingIndex] as string;

                    foreach (var item in UsedFiles.Items)
                    {
                        if (item is MenuItem menuItem && menuItem.Header is string header && header == file)
                        {
                            UsedFiles.Items.Remove(item);
                        }
                    }
                }
            };
        }

        private void MetroWindow_Closing(object sender, System.ComponentModel.CancelEventArgs e) { }

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

        private void MetroWindow_Loaded(object sender, RoutedEventArgs e)
        {
            //コマンドライン引数から開く
            if (AppData.Current.Arguments.Length != 0 && File.Exists(AppData.Current.Arguments[0]))
            {
                if (Path.GetExtension(AppData.Current.Arguments[0]) == ".bedit")
                {
                    AppData.Current.Project = new(AppData.Current.Arguments[0]);
                    AppData.Current.AppStatus = Status.Edit;
                }
            }
        }
    }
}
