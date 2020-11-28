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

using BEditor.Core.Data.Primitive.Components;
using BEditor.Views.CustomControl;

namespace BEditor.Views.PropertyControl
{
    /// <summary>
    /// LabelControl.xaml の相互作用ロジック
    /// </summary>
    public partial class LabelControl : UserControl, ICustomTreeViewItem
    {
        public LabelControl(LabelComponent component)
        {
            InitializeComponent();
            DataContext = component;
        }

        public double LogicHeight => 32.5;
    }
}
