using BeUtl.Animation;
using BeUtl.Graphics;
using BeUtl.Graphics.Filters;
using BeUtl.Rendering;
using BeUtl.Streaming;
using BeUtl.Styling;

namespace BeUtl.Operators.Configure.ImageFilter;

#pragma warning disable IDE0065
using ImageFilter = Graphics.Filters.ImageFilter;

public abstract class ImageFilterOperator : StreamStyler
{
    private IDrawable? _previous;
    private ImageFilter? _instance;

    protected override IStyleInstance? GetInstance(IRenderable value)
    {
        if (!ReferenceEquals(_previous, value))
        {
            if (Style.TargetType.IsAssignableTo(typeof(ImageFilter)))
            {
                return Style.Instance(CreateTargetValue(Style.TargetType));
            }
            else
            {
                return null;
            }
        }
        else
        {
            return Instance;
        }
    }

    protected override void ApplyStyle(IStyleInstance instance, IRenderable value, IClock clock)
    {
        if (value is IDrawable current && instance.Target is ImageFilter target)
        {
            target.IsEnabled = IsEnabled;
            if (_previous != current)
            {
                IImageFilter? tmp = current.Filter;
                if (current.Filter is not ImageFilterGroup group)
                {
                    current.Filter = group = new ImageFilterGroup();
                    if (tmp != null)
                    {
                        group.Children.Add(tmp);
                    }
                }

                if (_previous?.Filter is ImageFilterGroup group1)
                {
                    group1.Children.Remove(target);
                }

                group.Children.Add(target);
                _previous = current;
                _instance = target;
            }

            instance.Begin();
            instance.Apply(clock);
            instance.End();
        }
    }

    protected override void OnDetachedFromLogicalTree(in LogicalTreeAttachmentEventArgs args)
    {
        base.OnDetachedFromLogicalTree(args);
        if (_previous != null
            && _previous.Filter is ImageFilterGroup group
            && _instance != null)
        {
            group.Children.Remove(_instance);
        }
    }

    private static ImageFilter CreateTargetValue(Type type)
    {
        return (ImageFilter)Activator.CreateInstance(type)!;
    }
}
