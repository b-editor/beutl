using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Xaml.Interactivity;

namespace BeUtl.Controls;

public sealed class RoundedClippingBehavior : Behavior<Control>
{
    public static readonly StyledProperty<CornerRadius> CornerRadiusProperty
        = AvaloniaProperty.Register<RoundedClippingBehavior, CornerRadius>("CornerRadius");
    private IDisposable _disposable;
    private IDisposable _disposable1;

    public CornerRadius CornerRadius
    {
        get => GetValue(CornerRadiusProperty);
        set => SetValue(CornerRadiusProperty, value);
    }

    protected override void OnAttached()
    {
        base.OnAttached();
        if (AssociatedObject != null)
        {
            _disposable = AssociatedObject.GetObservable(Visual.BoundsProperty)
                .Subscribe(_ => Invalidate());

            _disposable1 = this.GetObservable(CornerRadiusProperty).Subscribe(_ => Invalidate());
        }
    }

    protected override void OnDetaching()
    {
        base.OnDetaching();
        _disposable?.Dispose();
        _disposable1?.Dispose();
    }

    private void Invalidate()
    {
        // Avalonia.Controls.Shapes.Rectangle
        const double PiOver2 = 1.57079633; // 90 deg to rad
        var rect = new Rect(AssociatedObject!.Bounds.Size);
        var cornerRadius = CornerRadius;
        var geometry = new StreamGeometry();
        using (StreamGeometryContext context = geometry.Open())
        {
            // The rectangle is constructed as follows:
            //
            //   (origin)
            //   Corner 4            Corner 1
            //   Top/Left  Line 1    Top/Right
            //      \_   __________   _/
            //          |          |
            //   Line 4 |          | Line 2
            //       _  |__________|  _
            //      /      Line 3      \
            //   Corner 3            Corner 2
            //   Bottom/Left         Bottom/Right
            //
            // - Lines 1,3 follow the deflated rectangle bounds minus RadiusX
            // - Lines 2,4 follow the deflated rectangle bounds minus RadiusY
            // - All corners are constructed using elliptical arcs 

            // Line 1 + Corner 1
            context.BeginFigure(new Point(rect.Left + cornerRadius.TopLeft, rect.Top), true);
            context.LineTo(new Point(rect.Right - cornerRadius.TopRight, rect.Top));
            context.ArcTo(
                new Point(rect.Right, rect.Top + cornerRadius.TopRight),
                new Size(cornerRadius.TopRight, cornerRadius.TopRight),
                rotationAngle: PiOver2,
                isLargeArc: false,
                SweepDirection.Clockwise);

            // Line 2 + Corner 2
            context.LineTo(new Point(rect.Right, rect.Bottom - cornerRadius.BottomRight));
            context.ArcTo(
                new Point(rect.Right - cornerRadius.BottomRight, rect.Bottom),
                new Size(cornerRadius.BottomRight, cornerRadius.BottomRight),
                rotationAngle: PiOver2,
                isLargeArc: false,
                SweepDirection.Clockwise);

            // Line 3 + Corner 3
            context.LineTo(new Point(rect.Left + cornerRadius.BottomLeft, rect.Bottom));
            context.ArcTo(
                new Point(rect.Left, rect.Bottom - cornerRadius.BottomLeft),
                new Size(cornerRadius.BottomLeft, cornerRadius.BottomLeft),
                rotationAngle: PiOver2,
                isLargeArc: false,
                SweepDirection.Clockwise);

            // Line 4 + Corner 4
            context.LineTo(new Point(rect.Left, rect.Top + cornerRadius.TopLeft));
            context.ArcTo(
                new Point(rect.Left + cornerRadius.TopLeft, rect.Top),
                new Size(cornerRadius.TopLeft, cornerRadius.TopLeft),
                rotationAngle: PiOver2,
                isLargeArc: false,
                SweepDirection.Clockwise);

            context.EndFigure(true);
        }

        AssociatedObject.Clip = geometry;
    }
}

public class ImageClipping : AvaloniaObject
{
    public static readonly AttachedProperty<CornerRadius> CornerRadiusProperty
        = AvaloniaProperty.RegisterAttached<ImageClipping, Control, CornerRadius>("CornerRadius");

    static ImageClipping()
    {
        CornerRadiusProperty.Changed.Subscribe(args =>
        {
            if (args.Sender is Control control && args.NewValue.HasValue)
            {
                BehaviorCollection collection = Interaction.GetBehaviors(control);
                if (collection.FirstOrDefault(x => x is RoundedClippingBehavior) is RoundedClippingBehavior exists)
                {
                    exists.CornerRadius = args.NewValue.Value;
                }
                else
                {
                    collection.Add(new RoundedClippingBehavior()
                    {
                        CornerRadius = args.NewValue.Value,
                    });
                }
            }
        });
    }

    public static void SetCornerRadius(Control obj, CornerRadius value)
    {
        obj.SetValue(CornerRadiusProperty, value);
    }

    public static CornerRadius GetCornerRadius(Control obj)
    {
        return obj.GetValue(CornerRadiusProperty);
    }
}
