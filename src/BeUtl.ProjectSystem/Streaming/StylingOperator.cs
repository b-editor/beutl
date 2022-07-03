using Avalonia.Collections.Pooled;

using BeUtl.Styling;

namespace BeUtl.Streaming;

public abstract class StylingOperator : StreamOperator
{
    protected StylingOperator()
    {
        Style = OnInitializeStyle(() =>
        {
            var list = new PooledList<ISetterDescription>();
            OnInitializeSetters(list);
            return list.ConvertAll(x => x.ToSetter(this));
        });

        Style.Invalidated += (_, _) => RaiseInvalidated();
    }

    public IStyle Style { get; }

    public IStyleInstance? Instance { get; protected set; }

    protected abstract Style OnInitializeStyle(Func<IList<ISetter>> setters);

    protected virtual void OnInitializeSetters(IList<ISetterDescription> initializing)
    {
    }
}
