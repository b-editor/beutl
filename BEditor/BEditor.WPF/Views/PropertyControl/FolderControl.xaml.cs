using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;

using BEditor.Core.Data.Primitive.Properties;
using BEditor.ViewModels.PropertyControl;
using BEditor.Views.CustomControl;
using BEditor.Views.PropertyControls;

using Microsoft.WindowsAPICodePack.Dialogs;

namespace BEditor.Views.PropertyControl
{
    /// <summary>
    /// FolderControl.xaml の相互作用ロジック
    /// </summary>
    public partial class FolderControl : UserControl, ICustomTreeViewItem
    {
        private readonly FolderProperty property;

        public FolderControl(FolderProperty property)
        {
            InitializeComponent();
            DataContext = new FolderPropertyViewModel(this.property = property);


            FolderChangeCommand = () =>
            {
                // ダイアログのインスタンスを生成
                var dialog = new CommonOpenFileDialog()
                {
                    IsFolderPicker = true
                };


                // ダイアログを表示する
                if (dialog.ShowDialog() == CommonFileDialogResult.Ok)
                {
                    return dialog.FileName;
                }

                return null;
            };
        }

        public Func<string> FolderChangeCommand { get; }
        public double LogicHeight => 42.5;

        private void BindClick(object sender, RoutedEventArgs e)
        {
            var window = new BindSettings()
            {
                DataContext = new BindSettingsViewModel<string>(property)
            };
            window.ShowDialog();
        }
    }
}
