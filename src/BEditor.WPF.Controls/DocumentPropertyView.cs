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
    public class DocumentPropertyView : BasePropertyView
    {
        public static readonly DependencyProperty TextProperty = DependencyProperty.Register(nameof(Text), typeof(string), typeof(DocumentPropertyView));
        public static readonly DependencyProperty GotFocusCommandProperty = DependencyProperty.Register(nameof(GotFocusCommand), typeof(ICommand), typeof(DocumentPropertyView));
        public static readonly DependencyProperty LostFocusCommandProperty = DependencyProperty.Register(nameof(LostFocusCommand), typeof(ICommand), typeof(DocumentPropertyView));
        public static readonly DependencyProperty TextChangedCommandProperty = DependencyProperty.Register(nameof(TextChanged), typeof(ICommand), typeof(DocumentPropertyView));

        static DocumentPropertyView()
        {
            DefaultStyleKeyProperty.OverrideMetadata(typeof(DocumentPropertyView), new FrameworkPropertyMetadata(typeof(DocumentPropertyView)));
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

        private void Text_TextChanged(object sender, TextChangedEventArgs e)
        {
            TextChanged?.Execute(((TextBox)sender).Text);
        }

        private void Text_LostFocus(object sender, RoutedEventArgs e)
        {
            LostFocusCommand?.Execute(((TextBox)sender).Text);
        }

        private void Text_GotFocus(object sender, RoutedEventArgs e)
        {
            GotFocusCommand?.Execute(((TextBox)sender).Text);
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
    }
}
