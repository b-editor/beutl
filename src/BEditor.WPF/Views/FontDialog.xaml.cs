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

using BEditor.ViewModels;
using BEditor.ViewModels.PropertyControl;

using MahApps.Metro.Controls;

using Reactive.Bindings.Extensions;

namespace BEditor.Views
{
    /// <summary>
    /// FontDialogContent.xaml の相互作用ロジック
    /// </summary>
    public partial class FontDialog : MetroWindow
    {
        public FontDialog()
        {
            InitializeComponent();

            DataContextChanged += FontDialog_DataContextChanged;
        }

        private void FontDialog_DataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            if (DataContext is FontDialogViewModel viewModel)
            {
                viewModel.WindowClose.Subscribe(Close).AddTo(viewModel._disposables);
            }
        }
    }
}