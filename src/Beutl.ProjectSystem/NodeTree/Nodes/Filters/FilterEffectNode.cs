using Beutl.Engine;
using Beutl.Graphics.Effects;
using Beutl.Graphics.Rendering;
using Beutl.Serialization;

namespace Beutl.NodeTree.Nodes.Effects;

public class FilterEffectNode<T> : ConfigureNode
    where T : FilterEffect, new()
{
    public static readonly CoreProperty<T> ObjectProperty;

    static FilterEffectNode()
    {
        ObjectProperty = ConfigureProperty<T, FilterEffectNode<T>>(nameof(Object))
            .Accessor(o => o.Object, (o, v) => o.Object = v)
            .Register();

        Hierarchy<FilterEffectNode<T>>(ObjectProperty);
    }

    public FilterEffectNode()
    {
        Object = new T();
        foreach (IProperty property in Object.Properties)
        {
            AddInput(Object, property);
        }
    }

    [NotAutoSerialized]
    public T Object
    {
        get;
        set => SetAndRaise(ObjectProperty, ref field, value);
    }

    protected override void EvaluateCore(NodeEvaluationContext context)
    {
        FilterEffect.Resource? resource;

        if (OutputSocket.Value == null)
        {
            resource = Object.ToResource(new(context.Renderer.Time));
            OutputSocket.Value = new FilterEffectRenderNode(resource);
        }
        else if (OutputSocket.Value is FilterEffectRenderNode { FilterEffect.Resource: { } filterEffect } node)
        {
            resource = filterEffect;
            bool updateOnly = false;
            resource.Update(Object, new(context.Renderer.Time), ref updateOnly);
            node.Update(resource);
        }
    }

    public override void Serialize(ICoreSerializationContext context)
    {
        base.Serialize(context);
        context.SetValue("Object", Object);
    }

    public override void Deserialize(ICoreSerializationContext context)
    {
        base.Deserialize(context);
        context.Populate("Object", Object);
    }
}
