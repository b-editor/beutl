using Beutl.Engine;

namespace Beutl.PropertyAdapters;

public sealed class SimplePropertyAdapter<T>(SimpleProperty<T> property, CoreObject obj)
    : ExpressionPropertyAdapterBase<T>(property, obj)
{
    public SimplePropertyAdapter(SimpleProperty<T> property, EngineObject obj) : this(property, (CoreObject)obj) { }
}
