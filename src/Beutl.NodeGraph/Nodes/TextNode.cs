using Beutl.Engine;
using Beutl.Graphics;
using Beutl.Graphics.Rendering;
using Beutl.Graphics.Shapes;
using Beutl.Media;
using Beutl.NodeGraph.Composition;
using Beutl.Serialization;

namespace Beutl.NodeGraph.Nodes;

public partial class TextNode : GraphNode
{
    public TextNode()
    {
        ScanProperties<TextNode>();
        HideProperty(Object);
        Output = AddOutput<DrawableRenderNode?>("Output", NodePortDisplays.Output);
        Object.Value = new TextBlock();
        Object.Value.AlignmentX.Value = AlignmentX.Left;
        Object.Value.AlignmentY.Value = AlignmentY.Top;
        AddInput(Object.Value, Object.Value.FontWeight);
        AddInput(Object.Value, Object.Value.FontStyle);
        AddInput(Object.Value, Object.Value.FontFamily);
        AddInput(Object.Value, Object.Value.Size);
        AddInput(Object.Value, Object.Value.Spacing);
        AddInput(Object.Value, Object.Value.Text);
        AddInput(Object.Value, Object.Value.Fill);
        AddInput(Object.Value, Object.Value.Pen);
        AddInput(Object.Value, Object.Value.SplitByCharacters);
        AddInput(Object.Value, Object.Value.TextAlignment);
    }

    public OutputPort<DrawableRenderNode?> Output { get; }

    [NotAutoSerialized]
    [SuppressResourceClassGeneration]
    public IProperty<TextBlock> Object { get; } = new ValueOnlyProperty<TextBlock>(null!) { AffectsRender = false };

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
        public override void Update(GraphCompositionContext context)
        {
            var node = RequireOriginal();
            var output = Output;
            if (output?.Drawable?.Resource is not TextBlock.Resource resource)
            {
                resource = node.Object.Value.ToResource(context);
            }
            else
            {
                bool versionBumped = false;
                resource.Reconcile(node.Object.Value, context, ref versionBumped);
            }

            if (output == null || output.IsDisposed)
            {
                output = new DrawableRenderNode(resource);
            }
            else
            {
                output.Update(resource);
            }

            Size size = node.Object.Value.MeasureInternal(Size.Infinity, resource);
            using (var gc2d = new GraphicsContext2D(output, size))
            {
                resource.Render(gc2d);
            }

            Output = output;
        }

        partial void PostDispose(bool disposing)
        {
            Output?.Dispose();
            Output = null;
        }
    }
}
