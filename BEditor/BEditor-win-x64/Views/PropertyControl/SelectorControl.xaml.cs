using System.Windows.Controls;
using System.Windows.Data;

using BEditor.ViewModels.PropertyControl;
using BEditor.Views.CustomControl;

using BEditor.NET.Data.PropertyData;

namespace BEditor.Views.PropertyControls {
    /// <summary>
    /// SelectorControl.xaml の相互作用ロジック
    /// </summary>
    public partial class SelectorControl : UserControl, ICustomTreeViewItem {
        public SelectorControl(SelectorProperty selector) {
            InitializeComponent();
            DataContext = new SelectorPropertyViewModel(selector);

            Binding binding = new Binding("Property.Index") { Mode = BindingMode.OneWay };
            combo.SetBinding(System.Windows.Controls.Primitives.Selector.SelectedIndexProperty, binding);
        }

        public SelectorControl(FontProperty selector) {
            InitializeComponent();
            DataContext = new FontPropertyViewModel(selector);

            Binding binding = new Binding("Property.Select") { Mode = BindingMode.OneWay };
            combo.SetBinding(System.Windows.Controls.Primitives.Selector.SelectedValueProperty, binding);
        }

        public double LogicHeight => 32.5;
    }
}
