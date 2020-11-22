using System.Windows.Controls;

using BEditor.ViewModels.PropertyControl;
using BEditor.Views.CustomControl;
using BEditor.Core.Data.Property;
using BEditor.Core.Data.Primitive.Properties;

namespace BEditor.Views.PropertyControls
{
    /// <summary>
    /// CheckBox.xaml の相互作用ロジック
    /// </summary>
    public partial class CheckBox : UserControl, ICustomTreeViewItem
    {
        public CheckBox(CheckProperty check)
        {
            InitializeComponent();

            DataContext = new CheckPropertyViewModel(check);
        }

        public double LogicHeight => 32.5;
    }
}
