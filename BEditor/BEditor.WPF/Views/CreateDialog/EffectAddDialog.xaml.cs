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

using BEditor.Core.Data;
using BEditor.ViewModels.CreateDialog;

using MahApps.Metro.Controls;

namespace BEditor.Views.CreateDialog
{
    /// <summary>
    /// EffectAddDialog.xaml の相互作用ロジック
    /// </summary>
    public partial class EffectAddDialog : MetroWindow
    {
        public EffectAddDialog()
        {
            InitializeComponent();
            TreeView.SelectedItemChanged += (s, e) =>
            {
                (DataContext as EffectAddDialogViewModel).Type.Value = e.NewValue as EffectMetadata;
            };
        }

        private void CloseClick(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}
