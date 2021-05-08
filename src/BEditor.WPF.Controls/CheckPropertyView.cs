using System.Windows;
using System.Windows.Input;

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