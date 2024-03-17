using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using Beutl.Animation;
using Beutl.Graphics;
using Beutl.Serialization;

namespace Beutl.Media;

public sealed class PathFigure : Animatable, IAffectsRender
{
    public static readonly CoreProperty<bool> IsClosedProperty;
    public static readonly CoreProperty<Point> StartPointProperty;
    public static readonly CoreProperty<PathSegments> SegmentsProperty;
    private bool _isClosed;
    private Point _startPoint = new(float.NaN, float.NaN);
    private readonly PathSegments _segments = [];

    static PathFigure()
    {
        IsClosedProperty = ConfigureProperty<bool, PathFigure>(nameof(IsClosed))
            .Accessor(o => o.IsClosed, (o, v) => o.IsClosed = v)
            .Register();

        StartPointProperty = ConfigureProperty<Point, PathFigure>(nameof(StartPoint))
            .Accessor(o => o.StartPoint, (o, v) => o.StartPoint = v)
            .DefaultValue(new Point(float.NaN, float.NaN))
            .Register();

        SegmentsProperty = ConfigureProperty<PathSegments, PathFigure>(nameof(Segments))
            .Accessor(o => o.Segments, (o, v) => o.Segments = v)
            .Register();
    }

    public PathFigure()
    {
        AnimationInvalidated += (_, e) => Invalidated?.Invoke(this, e);
        Segments.Invalidated += (_, e) => Invalidated?.Invoke(this, e);
    }

    public event EventHandler<RenderInvalidatedEventArgs>? Invalidated;

    public bool IsClosed
    {
        get => _isClosed;
        set
        {
            if (SetAndRaise(IsClosedProperty, ref _isClosed, value))
            {
                Invalidated?.Invoke(this, new RenderInvalidatedEventArgs(this, IsClosedProperty.Name));
            }
        }
    }

    public Point StartPoint
    {
        get => _startPoint;
        set
        {
            if (SetAndRaise(StartPointProperty, ref _startPoint, value))
            {
                Invalidated?.Invoke(this, new RenderInvalidatedEventArgs(this, StartPointProperty.Name));
            }
        }
    }

    [NotAutoSerialized]
    public PathSegments Segments
    {
        get => _segments;
        set => _segments.Replace(value);
    }

    public override void Serialize(ICoreSerializationContext context)
    {
        base.Serialize(context);
        context.SetValue(nameof(Segments), Segments);
    }

    public override void Deserialize(ICoreSerializationContext context)
    {
        base.Deserialize(context);
        if (context.GetValue<PathSegments>(nameof(Segments)) is { } segments)
        {
            Segments = segments;
        }
    }

    public void ApplyTo(IGeometryContext context)
    {
        bool skipFirst = false;
        if (!StartPoint.IsInvalid)
        {
            context.MoveTo(StartPoint);
        }
        else if (Segments.Count > 0)
        {
            if (IsClosed)
            {
                if (Segments[^1].TryGetEndPoint(out Point endPoint))
                {
                    context.MoveTo(endPoint);
                }
            }
            else
            {
                if (Segments[0].TryGetEndPoint(out Point endPoint))
                {
                    context.MoveTo(endPoint);
                    skipFirst = true;
                }
            }
        }

        foreach (PathSegment item in Segments.GetMarshal().Value)
        {
            if (skipFirst)
            {
                skipFirst = false;
                continue;
            }

            item.ApplyTo(context);
        }

        if (IsClosed)
            context.Close();
    }

    public override void ApplyAnimations(IClock clock)
    {
        base.ApplyAnimations(clock);
        foreach (PathSegment item in Segments)
        {
            item.ApplyAnimations(clock);
        }
    }
}
