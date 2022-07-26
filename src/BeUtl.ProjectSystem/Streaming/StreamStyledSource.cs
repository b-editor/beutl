using BeUtl.Animation;
using BeUtl.Rendering;
using BeUtl.Styling;

namespace BeUtl.Streaming;

public abstract class StreamStyledSource : StylingOperator, IStreamSource
{
    public virtual IRenderable? Publish(IClock clock)
    {
        OnPrePublish();
        IRenderable? renderable = Instance?.Target as IRenderable;
        
        if (!ReferenceEquals(Style, Instance?.Source) || Instance?.Target == null)
        {
            renderable = Activator.CreateInstance(Style.TargetType) as IRenderable;
            if (renderable is IStyleable styleable)
            {
                Instance = Style.Instance(styleable);
            }
            else
            {
                renderable = null;
            }
        }

        if (Instance != null && IsEnabled)
        {
            Instance.Begin();
            Instance.Apply(clock);
            Instance.End();
        }

        OnPostPublish();

        return IsEnabled ? renderable : null;
    }

    protected virtual void OnPrePublish()
    {
    }

    protected virtual void OnPostPublish()
    {
    }
}
