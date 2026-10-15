using Beutl.Engine;

namespace Beutl.PropertyAdapters;

public sealed class SimplePropertyAdapter<T>(SimpleProperty<T> property, EngineObject obj)
    : ExpressionPropertyAdapterBase<T>(property, obj);
