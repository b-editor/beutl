using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;

using BEditor.ViewModels;
using BEditor.Core.Data;
using BEditor.Core.Plugin;
using MahApps.Metro.Controls;

using MaterialDesignThemes.Wpf;
using BEditor.Models;
using BEditor.Core.Service;
using System.Linq;
using BEditor.Core.Data.Primitive.Properties;
using BEditor.Views.MessageContent;
using BEditor.ViewModels.MessageContent;
using BEditor.Views;

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

            Loaded += (sender, e) =>
            {
                PluginInit();

                //コマンドライン引数から開く
                if (AppData.Current.Arguments.Length != 0 && File.Exists(AppData.Current.Arguments[0]))
                {
                    if (Path.GetExtension(AppData.Current.Arguments[0]) == ".bedit")
                    {
                        AppData.Current.Project = new(AppData.Current.Arguments[0]);
                        AppData.Current.AppStatus = Status.Edit;
                    }
                }
            };

            Activated += (_, _) => MainWindowViewModel.Current.MainWindowColor.Value = (System.Windows.Media.Brush)FindResource("PrimaryHueMidBrush");
            Deactivated += (_, _) => MainWindowViewModel.Current.MainWindowColor.Value = (System.Windows.Media.Brush)FindResource("PrimaryHueDarkBrush");

            Focus();
        }

        private static void PluginInit()
        {
            // すべて
            var all = PluginManager.GetNames();
            // 無効なプラグイン
            var disable = all.Except(Settings.Default.EnablePlugins)
                .Except(Settings.Default.DisablePlugins)
                .ToArray();

            // ここで確認ダイアログを表示
            if (disable.Length != 0)
            {
                var control = new PluginCheckHost();
                var controlvm = new PluginCheckHostViewModel
                {
                    Plugins = new(disable.Select(name => new PluginCheckViewModel() { Name = { Value = name } }))
                };

                control.DataContext = controlvm;

                new NoneDialog(control).ShowDialog();

                foreach (var vm in controlvm.Plugins)
                {
                    if (vm.IsEnabled.Value)
                    {
                        Settings.Default.EnablePlugins.Add(vm.Name.Value);
                    }
                    else
                    {
                        Settings.Default.DisablePlugins.Add(vm.Name.Value);
                    }
                }

                Settings.Default.Save();
            }

            AppData.Current.LoadedPlugins = PluginManager.Load(Settings.Default.EnablePlugins).ToList();
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
    }
}
