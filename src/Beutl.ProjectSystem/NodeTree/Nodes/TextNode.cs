using Beutl.Engine;
using Beutl.Graphics;
using Beutl.Graphics.Rendering;
using Beutl.Graphics.Shapes;
using Beutl.Media;
using Beutl.NodeTree.Rendering;
using Beutl.Serialization;

namespace Beutl.NodeTree.Nodes;

public partial class TextNode : Node
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
        Output = AddOutput<DrawableRenderNode>("Output");
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

    public OutputSocket<DrawableRenderNode?> Output { get; }

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
        public override void Update(NodeRenderContext context)
        {
            if (Renderer == null) return;
            var node = GetOriginal();
            var output = Output;
            if (output?.Drawable?.Resource is not TextBlock.Resource resource)
            {
                resource = node.Object.ToResource(context);
            }
            else
            {
                bool updateOnly = false;
                resource.Update(node.Object, context, ref updateOnly);
            }

            if (output == null)
            {
                output = new DrawableRenderNode(resource);
            }
            else
            {
                output.Update(resource);
            }

            using (var gc2d = new GraphicsContext2D(output, Renderer.FrameSize))
            {
                node.Object.Render(gc2d, resource);
            }

            Output = output;
        }
    }
}
