using Beutl.Engine;
using Beutl.Graphics.Effects;
using Beutl.Graphics.Rendering;
using Beutl.NodeTree.Rendering;
using Beutl.Serialization;

namespace Beutl.NodeTree.Nodes.Effects;

public partial class FilterEffectNode<T> : ConfigureNode
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

    public partial class Resource
    {
        protected override void EvaluateCore(NodeRenderContext context)
        {
            var node = GetOriginal();
            FilterEffect.Resource? resource;
            var output = OutputSocket;

            if (output == null)
            {
                resource = node.Object.ToResource(context);
                OutputSocket = new FilterEffectRenderNode(resource);
            }
            else if (output is FilterEffectRenderNode { FilterEffect.Resource: { } filterEffect } fen)
            {
                resource = filterEffect;
                bool updateOnly = false;
                resource.Update(node.Object, context, ref updateOnly);
                fen.Update(resource);
            }
        }
    }
}
