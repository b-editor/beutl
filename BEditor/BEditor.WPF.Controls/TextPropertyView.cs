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
    public class TextPropertyView : BasePropertyView
    {
        public static readonly DependencyProperty TextProperty = DependencyProperty.Register(nameof(Text), typeof(string), typeof(TextPropertyView));
        public static readonly DependencyProperty GotFocusCommandProperty = DependencyProperty.Register(nameof(GotFocusCommand), typeof(ICommand), typeof(TextPropertyView));
        public static readonly DependencyProperty LostFocusCommandProperty = DependencyProperty.Register(nameof(LostFocusCommand), typeof(ICommand), typeof(TextPropertyView));
        public static readonly DependencyProperty TextChangedCommandProperty = DependencyProperty.Register(nameof(TextChanged), typeof(ICommand), typeof(TextPropertyView));

        static TextPropertyView()
        {
            DefaultStyleKeyProperty.OverrideMetadata(typeof(TextPropertyView), new FrameworkPropertyMetadata(typeof(TextPropertyView)));
        }

        public string Text
        {
            get => (string)GetValue(TextProperty);
            set => SetValue(TextProperty, value);
        }
        public ICommand GotFocusCommand
        {
            get => (ICommand)GetValue(GotFocusCommandProperty);
            set => SetValue(GotFocusCommandProperty, value);
        }
        public ICommand LostFocusCommand
        {
            get => (ICommand)GetValue(LostFocusCommandProperty);
            set => SetValue(LostFocusCommandProperty, value);
        }
        public ICommand TextChanged
        {
            get => (ICommand)GetValue(TextChangedCommandProperty);
            set => SetValue(TextChangedCommandProperty, value);
        }

        public override void OnApplyTemplate()
        {
            base.OnApplyTemplate();

            var text = GetTemplateChild("TextBox") as TextBox;

            text.GotFocus += (s, _) => GotFocusCommand.Execute((s as TextBox).Text);
            text.LostFocus += (s, _) => LostFocusCommand.Execute((s as TextBox).Text);
            text.TextChanged += (s, _) => TextChanged.Execute((s as TextBox).Text);
        }
    }
}
