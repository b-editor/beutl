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

using BEditor.Data;

using BEditor.Data.Property;
using BEditor.ViewModels.PropertyControl;
using BEditor.Views.CustomControl;
using BEditor.WPF.Controls;

namespace BEditor.Views.PropertyControl
{
    /// <summary>
    /// DialogControl.xaml の相互作用ロジック
    /// </summary>
    public sealed partial class DialogControl : UserControl, ICustomTreeViewItem, IDisposable
    {
        private static readonly ViewBuilder.PropertyViewBuilder builder;
        public static readonly EditingProperty<UIElement> DialogProperty = EditingProperty.Register<UIElement, DialogProperty>("GetDialog");
        private DialogProperty property;

        static DialogControl()
        {
            builder = ViewBuilder.PropertyViewBuilders.Find(builder => builder.PropertyType == typeof(Group))!;
        }
        public DialogControl(DialogProperty property)
        {
            DataContext = new DialogPropertyViewModel(this.property = property);
            InitializeComponent();
            property.Showed += Property_Showed;
        }

        public double LogicHeight => 32.5;


        private void Property_Showed(object? sender, EventArgs e)
        {
            static Window GetCreate(DialogProperty property)
            {
                if (property[DialogProperty] is null)
                {
                    property[DialogProperty] = builder.CreateFunc(property);
                }
                return new NoneDialog(property.GetValue(DialogProperty));
            }

            GetCreate(property).ShowDialog();
        }

        public void Dispose()
        {
            property = null!;
            DataContext = null;

            GC.SuppressFinalize(this);
        }
    }
}
