using BeUtl.Animation;
using BeUtl.Graphics;
using BeUtl.Graphics.Transformation;
using BeUtl.Rendering;
using BeUtl.Streaming;
using BeUtl.Styling;

namespace BeUtl.Operators.Configure.Transform;

#pragma warning disable IDE0065
using Transform = Graphics.Transformation.Transform;

public abstract class TransformOperator : StreamStyler
{
    private IDrawable? _previous;
    private Transform? _instance;

    protected override IStyleInstance? GetInstance(IRenderable value)
    {
        if (Style.TargetType.IsAssignableTo(typeof(Transform)))
        {
            return Style.Instance(CreateTargetValue(Style.TargetType));
        }
        else
        {
            return null;
        }
    }

    protected override void ApplyStyle(IStyleInstance instance, IRenderable value, IClock clock)
    {
        if (value is IDrawable current && instance.Target is Transform transform)
        {
            transform.IsEnabled = IsEnabled;
            if (_previous != current)
            {
                ITransform? tmp = current.Transform;
                if (current.Transform is not TransformGroup group)
                {
                    current.Transform = group = new TransformGroup();
                    if (tmp != null)
                    {
                        group.Children.Add(tmp);
                    }
                }

                if (_previous?.Transform is TransformGroup group1)
                {
                    group1.Children.Remove(transform);
                }

                group.Children.Add(transform);
                _previous = current;
                _instance = transform;
            }
        }
    }

    protected override void OnDetachedFromLogicalTree(in LogicalTreeAttachmentEventArgs args)
    {
        base.OnDetachedFromLogicalTree(args);
        if (_previous != null
            && _previous.Transform is TransformGroup group
            && _instance != null)
        {
            group.Children.Remove(_instance);
        }
    }

    private static Transform CreateTargetValue(Type type)
    {
        return (Transform)Activator.CreateInstance(type)!;
    }
}
