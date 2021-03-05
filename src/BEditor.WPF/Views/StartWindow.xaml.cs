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
                if (vm.Selected.Value != (StartWindowViewModel.MenuItem)b.DataContext)
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

        private void Card_MouseDown(object sender, MouseButtonEventArgs e)
        {
            DragMove();
        }
    }
}
