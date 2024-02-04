using System.Text.Json.Nodes;

using Beutl.Animation;
using Beutl.Collections;
using Beutl.Configuration;
using Beutl.Extensibility;
using Beutl.Graphics;
using Beutl.Rendering;
using Beutl.Serialization;
using Beutl.Styling;

namespace Beutl.Operation;

public abstract class StyledSourcePublisher : StylingOperator, ISourcePublisher
{
    protected StyledSourcePublisher()
    {
        ViewConfig viewConfig = GlobalConfiguration.Instance.ViewConfig;
        if (viewConfig.HidePrimaryProperties)
        {
            RemovePrimaryProperties();
        }
    }

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

    public override void Deserialize(ICoreSerializationContext context)
    {
        base.Deserialize(context);
        ViewConfig viewConfig = GlobalConfiguration.Instance.ViewConfig;
        if (viewConfig.HidePrimaryProperties)
        {
            RemovePrimaryProperties();
        }
    }

    private void RemovePrimaryProperties()
    {
        ICoreList<IAbstractProperty> props = Properties;
        for (int i = props.Count - 1; i >= 0; i--)
        {
            if (IsPrimaryProperty(props[i]))
            {
                props.RemoveAt(i);
            }
        }
    }

    private static bool IsPrimaryProperty(IAbstractProperty property)
    {
        ViewConfig viewConfig = GlobalConfiguration.Instance.ViewConfig;
        CoreProperty? coreProp = property.GetCoreProperty();
        if (coreProp != null && coreProp.OwnerType.IsAssignableTo(typeof(Drawable)))
        {
            return viewConfig.PrimaryProperties.Contains(coreProp.Name);
        }

        return false;
    }
}
