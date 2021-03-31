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
    public class ValuePropertyView : BasePropertyView
    {
        public static readonly DependencyProperty ValueProperty = DependencyProperty.Register(nameof(Value), typeof(float), typeof(ValuePropertyView), new FrameworkPropertyMetadata(0f, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault));
        public static readonly DependencyProperty GotFocusCommandProperty = DependencyProperty.Register(nameof(GotFocusCommand), typeof(ICommand), typeof(ValuePropertyView));
        public static readonly DependencyProperty LostFocusCommandProperty = DependencyProperty.Register(nameof(LostFocusCommand), typeof(ICommand), typeof(ValuePropertyView));
        public static readonly DependencyProperty PreviewMouseWheelCommandProperty = DependencyProperty.Register(nameof(PreviewMouseWheelCommand), typeof(ICommand), typeof(ValuePropertyView));
        public static readonly DependencyProperty KeyDownCommandProperty = DependencyProperty.Register(nameof(KeyDownCommand), typeof(ICommand), typeof(ValuePropertyView));

        static ValuePropertyView()
        {
            DefaultStyleKeyProperty.OverrideMetadata(typeof(ValuePropertyView), new FrameworkPropertyMetadata(typeof(ValuePropertyView)));
        }

        public float Value
        {
            get => (float)GetValue(ValueProperty);
            set => SetValue(ValueProperty, value);
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
        public ICommand PreviewMouseWheelCommand
        {
            get => (ICommand)GetValue(PreviewMouseWheelCommandProperty);
            set => SetValue(PreviewMouseWheelCommandProperty, value);
        }
        public ICommand KeyDownCommand
        {
            get => (ICommand)GetValue(KeyDownCommandProperty);
            set => SetValue(KeyDownCommandProperty, value);
        }

        public override void OnApplyTemplate()
        {
            base.OnApplyTemplate();

            var text = (TextBox)GetTemplateChild("textbox");

            text.GotFocus += Text_GotFocus;
            text.LostFocus += Text_LostFocus;
            text.PreviewMouseWheel += Text_PreviewMouseWheel;
            text.KeyDown += Text_KeyDown;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                var text = (TextBox)GetTemplateChild("textbox");

                if (text is null) return;

                text.GotFocus -= Text_GotFocus;
                text.LostFocus -= Text_LostFocus;
                text.PreviewMouseWheel -= Text_PreviewMouseWheel;
                text.KeyDown -= Text_KeyDown;
            }

            base.Dispose(disposing);
        }

        private void Text_KeyDown(object sender, KeyEventArgs e)
        {
            KeyDownCommand?.Execute(((TextBox)sender).Text);
        }
        private void Text_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            PreviewMouseWheelCommand?.Execute(((TextBox)sender as TextBox, e));
        }
        private void Text_LostFocus(object sender, RoutedEventArgs e)
        {
            LostFocusCommand?.Execute(((TextBox)sender).Text);
        }
        private void Text_GotFocus(object sender, RoutedEventArgs e)
        {
            GotFocusCommand?.Execute(((TextBox)sender).Text);
        }
    }
}
