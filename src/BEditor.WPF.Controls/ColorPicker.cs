using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;

namespace BEditor.WPF.Controls
{
    public class ColorPicker : Control, INotifyPropertyChanged
    {
        public static readonly DependencyProperty RedProperty = DependencyProperty.Register("Red", typeof(byte), typeof(ColorPicker), new FrameworkPropertyMetadata((byte)255, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, ColorChanged));
        public static readonly DependencyProperty GreenProperty = DependencyProperty.Register("Green", typeof(byte), typeof(ColorPicker), new FrameworkPropertyMetadata((byte)255, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, ColorChanged));
        public static readonly DependencyProperty BlueProperty = DependencyProperty.Register("Blue", typeof(byte), typeof(ColorPicker), new FrameworkPropertyMetadata((byte)255, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, ColorChanged));
        public static readonly DependencyProperty AlphaProperty = DependencyProperty.Register("Alpha", typeof(byte), typeof(ColorPicker), new FrameworkPropertyMetadata((byte)255, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, ColorChanged));
        public static readonly DependencyProperty UseAlphaProperty = DependencyProperty.Register("UseAlpha", typeof(bool), typeof(ColorPicker), new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault));
        private static readonly PropertyChangedEventArgs selectcolorArgs = new(nameof(SelectedColor));

        public event PropertyChangedEventHandler? PropertyChanged;

        static ColorPicker()
        {
            DefaultStyleKeyProperty.OverrideMetadata(typeof(ColorPicker), new FrameworkPropertyMetadata(typeof(ColorPicker)));
        }

        public ColorPicker()
        {

        }

        public byte Red
        {
            get => (byte)GetValue(RedProperty);
            set => SetValue(RedProperty, value);
        }
        public byte Green
        {
            get => (byte)GetValue(GreenProperty);
            set => SetValue(GreenProperty, value);
        }
        public byte Blue
        {
            get => (byte)GetValue(BlueProperty);
            set => SetValue(BlueProperty, value);
        }
        public byte Alpha
        {
            get => (byte)GetValue(AlphaProperty);
            set => SetValue(AlphaProperty, value);
        }
        public bool UseAlpha
        {
            get => (bool)GetValue(UseAlphaProperty);
            set => SetValue(UseAlphaProperty, value);
        }
        public Color SelectedColor
        {
            get => Color.FromArgb(Alpha, Red, Green, Blue);
            set
            {
                Red = value.R;
                Green = value.G;
                Blue = value.B;
                Alpha = value.A;
            }
        }

        private static void ColorChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (e.NewValue == e.OldValue) return;

            if (d is ColorPicker picker)
            {
                picker.PropertyChanged?.Invoke(picker, selectcolorArgs);
            }
        }
        private void TextBox_Red_MouseWheel(object sender, MouseWheelEventArgs e)
        {
            int v = 10;

            if (Keyboard.IsKeyDown(Key.LeftShift)) v = 1;
            Red += (byte)(e.Delta / 120 * v);
        }
        private void TextBox_Green_MouseWheel(object sender, MouseWheelEventArgs e)
        {
            int v = 10;

            if (Keyboard.IsKeyDown(Key.LeftShift)) v = 1;
            Green += (byte)(e.Delta / 120 * v);
        }
        private void TextBox_Blue_MouseWheel(object sender, MouseWheelEventArgs e)
        {
            int v = 10;

            if (Keyboard.IsKeyDown(Key.LeftShift)) v = 1;
            Blue += (byte)(e.Delta / 120 * v);
        }
        private void TextBox_Alpha_MouseWheel(object sender, MouseWheelEventArgs e)
        {
            int v = 10;

            if (Keyboard.IsKeyDown(Key.LeftShift)) v = 1;
            Alpha += (byte)(e.Delta / 120 * v);
        }
        public override void OnApplyTemplate()
        {
            base.OnApplyTemplate();

            var red = (TextBox)GetTemplateChild("red");
            var green = (TextBox)GetTemplateChild("green");
            var blue = (TextBox)GetTemplateChild("blue");
            var alpha = (TextBox)GetTemplateChild("alpha");
            var picker = (MaterialDesignThemes.Wpf.ColorPicker)GetTemplateChild("picker");

            red.MouseWheel += TextBox_Red_MouseWheel;
            green.MouseWheel += TextBox_Green_MouseWheel;
            blue.MouseWheel += TextBox_Blue_MouseWheel;
            alpha.MouseWheel += TextBox_Alpha_MouseWheel;

            //Color = "{Binding SelectedColor, Mode=TwoWay, UpdateSourceTrigger=PropertyChanged}"
            picker.DataContext = this;
            picker.SetBinding(MaterialDesignThemes.Wpf.ColorPicker.ColorProperty, new Binding("SelectedColor")
            {
                Mode = BindingMode.TwoWay,
                UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged
            });
        }
    }
}
