using Beutl.Animation;
using Beutl.Engine;

namespace Beutl.Graphics.Rendering;

public class RenderContext(IClock clock)
{
    public IClock Clock => clock;

    public T Get<T>(IProperty<T> property)
    {
        if (property == null)
            throw new ArgumentNullException(nameof(property));
        return property.GetValue(Clock);
    }
}
