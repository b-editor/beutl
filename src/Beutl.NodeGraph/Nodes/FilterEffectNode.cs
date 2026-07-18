using System.Runtime.ExceptionServices;
using Beutl.Engine;
using Beutl.Graphics.Effects;
using Beutl.Graphics.Rendering;
using Beutl.NodeGraph.Composition;
using Beutl.Serialization;

namespace Beutl.NodeGraph.Nodes;

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
        private FilterEffect.Resource? _filterEffectResource;

        protected override void UpdateConfiguredCore(GraphCompositionContext context)
        {
            var node = GetOriginal();
            var output = OutputPort;
            ExceptionDispatchInfo? failure = null;

            if (_filterEffectResource == null)
            {
                _filterEffectResource = node.Object.ToResource(context);
            }
            else if (_filterEffectResource.GetOriginal() != node.Object)
            {
                FilterEffect.Resource replacement = node.Object.ToResource(context);
                failure = ReplaceOwnedResource(ref _filterEffectResource, replacement);
            }
            else
            {
                bool updateOnly = false;
                _filterEffectResource.Update(node.Object, context, ref updateOnly);
            }

            FilterEffect.Resource filterEffectResource = _filterEffectResource!;
            FilterEffectRenderNodeFactory factory = filterEffectResource.ResolveRenderNodeFactory().Factory;
            if (output is not FilterEffectRenderNode fen || output.IsDisposed)
            {
                OutputPort = factory.Create(filterEffectResource);
            }
            else
            {
                if (!factory.Matches(fen))
                {
                    FilterEffectRenderNode replacement = factory.Create(filterEffectResource);
                    replacement.BringFrom(output);
                    OutputPort = replacement;
                    try
                    {
                        output.Dispose();
                    }
                    catch (Exception ex)
                    {
                        failure ??= ExceptionDispatchInfo.Capture(ex);
                    }
                }
                else
                {
                    fen.Update(filterEffectResource);
                }
            }

            failure?.Throw();
        }

        partial void PrepareResourceDispose(bool disposing, GeneratedResourceCleanupContext context)
        {
            if (disposing)
            {
                context.Reserve(_filterEffectResource);
            }
        }

        partial void PostDispose(bool disposing)
        {
            if (!disposing)
                return;

            RenderNode? output = OutputPort;
            OutputPort = null;
            _filterEffectResource = null;

            Exception? failure = null;
            DisposeOwnedResources(ref failure, output);
            ThrowIfCleanupFailed(failure);
        }
    }
}
