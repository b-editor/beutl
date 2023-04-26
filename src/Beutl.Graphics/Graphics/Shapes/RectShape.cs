using Beutl.Media;

namespace Beutl.Graphics.Shapes;

public sealed class RectShape : Shape
{
    private RectGeometry? _geometry;

    static RectShape()
    {
        AffectsGeometry<RectShape>(WidthProperty, HeightProperty);
    }

    protected override Geometry CreateGeometry()
    {
        _geometry ??= new RectGeometry();
        _geometry.Width = Width;
        _geometry.Height = Height;
        return _geometry;
    }

    private sealed class RectGeometry : Geometry
    {
        public static readonly CoreProperty<float> WidthProperty;
        public static readonly CoreProperty<float> HeightProperty;
        private float _width = 0;
        private float _height = 0;

        static RectGeometry()
        {
            WidthProperty = ConfigureProperty<float, RectGeometry>(nameof(Width))
                .Accessor(o => o.Width, (o, v) => o.Width = v)
                .DefaultValue(0)
                .Register();

            HeightProperty = ConfigureProperty<float, RectGeometry>(nameof(Height))
                .Accessor(o => o.Height, (o, v) => o.Height = v)
                .DefaultValue(0)
                .Register();

            AffectsRender<RectGeometry>(WidthProperty, HeightProperty);
        }

        public float Width
        {
            get => _width;
            set => SetAndRaise(WidthProperty, ref _width, value);
        }

        public float Height
        {
            get => _height;
            set => SetAndRaise(HeightProperty, ref _height, value);
        }

        public override void ApplyTo(IGeometryContext context)
        {
            base.ApplyTo(context);
            float width = Width;
            float height = Height;
            if (float.IsInfinity(width))
                width = 0;

            if (float.IsInfinity(height))
                height = 0;

            context.MoveTo(new Point(0, 0));
            context.LineTo(new Point(width, 0));
            context.LineTo(new Point(width, height));
            context.LineTo(new Point(0, height));
            context.LineTo(new Point(0, 0));
            context.Close();
        }
    }
}
