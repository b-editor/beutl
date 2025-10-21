using Beutl.Engine;

namespace Beutl.Graphics.Rendering;

public class RenderContext(TimeSpan time)
{
    public static RenderContext Default { get; } = new(TimeSpan.Zero);

    public TimeSpan Time => time;

    public T Get<T>(IProperty<T> property)
    {
        if (property == null)
            throw new ArgumentNullException(nameof(property));
        return property.GetValue(Time);
    }
}
