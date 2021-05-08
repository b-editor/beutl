using System.Windows;
using System.Windows.Controls;

namespace BEditor.WPF.Controls
{
    public class LabelComponentView : Control
    {
        public static readonly DependencyProperty TextProperty = DependencyProperty.Register(nameof(Text), typeof(string), typeof(LabelComponentView));

        static LabelComponentView()
        {
            DefaultStyleKeyProperty.OverrideMetadata(typeof(LabelComponentView), new FrameworkPropertyMetadata(typeof(LabelComponentView)));
        }

        public string Text
        {
            get => (string)GetValue(TextProperty);
            set => SetValue(TextProperty, value);
        }
    }
}