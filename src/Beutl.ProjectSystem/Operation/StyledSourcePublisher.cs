using Beutl.Animation;
using Beutl.Rendering;
using Beutl.Styling;

namespace Beutl.Operation;

public abstract class StyledSourcePublisher : StylingOperator, ISourcePublisher
{
    public IStyleInstance? Instance { get; protected set; }

    public virtual Renderable? Publish(IClock clock)
    {
        Renderable? renderable = Instance?.Target as Renderable;
        
        if (!ReferenceEquals(Style, Instance?.Source) || Instance?.Target == null)
        {
            renderable = Activator.CreateInstance(Style.TargetType) as Renderable;
            if (renderable is ICoreObject coreObj)
            {
                Instance?.Dispose();
                Instance = Style.Instance(coreObj);
            }
            else
            {
                renderable = null;
            }
        }

        OnBeforeApplying();
        if (Instance != null && IsEnabled)
        {
            Instance.Begin();
            Instance.Apply(clock);
            Instance.End();
        }

        OnAfterApplying();

        return IsEnabled ? renderable : null;
    }

    protected override void OnDetachedFromHierarchy(in HierarchyAttachmentEventArgs args)
    {
        base.OnDetachedFromHierarchy(args);
        IStyleInstance? tmp = Instance;
        Instance = null;
        tmp?.Dispose();
    }

    protected virtual void OnBeforeApplying()
    {
    }

    protected virtual void OnAfterApplying()
    {
    }
}
