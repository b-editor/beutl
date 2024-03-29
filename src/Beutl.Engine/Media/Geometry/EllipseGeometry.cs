﻿using Beutl.Graphics;

namespace Beutl.Media;

public sealed class EllipseGeometry : Geometry
{
    public static readonly CoreProperty<float> WidthProperty;
    public static readonly CoreProperty<float> HeightProperty;
    private float _width = 0;
    private float _height = 0;

    static EllipseGeometry()
    {
        WidthProperty = ConfigureProperty<float, EllipseGeometry>(nameof(Width))
            .Accessor(o => o.Width, (o, v) => o.Width = v)
            .DefaultValue(0)
            .Register();

        HeightProperty = ConfigureProperty<float, EllipseGeometry>(nameof(Height))
            .Accessor(o => o.Height, (o, v) => o.Height = v)
            .DefaultValue(0)
            .Register();

        AffectsRender<EllipseGeometry>(WidthProperty, HeightProperty);
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

        float radiusX = width / 2;
        float radiusY = height / 2;
        var radius = new Size(radiusX, radiusY);

        context.MoveTo(new Point(radiusX, 0));
        context.ArcTo(radius, 0, true, false, new Point(radiusX, height));
        context.ArcTo(radius, 0, true, false, new Point(radiusX, 0));
        context.Close();
    }
}
