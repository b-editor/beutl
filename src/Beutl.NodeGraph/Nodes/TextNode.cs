using System.Runtime.ExceptionServices;
using Beutl.Graphics;
using Beutl.Graphics.Rendering;
using Beutl.Graphics.Shapes;
using Beutl.Media;
using Beutl.NodeGraph.Composition;
using Beutl.Serialization;

namespace Beutl.NodeGraph.Nodes;

public partial class TextNode : GraphNode
{
    public static readonly CoreProperty<TextBlock> ObjectProperty;

    static TextNode()
    {
        ObjectProperty = ConfigureProperty<TextBlock, TextNode>(nameof(Object))
            .Accessor(o => o.Object, (o, v) => o.Object = v)
            .Register();

        Hierarchy<TextNode>(ObjectProperty);
    }

    public TextNode()
    {
        Output = AddOutput<DrawableRenderNode?>("Output");
        Object = new TextBlock();
        Object.AlignmentX.CurrentValue = AlignmentX.Left;
        Object.AlignmentY.CurrentValue = AlignmentY.Top;
        AddInput(Object, Object.FontWeight);
        AddInput(Object, Object.FontStyle);
        AddInput(Object, Object.FontFamily);
        AddInput(Object, Object.Size);
        AddInput(Object, Object.Spacing);
        AddInput(Object, Object.Text);
        AddInput(Object, Object.Fill);
        AddInput(Object, Object.Pen);
        AddInput(Object, Object.SplitByCharacters);
    }

    public OutputPort<DrawableRenderNode?> Output { get; }

    [NotAutoSerialized]
    public TextBlock Object
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
        private TextBlock.Resource? _textResource;

        protected override void UpdateCore(GraphCompositionContext context)
        {
            var node = GetOriginal();
            var output = Output;
            ExceptionDispatchInfo? cleanupFailure = null;
            if (_textResource == null)
            {
                _textResource = node.Object.ToResource(context);
            }
            else if (_textResource.GetOriginal() != node.Object)
            {
                TextBlock.Resource replacement = node.Object.ToResource(context);
                cleanupFailure = ReplaceOwnedResource(ref _textResource, replacement);
            }
            else
            {
                bool updateOnly = false;
                _textResource.Update(node.Object, context, ref updateOnly);
            }

            TextBlock.Resource textResource = _textResource!;
            if (output == null || output.IsDisposed)
            {
                output = new DrawableRenderNode(textResource);
            }
            else
            {
                output.Update(textResource);
            }

            Size size = node.Object.MeasureInternal(Size.Infinity, textResource);
            using (var gc2d = new GraphicsContext2D(output, size))
            {
                node.Object.Render(gc2d, textResource);
            }

            Output = output;
            cleanupFailure?.Throw();
        }

        partial void PrepareResourceDispose(bool disposing, GeneratedResourceCleanupContext context)
        {
            if (disposing)
            {
                context.Reserve(_textResource);
            }
        }

        partial void PostDispose(bool disposing)
        {
            if (!disposing)
                return;

            DrawableRenderNode? output = Output;
            Output = null;
            _textResource = null;

            Exception? failure = null;
            DisposeOwnedResources(ref failure, output);
            ThrowIfCleanupFailed(failure);
        }
    }
}
