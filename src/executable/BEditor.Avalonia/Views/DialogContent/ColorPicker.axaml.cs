using System;
using System.Reactive.Linq;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Markup.Xaml;
using Avalonia.Media;

using Reactive.Bindings;

namespace BEditor.Views.DialogContent
{
    public sealed class ColorPicker : UserControl
    {
        public static readonly StyledProperty<byte> RedProperty = AvaloniaProperty.Register<ColorPicker, byte>(nameof(Red), 255, defaultBindingMode: BindingMode.TwoWay);
        public static readonly StyledProperty<byte> GreenProperty = AvaloniaProperty.Register<ColorPicker, byte>(nameof(Green), 255, defaultBindingMode: BindingMode.TwoWay);
        public static readonly StyledProperty<byte> BlueProperty = AvaloniaProperty.Register<ColorPicker, byte>(nameof(Blue), 255, defaultBindingMode: BindingMode.TwoWay);
        public static readonly StyledProperty<byte> AlphaProperty = AvaloniaProperty.Register<ColorPicker, byte>(nameof(Alpha), 255, defaultBindingMode: BindingMode.TwoWay);
        public static readonly StyledProperty<bool> UseAlphaProperty = AvaloniaProperty.Register<ColorPicker, bool>(nameof(UseAlpha), true, defaultBindingMode: BindingMode.OneWay);

        public ColorPicker()
        {
            InitializeComponent();

            SelectedColor = new(new SolidColorBrush(Colors.White));
            this.FindControl<Border>("border").DataContext = SelectedColor;

            SelectedColor.Subscribe(c => (Red, Green, Blue, Alpha) = (c.Color.R, c.Color.G, c.Color.B, c.Color.A));
            PropertyChanged += (s, e) =>
            {
                if (e.Property.Name is nameof(Red) or nameof(Green) or nameof(Blue) or nameof(Alpha))
                {
                    SelectedColor.Value = new(Color.FromArgb(Alpha, Red, Green, Blue));
                }
            };
        }

        public byte Red
        {
            get => GetValue(RedProperty);
            set => SetValue(RedProperty, value);
        }

        public byte Green
        {
            get => GetValue(GreenProperty);
            set => SetValue(GreenProperty, value);
        }

        public byte Blue
        {
            get => GetValue(BlueProperty);
            set => SetValue(BlueProperty, value);
        }

        public byte Alpha
        {
            get => GetValue(AlphaProperty);
            set => SetValue(AlphaProperty, value);
        }

        public bool UseAlpha
        {
            get => GetValue(UseAlphaProperty);
            set => SetValue(UseAlphaProperty, value);
        }

        public ReactiveProperty<SolidColorBrush> SelectedColor { get; }

        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
        }
    }
}