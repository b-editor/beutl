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

using BEditor.ViewModels.StartWindowControl;

namespace BEditor.Views.StartWindowControl
{
    /// <summary>
    /// Learn.xaml の相互作用ロジック
    /// </summary>
    public partial class Learn : UserControl
    {
        public Learn()
        {
            InitializeComponent();
        }

        private void ItemsControl_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            var items = (ItemsControl)sender;
            var scrollViewer = (ScrollViewer)items.Parent;

            if (e.Delta > 0)
            {
                for (int i = 0; i < 4; i++)
                {
                    scrollViewer.LineUp();
                }
            }
            else
            {
                for (int i = 0; i < 4; i++)
                {
                    scrollViewer.LineDown();
                }
            }
        }

        private void RadioButton_Checked(object sender, RoutedEventArgs e)
        {
            var radio = (RadioButton)sender;

            if (radio.IsChecked ?? false)
            {
                var datacontext = (LearnViewModel.Item)radio.DataContext;

                ((LearnViewModel)DataContext).SelectedItem.Value = datacontext;
            }
        }
    }
}