using Beutl.Graphics.Rendering;
using Beutl.Graphics.Shapes;
using Beutl.Media;
using Beutl.Serialization;

namespace Beutl.NodeTree.Nodes;

public class TextNode : Node
{
    public static readonly CoreProperty<TextBlock> ObjectProperty;
    private readonly OutputSocket<DrawableRenderNode> _outputSocket;

    static TextNode()
    {
        ObjectProperty = ConfigureProperty<TextBlock, TextNode>(nameof(Object))
            .Accessor(o => o.Object, (o, v) => o.Object = v)
            .Register();

        Hierarchy<TextNode>(ObjectProperty);
    }

    public TextNode()
    {
        _outputSocket = AddOutput<DrawableRenderNode>("Output");
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

    [NotAutoSerialized]
    public TextBlock Object
    {
        get;
        set => SetAndRaise(ObjectProperty, ref field, value);
    }

    public override void Evaluate(NodeEvaluationContext context)
    {
        base.Evaluate(context);
        var renderContext = new RenderContext(context.Renderer.Time);
        DrawableRenderNode? drawableNode = _outputSocket.Value;
        TextBlock.Resource? resource = drawableNode?.Drawable?.Resource as TextBlock.Resource;
        if (resource == null)
        {
            resource = Object.ToResource(renderContext);
        }
        else
        {
            bool updateOnly = false;
            resource.Update(Object, renderContext, ref updateOnly);
        }

        if (drawableNode == null)
        {
            drawableNode = new DrawableRenderNode(resource);
        }
        else
        {
            drawableNode.Update(resource);
        }

        using (var gc2d = new GraphicsContext2D(drawableNode, context.Renderer.FrameSize))
        {
            Object.Render(gc2d, resource);
        }
        
        _outputSocket.Value = drawableNode;
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
