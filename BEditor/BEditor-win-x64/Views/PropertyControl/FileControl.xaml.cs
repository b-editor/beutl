using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Animation;

using BEditor.ViewModels.PropertyControl;
using BEditor.Views.CustomControl;
using BEditor.NET.Data.PropertyData;
using Microsoft.WindowsAPICodePack.Dialogs;

namespace BEditor.Views.PropertyControls {
    /// <summary>
    /// FileControl.xaml の相互作用ロジック
    /// </summary>
    public partial class FileControl : UserControl, ICustomTreeViewItem {
        public FileControl(FileProperty fileSetting) {
            InitializeComponent();
            DataContext = new FilePropertyViewModel(fileSetting);


            FileChangeCommand = new Func<string, string, string>((filtername, filter) => {
                // ダイアログのインスタンスを生成
                var dialog = new CommonOpenFileDialog();


                dialog.Filters.Add(new CommonFileDialogFilter(filtername, filter));
                dialog.Filters.Add(new CommonFileDialogFilter(null, "*.*"));

                // ダイアログを表示する
                if (dialog.ShowDialog() == CommonFileDialogResult.Ok) {
                    return dialog.FileName;
                }

                return null;
            });
        }

        public Func<string, string, string> FileChangeCommand { get; }

        public double LogicHeight => 42.5;
    }
}
