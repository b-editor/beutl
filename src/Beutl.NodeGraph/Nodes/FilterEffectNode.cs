using System.ComponentModel;
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
        HideProperty(ObjectProperty);
        ObjectProperty.ValueChanged += (_, _) => OnPropertyChanged(new PropertyChangedEventArgs(nameof(Object)));
        Object = new T();
        foreach (IProperty property in Object.Properties)
        {
            AddInput(Object, property);
        }
    }

    [NotAutoSerialized]
    [SuppressResourceClassGeneration]
    public IProperty<T> ObjectProperty { get; } = new ValueOnlyProperty<T>(null!) { AffectsRender = false };

    public T Object
    {
        get => ObjectProperty.Value;
        set => ObjectProperty.Value = value;
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
        protected override void UpdateCore(GraphCompositionContext context)
        {
            var node = RequireOriginal();
            FilterEffect.Resource? resource;
            var output = OutputPort;

            if (output == null || output.IsDisposed)
            {
                resource = node.Object.ToResource(context);
                OutputPort = resource.CreateRenderNode();
            }
            else if (output is FilterEffectRenderNode { FilterEffect.Resource: { } filterEffect } fen)
            {
                resource = filterEffect;
                bool versionBumped = false;
                resource.Reconcile(node.Object, context, ref versionBumped);
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
