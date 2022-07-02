using BeUtl.Animation;
using BeUtl.Rendering;
using BeUtl.Styling;

namespace BeUtl.Streaming;

public abstract class StreamStyler : StreamSelector
{
    public abstract IStyle Style { get; }

    public IStyleInstance? Instance { get; protected set; }

    public override IRenderable? Select(IRenderable? value, IClock clock)
    {
        OnPreSelect(value);
        if (value == null)
            return null;

        Type type = value.GetType();
        if (!ReferenceEquals(Instance?.Target, value))
        {
            Instance?.Dispose();

            if (Style.TargetType.IsAssignableTo(type) && value is IStyleable styleable)
            {
                Instance = Style.Instance(styleable);
            }
            else
            {
                Instance = null;
            }
        }

        if (Instance != null)
        {
            Instance.IsEnabled = IsEnabled;
            Instance.Begin();
            Instance.Apply(clock);
            Instance.End();
        }

        OnPostSelect(value);

        return value;
    }

    protected virtual void OnPreSelect(IRenderable? value)
    {
    }

    protected virtual void OnPostSelect(IRenderable? value)
    {
    }
}
