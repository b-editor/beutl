using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Media;

using BEditor.Data.Property.Easing;

namespace BEditor.Views.Properties
{
    public sealed class EasingGraph : TemplatedControl
    {
        public static readonly StyledProperty<EasingFunc?> FuncProperty
            = AvaloniaProperty.Register<EasingGraph, EasingFunc?>("Func");

        public static readonly StyledProperty<float> PercentProperty
            = AvaloniaProperty.Register<EasingGraph, float>("Percent");

        private readonly Pen _pen = new()
        {
            Brush = (IBrush)Application.Current.FindResource("TextControlForeground")!,
            LineJoin = PenLineJoin.Round,
            LineCap = PenLineCap.Round,
            Thickness = 2.5,
        };

        private readonly EllipseGeometry _geometry = new(new Rect(0, 0, 10, 10))
        {
            RadiusX = 5,
            RadiusY = 5,
        };

        static EasingGraph()
        {
            AffectsRender<EasingGraph>(FuncProperty, PercentProperty);
        }

        public EasingFunc? Func
        {
            get => GetValue(FuncProperty);
            set => SetValue(FuncProperty, value);
        }

        public float Percent
        {
            get => GetValue(PercentProperty);
            set => SetValue(PercentProperty, value);
        }

        public override void Render(DrawingContext context)
        {
            base.Render(context);
            var func = Func;
            var bounds = Bounds;
            var width = (int)bounds.Width;
            var height = (int)bounds.Height;

            if (func is null) return;

            for (var i = 0; i < width - 1; i++)
            {
                var value = func.EaseFunc(i, width, height, 0);
                var after = func.EaseFunc(i + 1, width, height, 0);

                context.DrawLine(_pen, new(i, value), new(i + 1, after));
            }

            var current = func.EaseFunc((Media.Frame)(Percent * 100), 100, height, 0);

            if (_geometry.Transform is not MatrixTransform)
            {
                _geometry.Transform = new MatrixTransform();
            }
            var transform = (MatrixTransform)_geometry.Transform;
            transform.Matrix = Matrix.CreateTranslation((Percent * bounds.Width) - 5, current - 5);

            context.DrawGeometry(_pen.Brush, _pen, _geometry);
        }
    }
}