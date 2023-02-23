using System.Text.Json.Nodes;

using Beutl.Animation;
using Beutl.Graphics;
using Beutl.Graphics.Shapes;
using Beutl.Graphics.Transformation;

namespace Beutl.NodeTree.Nodes;

public sealed class SpecializedTransformGroup : Graphics.Transformation.Transform
{
    public static readonly CoreProperty<Transforms> ChildrenProperty;
    private readonly Transforms _children;
    private int _lastAcceptIndex = -1;

    static SpecializedTransformGroup()
    {
        ChildrenProperty = ConfigureProperty<Transforms, SpecializedTransformGroup>(nameof(Children))
            .Accessor(o => o.Children, (o, v) => o.Children = v)
            .PropertyFlags(PropertyFlags.All)
            .Register();
    }

    public SpecializedTransformGroup()
    {
        _children = new Transforms();
        _children.Invalidated += (_, e) => RaiseInvalidated(e);
    }

    public Transforms Children
    {
        get => _children;
        set => _children.Replace(value);
    }

    public override Matrix Value
    {
        get
        {
            Matrix value = Matrix.Identity;

            foreach (ITransform item in _children.GetMarshal().Value.Slice(0, _lastAcceptIndex + 1))
            {
                if (item.IsEnabled)
                    value = item.Value * value;
            }

            return value;
        }
    }

    public void AcceptTransform(ITransform transform)
    {
        int index = _children.IndexOf(transform);
        if (index >= 0)
        {
            _lastAcceptIndex = index;
        }
    }

    public void Begin()
    {
        _lastAcceptIndex = -1;
    }

    public void Commit()
    {
        _lastAcceptIndex = _children.Count - 1;
    }

    public override void ReadFromJson(JsonNode json)
    {
        base.ReadFromJson(json);
        if (json is JsonObject jobject)
        {
            if (jobject.TryGetPropertyValue("children", out JsonNode? childrenNode)
                && childrenNode is JsonArray childrenArray)
            {
                _children.Clear();
                _children.EnsureCapacity(childrenArray.Count);

                foreach (JsonObject childJson in childrenArray.OfType<JsonObject>())
                {
                    if (childJson.TryGetPropertyValue("@type", out JsonNode? atTypeNode)
                        && atTypeNode is JsonValue atTypeValue
                        && atTypeValue.TryGetValue(out string? atType)
                        && TypeFormat.ToType(atType) is Type type
                        && type.IsAssignableTo(typeof(Graphics.Transformation.Transform))
                        && Activator.CreateInstance(type) is Graphics.Transformation.Transform transform)
                    {
                        transform.ReadFromJson(childJson);
                        _children.Add(transform);
                    }
                }
            }
        }
    }

    public override void WriteToJson(ref JsonNode json)
    {
        base.WriteToJson(ref json);
        if (json is JsonObject jobject)
        {
            var array = new JsonArray();

            foreach (ITransform item in _children.GetMarshal().Value)
            {
                if (item is Graphics.Transformation.Transform transform)
                {
                    JsonNode node = new JsonObject();
                    transform.WriteToJson(ref node);

                    node["@type"] = TypeFormat.ToString(item.GetType());

                    array.Add(node);
                }
            }

            jobject["children"] = array;
        }
    }

    public override void ApplyAnimations(IClock clock)
    {
        base.ApplyAnimations(clock);
        foreach (ITransform item in Children.GetMarshal().Value)
        {
            (item as Animatable)?.ApplyAnimations(clock);
        }
    }
}

public class RectNode : Node
{
    private readonly OutputSocket<Rectangle> _outputSocket;
    private readonly InputSocket<float> _widthSocket;
    private readonly InputSocket<float> _heightSocket;
    private readonly InputSocket<float> _strokeSocket;

    public RectNode()
    {
        _outputSocket = AsOutput<Rectangle>("Rectangle");

        _widthSocket = AsInput<float, Rectangle>(Drawable.WidthProperty, value: 100).AcceptNumber();
        _heightSocket = AsInput<float, Rectangle>(Drawable.HeightProperty, value: 100).AcceptNumber();
        _strokeSocket = AsInput<float, Rectangle>(Rectangle.StrokeWidthProperty, value: 4000).AcceptNumber();
    }

    public override void InitializeForContext(NodeEvaluationContext context)
    {
        base.InitializeForContext(context);
        context.State = new Rectangle();
    }

    public override void UninitializeForContext(NodeEvaluationContext context)
    {
        base.UninitializeForContext(context);
        context.State = null;
    }

    public override void Evaluate(NodeEvaluationContext context)
    {
        Rectangle rectangle = context.GetOrSetState<Rectangle>();
        while (rectangle.BatchUpdate)
        {
            rectangle.EndBatchUpdate();
        }

        rectangle.BeginBatchUpdate();
        rectangle.Width = _widthSocket.Value;
        rectangle.Height = _heightSocket.Value;
        rectangle.StrokeWidth = _strokeSocket.Value;
        rectangle.BlendMode = BlendMode.SrcOver;
        rectangle.AlignmentX = Media.AlignmentX.Left;
        rectangle.AlignmentY = Media.AlignmentY.Top;
        rectangle.TransformOrigin = RelativePoint.TopLeft;
        if (rectangle.Transform is SpecializedTransformGroup transformGroup)
        {
            transformGroup.Begin();
        }
        else
        {
            rectangle.Transform = new SpecializedTransformGroup();
        }

        _outputSocket.Value = rectangle;
    }
}
