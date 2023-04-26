using System.ComponentModel.DataAnnotations;

using Beutl.Language;
using Beutl.Media;

namespace Beutl.Graphics.Shapes;

public sealed class RoundedRectShape : Shape
{
    public static readonly CoreProperty<CornerRadius> CornerRadiusProperty;
    private CornerRadius _cornerRadius;
    private RoundedRectGeometry? _geometry;

    static RoundedRectShape()
    {
        CornerRadiusProperty = ConfigureProperty<CornerRadius, RoundedRectShape>(nameof(CornerRadius))
            .Accessor(o => o.CornerRadius, (o, v) => o.CornerRadius = v)
            .DefaultValue(new CornerRadius())
            .Register();

        AffectsGeometry<RoundedRectShape>(WidthProperty, HeightProperty, CornerRadiusProperty);
    }

    [Display(Name = nameof(Strings.CornerRadius), ResourceType = typeof(Strings))]
    [Range(typeof(CornerRadius), "0", "max")]
    public CornerRadius CornerRadius
    {
        get => _cornerRadius;
        set => SetAndRaise(CornerRadiusProperty, ref _cornerRadius, value);
    }

    protected override Geometry CreateGeometry()
    {
        _geometry ??= new RoundedRectGeometry();
        _geometry.Width = Width;
        _geometry.Height = Height;
        _geometry.CornerRadius = CornerRadius;
        return _geometry;
    }

    private sealed class RoundedRectGeometry : Geometry
    {
        public static readonly CoreProperty<float> WidthProperty;
        public static readonly CoreProperty<float> HeightProperty;
        public static readonly CoreProperty<CornerRadius> CornerRadiusProperty;
        private float _width = 0;
        private float _height = 0;
        private CornerRadius _cornerRadius;

        static RoundedRectGeometry()
        {
            WidthProperty = ConfigureProperty<float, RoundedRectGeometry>(nameof(Width))
                .Accessor(o => o.Width, (o, v) => o.Width = v)
                .DefaultValue(0)
                .Register();

            HeightProperty = ConfigureProperty<float, RoundedRectGeometry>(nameof(Height))
                .Accessor(o => o.Height, (o, v) => o.Height = v)
                .DefaultValue(0)
                .Register();

            CornerRadiusProperty = ConfigureProperty<CornerRadius, RoundedRectGeometry>(nameof(CornerRadius))
                .Accessor(o => o.CornerRadius, (o, v) => o.CornerRadius = v)
                .DefaultValue(new CornerRadius())
                .Register();

            AffectsRender<RoundedRectGeometry>(WidthProperty, HeightProperty, CornerRadiusProperty);
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

        public CornerRadius CornerRadius
        {
            get => _cornerRadius;
            set => SetAndRaise(CornerRadiusProperty, ref _cornerRadius, value);
        }

        public override void ApplyTo(IGeometryContext context)
        {
            base.ApplyTo(context);
            float width = _width;
            float height = _height;
            if (float.IsInfinity(width))
                width = 0;

            if (float.IsInfinity(height))
                height = 0;

            (float radiusX, float radiusY) = (width / 2, height / 2);
            float maxRadius = Math.Max(radiusX, radiusY);
            CornerRadius cornerRadius = _cornerRadius;
            float topLeft = Math.Clamp(cornerRadius.TopLeft, 0, maxRadius);
            float topRight = Math.Clamp(cornerRadius.TopRight, 0, maxRadius);
            float bottomRight = Math.Clamp(cornerRadius.BottomRight, 0, maxRadius);
            float bottomLeft = Math.Clamp(cornerRadius.BottomLeft, 0, maxRadius);

            context.MoveTo(new Point(topLeft, 0));

            context.LineTo(new Point(width - topRight, 0));
            context.ArcTo(
                new Size(topRight, topRight),
                90,
                false,
                true,
                new Point(width, topRight));

            context.LineTo(new Point(width, height - bottomRight));
            context.ArcTo(
                new Size(bottomRight, bottomRight),
                90,
                false,
                true,
                new Point(width - bottomRight, height));

            context.LineTo(new Point(bottomLeft, height));
            context.ArcTo(
                new Size(bottomLeft, bottomLeft),
                90,
                false,
                true,
                new Point(0, height - bottomLeft));

            context.LineTo(new Point(0, topLeft));
            context.ArcTo(
                new Size(topLeft, topLeft),
                90,
                false,
                true,
                new Point(topLeft, 0));

            context.Close();
        }
    }
}
