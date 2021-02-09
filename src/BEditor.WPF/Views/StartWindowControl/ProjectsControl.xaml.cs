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
    /// ProjectsControl.xaml の相互作用ロジック
    /// </summary>
    public partial class ProjectsControl : UserControl
    {
        public ProjectsControl()
        {
            var d= new ProjectsControlViewModel();
            DataContext = d;

            d.Close += (_, _) =>
            {
                var win = Window.GetWindow(this);
                win.Close();
            };

            InitializeComponent();
        }
    }
}
