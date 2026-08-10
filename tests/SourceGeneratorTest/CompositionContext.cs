using Beutl.Engine;

namespace Beutl.Composition;

public class CompositionContext
{
    public static CompositionContext Default => new();

    public T Get<T>(IProperty<T> property)
    {
        throw null!;
    }
}
