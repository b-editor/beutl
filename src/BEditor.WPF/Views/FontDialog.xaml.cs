using System.Windows;

using BEditor.ViewModels;

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