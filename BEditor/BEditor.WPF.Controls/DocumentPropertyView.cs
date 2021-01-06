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

            var text = GetTemplateChild("TextBox") as TextBox;

            text.GotFocus += (s, _) => GotFocusCommand.Execute((s as TextBox).Text);
            text.LostFocus += (s, _) => LostFocusCommand.Execute((s as TextBox).Text);
            text.TextChanged += (s, _) => TextChanged.Execute((s as TextBox).Text);
        }
    }
}
