using System.Windows.Controls;

namespace BEditor.Views.CustomControl
{
    /// <summary>
    /// CustomTab.xaml の相互作用ロジック
    /// </summary>
    public partial class PropertyTab : UserControl
    {
        public PropertyTab(object datacontext)
        {
            DataContext = datacontext;
            InitializeComponent();
        }
    }
}