using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace BEditor.WPF.Controls
{
    public class ButtonComponentView : Control
    {
        public static readonly DependencyProperty TextProperty = DependencyProperty.Register(nameof(Text), typeof(string), typeof(ButtonComponentView));
        public static readonly DependencyProperty CommandProperty = DependencyProperty.Register(nameof(Command), typeof(ICommand), typeof(ButtonComponentView));

        static ButtonComponentView()
        {
            DefaultStyleKeyProperty.OverrideMetadata(typeof(ButtonComponentView), new FrameworkPropertyMetadata(typeof(ButtonComponentView)));
        }

        public string Text
        {
            get => (string)GetValue(TextProperty);
            set => SetValue(TextProperty, value);
        }
        public ICommand Command
        {
            get => (ICommand)GetValue(CommandProperty);
            set => SetValue(CommandProperty, value);
        }
    }
}
