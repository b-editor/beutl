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
using System.Windows.Shapes;

using BEditor.ViewModels;
using BEditor.Views.SettingsControl;

using MahApps.Metro.Controls;

namespace BEditor.Views
{
    /// <summary>
    /// StartWindow.xaml の相互作用ロジック
    /// </summary>
    public partial class StartWindow : MetroWindow
    {
        public StartWindow()
        {
            DataContext = new StartWindowViewModel();
            InitializeComponent();
        }

        private void RadioButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is RadioButton b && DataContext is StartWindowViewModel vm)
            {
#pragma warning disable CS0252 // 予期しない参照比較です。左辺をキャストする必要があります
                if ((StartWindowViewModel.MenuItem)vm.Selected.Value != (StartWindowViewModel.MenuItem)b.DataContext)
#pragma warning restore CS0252 // 予期しない参照比較です。左辺をキャストする必要があります
                {
                    b.IsChecked = !b.IsChecked;
                }
            }
        }

        private void Continue_Click(object sender, RoutedEventArgs e)
        {
            var win = new MainWindow();
            App.Current.MainWindow = win;
            win.Show();

            Close();
        }

        private void ShowSetting_Click(object sender, RoutedEventArgs e)
        {
            new SettingsWindow() { Owner = App.Current.MainWindow }.ShowDialog();
        }
    }
}
