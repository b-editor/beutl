using Beutl.Engine;
using Beutl.Graphics.Effects;
using Beutl.Graphics.Rendering;
using Beutl.NodeGraph.Composition;
using Beutl.Serialization;

namespace Beutl.NodeGraph.Nodes;

public partial class FilterEffectNode<T> : ConfigureNode
    where T : FilterEffect, new()
{
    public FilterEffectNode()
    {
        ScanProperties<FilterEffectNode<T>>();
        HideProperty(Object);
        Object.Value = new T();
        // The object's base fields, such as IsEnabled, are not inputs.
        foreach (IProperty property in Object.Value.Properties.Where(property => !Object.Value.IsBaseField(property)))
        {
            AddInput(Object.Value, property);
        }
    }

    [NotAutoSerialized]
    [SuppressResourceClassGeneration]
    public IProperty<T> Object { get; } = new ValueOnlyProperty<T>(null!) { AffectsRender = false };

    public override void Serialize(ICoreSerializationContext context)
    {
        base.Serialize(context);
        context.SetValue("Object", Object.Value);
    }

    public override void Deserialize(ICoreSerializationContext context)
    {
        base.Deserialize(context);
        context.Populate("Object", Object.Value);
    }

    public partial class Resource
    {
        protected override void UpdateCore(GraphCompositionContext context)
        {
            var node = RequireOriginal();
            FilterEffect.Resource? resource;
            var output = OutputPort;

            if (output == null || output.IsDisposed)
            {
                resource = node.Object.Value.ToResource(context);
                OutputPort = resource.CreateRenderNode();
            }
            else if (output is FilterEffectRenderNode { FilterEffect.Resource: { } filterEffect } fen)
            {
                resource = filterEffect;
                bool versionBumped = false;
                resource.Reconcile(node.Object.Value, context, ref versionBumped);
                fen.Update(resource);
            }
        }

        partial void PostDispose(bool disposing)
        {
            if (OutputPort is FilterEffectRenderNode { FilterEffect.Resource: { } filterEffect })
            {
                filterEffect.Dispose();
            }

            OutputPort?.Dispose();
            OutputPort = null;
        }
    }
}
