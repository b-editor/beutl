using System.Windows;
using System.Windows.Controls;

namespace BEditor.Views.CreatePage
{
    /// <summary>
    /// EffectAddPage.xaml の相互作用ロジック
    /// </summary>
    public sealed partial class EffectAddPage : UserControl
    {
        public EffectAddPage(object datacontext)
        {
            DataContext = datacontext;
            InitializeComponent();
        }

        private void CloseClick(object sender, RoutedEventArgs e)
        {
            Window.GetWindow(this).Close();
        }
    }
}