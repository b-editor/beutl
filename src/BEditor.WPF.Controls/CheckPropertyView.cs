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

namespace BEditor.WPF.Controls
{
    public class CheckPropertyView : BasePropertyView
    {
        public static readonly DependencyProperty IsCheckedProperty = DependencyProperty.Register(nameof(IsChecked), typeof(bool), typeof(CheckPropertyView));
        public static readonly DependencyProperty CheckCommandProperty = DependencyProperty.Register(nameof(CheckCommand), typeof(ICommand), typeof(CheckPropertyView));

        static CheckPropertyView()
        {
            DefaultStyleKeyProperty.OverrideMetadata(typeof(CheckPropertyView), new FrameworkPropertyMetadata(typeof(CheckPropertyView)));
        }

        public bool IsChecked
        {
            get => (bool)GetValue(HeaderProperty);
            set => SetValue(HeaderProperty, value);
        }
        public ICommand CheckCommand
        {
            get => (ICommand)GetValue(CheckCommandProperty);
            set => SetValue(CheckCommandProperty, value);
        }
    }
}