using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;

using Beutl.Animation;
using Beutl.Views.AnimationVisualizer;

namespace VisualizeAnimationTest
{
    public partial class MainWindow : Window
    {
        private readonly Animation<float> _float_animation = new(Beutl.Graphics.Transformation.TranslateTransform.XProperty)
        {
            Children =
            {
                new AnimationSpan<float>()
                {
                    Duration = TimeSpan.FromSeconds(3),
                    Easing = new Beutl.Animation.Easings.CubicEaseIn(),
                    Previous = 0,
                    Next = 100,
                },
                new AnimationSpan<float>()
                {
                    Duration = TimeSpan.FromSeconds(3),
                    Easing = new Beutl.Animation.Easings.CubicEaseOut(),
                    Previous = 100,
                    Next = 500,
                },
                new AnimationSpan<float>()
                {
                    Duration = TimeSpan.FromSeconds(3),
                    Easing = new Beutl.Animation.Easings.CircularEaseInOut(),
                    Previous = 500,
                    Next = -100,
                }
            }
        };

        private readonly Animation<int> _int_animation = new(Beutl.Graphics.Effects.Border.ThicknessProperty)
        {
            Children =
            {
                new AnimationSpan<int>()
                {
                    Duration = TimeSpan.FromSeconds(3),
                    Easing = new Beutl.Animation.Easings.CubicEaseIn(),
                    Previous = 100,
                    Next = 100,
                },
                new AnimationSpan<int>()
                {
                    Duration = TimeSpan.FromSeconds(3),
                    Easing = new Beutl.Animation.Easings.CubicEaseOut(),
                    Previous = 100,
                    Next = 500,
                },
                new AnimationSpan<int>()
                {
                    Duration = TimeSpan.FromSeconds(3),
                    Easing = new Beutl.Animation.Easings.CircularEaseInOut(),
                    Previous = 500,
                    Next = -100,
                }
            }
        };

        private readonly Animation<Beutl.Media.Color> _color_animation = new(Beutl.Media.SolidColorBrush.ColorProperty)
        {
            Children =
            {
                new AnimationSpan<Beutl.Media.Color>()
                {
                    Duration = TimeSpan.FromSeconds(3),
                    Easing = new Beutl.Animation.Easings.CubicEaseIn(),
                    Previous = Beutl.Media.Colors.Blue,
                    Next = Beutl.Media.Colors.Green,
                },
                new AnimationSpan<Beutl.Media.Color>()
                {
                    Duration = TimeSpan.FromSeconds(3),
                    Easing = new Beutl.Animation.Easings.BounceEaseInOut(),
                    Previous = Beutl.Media.Colors.Green,
                    Next = Beutl.Media.Colors.Red,
                }
            }
        };

        public MainWindow()
        {
            Width = 500;
            Height = 100;
            InitializeComponent();
            Content = new IntegerAnimationVisualizer<int>(_int_animation)
            {
                Margin = new(8),
                ClipToBounds = false
            };
            //Content = new ColorAnimationVisualizer(_color_animation)
            //{
            //    Margin = new(8),
            //    ClipToBounds = false
            //};
            //Content = new EasingFunctionVisualizer<int>(_int_animation)
            //{
            //    Margin = new(8),
            //    ClipToBounds = false
            //};
#if DEBUG
            this.AttachDevTools();
#endif
        }

        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
        }
    }
}
