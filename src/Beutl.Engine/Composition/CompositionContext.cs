using Beutl.Engine;
using Beutl.Graphics;

namespace Beutl.Composition;

public class CompositionContext(TimeSpan time)
{
    public static CompositionContext Default { get; } = new(TimeSpan.Zero);

    public IList<EngineObject.Resource>? Flow { get; set; }

    public TimeSpan Time { get; set; } = time;

    public bool DisableResourceShare { get; init; }

    public bool UseProxyIfAvailable { get; init; }

    public float RenderScale { get; init; } = 1.0f;

    public float ScalePixel(float value) => value * RenderScale;

    public Size ScalePixel(Size size) => new(size.Width * RenderScale, size.Height * RenderScale);

    public Point ScalePixel(Point point) => new(point.X * RenderScale, point.Y * RenderScale);

    public Vector ScalePixel(Vector vector) => new(vector.X * RenderScale, vector.Y * RenderScale);

    public virtual T Get<T>(IProperty<T> property)
    {
        if (property == null)
            throw new ArgumentNullException(nameof(property));
        return property.GetValue(this);
    }
}
