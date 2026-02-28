using Beutl.Engine;

namespace Beutl.Composition;

public class CompositionContext(TimeSpan time)
{
    public static CompositionContext Default { get; } = new(TimeSpan.Zero);

    public IList<EngineObject.Resource>? Flow { get; set; }

    public TimeSpan Time { get; set; } = time;

    public virtual T Get<T>(IProperty<T> property)
    {
        if (property == null)
            throw new ArgumentNullException(nameof(property));
        return property.GetValue(this);
    }
}
