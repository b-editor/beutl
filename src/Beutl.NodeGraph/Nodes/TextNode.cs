using System.ComponentModel;
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
        HideProperty(ObjectProperty);
        ObjectProperty.ValueChanged += (_, _) => OnPropertyChanged(new PropertyChangedEventArgs(nameof(Object)));
        Output = AddOutput<DrawableRenderNode?>("Output", NodePortDisplays.Output);
        Object = new TextBlock();
        Object.AlignmentX.Value = AlignmentX.Left;
        Object.AlignmentY.Value = AlignmentY.Top;
        AddInput(Object, Object.FontWeight);
        AddInput(Object, Object.FontStyle);
        AddInput(Object, Object.FontFamily);
        AddInput(Object, Object.Size);
        AddInput(Object, Object.Spacing);
        AddInput(Object, Object.Text);
        AddInput(Object, Object.Fill);
        AddInput(Object, Object.Pen);
        AddInput(Object, Object.SplitByCharacters);
        AddInput(Object, Object.TextAlignment);
    }

    public OutputPort<DrawableRenderNode?> Output { get; }

    [NotAutoSerialized]
    [SuppressResourceClassGeneration]
    public IProperty<TextBlock> ObjectProperty { get; } = new ValueOnlyProperty<TextBlock>(null!) { AffectsRender = false };

    public TextBlock Object
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
        public override void Update(GraphCompositionContext context)
        {
            var node = RequireOriginal();
            var output = Output;
            if (output?.Drawable?.Resource is not TextBlock.Resource resource)
            {
                resource = node.Object.ToResource(context);
            }
            else
            {
                bool versionBumped = false;
                resource.Reconcile(node.Object, context, ref versionBumped);
            }

            if (output == null || output.IsDisposed)
            {
                output = new DrawableRenderNode(resource);
            }
            else
            {
                output.Update(resource);
            }

            Size size = node.Object.MeasureInternal(Size.Infinity, resource);
            using (var gc2d = new GraphicsContext2D(output, size))
            {
                node.Object.Render(gc2d, resource);
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
