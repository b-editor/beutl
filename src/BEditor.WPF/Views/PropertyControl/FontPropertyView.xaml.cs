using System;
using System.Collections.Generic;
using System.Globalization;
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
using BEditor.Models;
using BEditor.Primitive.Objects;
using BEditor.ViewModels;
using BEditor.ViewModels.PropertyControl;

namespace BEditor.Views.PropertyControl
{
    /// <summary>
    /// FontPropertyView.xaml の相互作用ロジック
    /// </summary>
    public partial class FontPropertyView : UserControl
    {
        private bool _mouseDown = false;

        public FontPropertyView(FontPropertyViewModel viewModel)
        {
            DataContext = viewModel;
            InitializeComponent();
        }

        private void Box_PreviewMouseDown(object sender, MouseButtonEventArgs e)
        {
            _mouseDown = true;

            e.Handled = true;
        }

        private void Box_PreviewMouseUp(object sender, MouseButtonEventArgs e)
        {
            if (_mouseDown)
            {
                // ダイアログ表示
                var thisViewModel = (FontPropertyViewModel)DataContext;
                using var viewModel = new FontDialogViewModel(thisViewModel.Property.Value);

                // Textオブジェクトの場合、値を設定する
                if (thisViewModel.Property.Parent is Text textObject)
                {
                    viewModel.SampleText.Value = textObject.Document.Value;
                }
                else
                {
                    viewModel.SampleText.Value = CultureInfo.CurrentCulture.DisplayName;
                }

                var dialog = new FontDialog()
                {
                    DataContext = viewModel
                };
                dialog.ShowDialog();

                if (thisViewModel.Property.Value != viewModel.SelectedItem.Value.Font && viewModel.OKIsClicked)
                {
                    thisViewModel.Property.ChangeFont(viewModel.SelectedItem.Value.Font).Execute();
                }

                _mouseDown = false;
            }
        }
    }
}
