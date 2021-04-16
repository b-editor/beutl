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

            var text = (TextBox)GetTemplateChild("TextBox");
            text.GotFocus += Text_GotFocus;
            text.LostFocus += Text_LostFocus;
            text.TextChanged += Text_TextChanged;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                var text = (TextBox)GetTemplateChild("TextBox");

                if (text is null) return;

                text.GotFocus -= Text_GotFocus;
                text.LostFocus -= Text_LostFocus;
                text.TextChanged -= Text_TextChanged;
            }

            base.Dispose(disposing);
        }

        private void Text_TextChanged(object s, TextChangedEventArgs _) => TextChanged?.Execute(((TextBox)s).Text);

        private void Text_LostFocus(object s, RoutedEventArgs _) => LostFocusCommand?.Execute(((TextBox)s).Text);

        private void Text_GotFocus(object s, RoutedEventArgs _) => GotFocusCommand?.Execute(((TextBox)s).Text);
    }
}