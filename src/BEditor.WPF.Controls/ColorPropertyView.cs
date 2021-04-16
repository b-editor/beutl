using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace BEditor.WPF.Controls
{
    public class ColorPropertyView : BasePropertyView
    {
        public static readonly DependencyProperty ClickCommandProperty = DependencyProperty.Register(nameof(ClickCommand), typeof(ICommand), typeof(ColorPropertyView));
        public static readonly DependencyProperty ColorProperty = DependencyProperty.Register(nameof(Color), typeof(Brush), typeof(ColorPropertyView));

        static ColorPropertyView()
        {
            DefaultStyleKeyProperty.OverrideMetadata(typeof(ColorPropertyView), new FrameworkPropertyMetadata(typeof(ColorPropertyView)));
        }

        public Brush Color
        {
            get => (Brush)GetValue(ColorProperty);
            set => SetValue(ColorProperty, value);
        }
        public ICommand ClickCommand
        {
            get => (ICommand)GetValue(ClickCommandProperty);
            set => SetValue(ClickCommandProperty, value);
        }
    }
}