using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;

using BEditor.ViewModels;
using BEditorCore.Data;
using BEditorCore.Data.ProjectData;
using BEditorCore.Plugin;
using MahApps.Metro.Controls;

using MaterialDesignThemes.Wpf;

namespace BEditor {
    /// <summary>
    /// MainWindow.xaml の相互作用ロジック
    /// </summary>
    public partial class MainWindow : MetroWindow {
        public MainWindow() {
            InitializeComponent();

            Loaded += (sender, e) => {
                object Data = null;

                if (System.Windows.Forms.Clipboard.ContainsText()) {
                    Data = System.Windows.Forms.Clipboard.GetText();
                }
                else if (System.Windows.Forms.Clipboard.ContainsFileDropList()) {
                    Data = System.Windows.Forms.Clipboard.GetFileDropList();
                }

                Models.Clipboard.Data = Data;


                PluginManager.Load();


                //コマンドライン引数から開く
                if (Component.Current.Arguments.Length != 0 && File.Exists(Component.Current.Arguments[0])) {
                    if (Path.GetExtension(Component.Current.Arguments[0]) == ".bedit") {
                        Component.Current.Project = Project.Open(Component.Current.Arguments[0]);
                    }
                }
            };

            Activated += (_, _) => MainWindowViewModel.Current.MainWindowColor.Value = (System.Windows.Media.Brush)FindResource("PrimaryHueMidBrush");
            Deactivated += (_, _) => MainWindowViewModel.Current.MainWindowColor.Value = (System.Windows.Media.Brush)FindResource("PrimaryHueDarkBrush");

            Focus();
        }

        private void MetroWindow_Closing(object sender, System.ComponentModel.CancelEventArgs e) => Models.Clipboard.clipboardWatcher.Dispose();

        private void ObjectMouseDown(object sender, MouseButtonEventArgs e) {
            if (e.LeftButton == MouseButtonState.Pressed) {
                PackIcon packIcon = (PackIcon)sender;
                Type s = ClipTypeIconConverter.ToClipType(packIcon.Kind);
                DataObject dataObject = new DataObject(typeof(Type), s);
                // ドラッグ開始
                DragDrop.DoDragDrop(this, dataObject, DragDropEffects.Copy);
            }
        }

        private void Button_Click(object sender, RoutedEventArgs e) {
            var btn = (Button)sender;

            if (btn.ContextMenu == null) {
                return;
            }

            btn.ContextMenu.IsOpen = true;
            btn.ContextMenu.PlacementTarget = btn;
            btn.ContextMenu.Placement = PlacementMode.Bottom;
        }
    }
}
