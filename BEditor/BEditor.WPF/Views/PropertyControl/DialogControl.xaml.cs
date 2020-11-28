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

namespace BEditor.Views.PropertyControl
{
    /// <summary>
    /// DialogControl.xaml の相互作用ロジック
    /// </summary>
    public partial class DialogControl : UserControl, ICustomTreeViewItem
    {
        private static readonly ModelToComponent.PropertyViewBuilder builder;
        private readonly DialogProperty property;

        static DialogControl()
        {
            builder = ModelToComponent.PropertyViewBuilders.Find(builder => builder.PropertyType == typeof(Group));
        }
        public DialogControl(DialogProperty property)
        {
            InitializeComponent();
            property.Showed += Property_Showed;
            DataContext = new DialogPropertyViewModel(this.property = property);
        }

        public double LogicHeight => 32.5;


        private void Property_Showed(object sender, EventArgs e)
        {
            static Window GetCreate(DialogProperty property)
            {
                if (!property.ComponentData.ContainsKey("GetDialog"))
                {
                    property.ComponentData.Add("GetDialog", builder.CreateFunc(property));
                }
                return new NoneDialog(property.ComponentData["GetDialog"]);
            }

            GetCreate(property).ShowDialog();
        }
    }
}
