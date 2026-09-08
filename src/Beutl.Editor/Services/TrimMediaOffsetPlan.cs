using Beutl.Audio;
using Beutl.Engine;
using Beutl.Graphics;
using Beutl.ProjectSystem;

namespace Beutl.Editor.Services;

// Proves how a trim moves each source clock before any geometry or offset is written.
// Unknown/nonlinear presenters are deliberately unsupported: an in-point correction alone
// cannot preserve the retained content of a nonlinear time mapping.
internal sealed class TrimMediaOffsetPlan
{
    private readonly Dictionary<IProperty<TimeSpan>, double> _scales = new();
    private readonly HashSet<Element> _movingElements;

    private TrimMediaOffsetPlan(IEnumerable<Element> movingElements)
    {
        _movingElements = movingElements.ToHashSet();
    }

    public static bool TryCreate(
        IEnumerable<Element> backs,
        IEnumerable<Element> middles,
        out TrimMediaOffsetPlan plan)
    {
        Element[] backArray = backs.ToArray();
        Element[] middleArray = middles.ToArray();
        plan = new TrimMediaOffsetPlan(backArray.Concat(middleArray));
        foreach (Element back in backArray)
        {
            foreach (EngineObject obj in back.Objects)
            {
                if (!plan.Visit(obj, back.Start, 0, false, new HashSet<object>(ReferenceEqualityComparer.Instance)))
                    return false;
            }
        }
        foreach (Element middle in middleArray)
        {
            foreach (EngineObject obj in middle.Objects)
            {
                // A middle is translated with its content, so evaluate at the translated clock.
                if (!plan.Visit(obj, middle.Start, 1, true, new HashSet<object>(ReferenceEqualityComparer.Instance)))
                    return false;
            }
        }
        return true;
    }

    private bool StartMoves(EngineObject obj)
    {
        if (_movingElements.Any(element => element.Objects.Contains(obj))) return true;
        if (obj.IsTimeAnchor) return false;
        EngineObject? parent = obj.FindHierarchicalParent<EngineObject>();
        return parent != null && StartMoves(parent);
    }

    private static bool IsStatic(IProperty property)
        => !property.HasExpression && property.Animation == null;

    private static bool TrySpeed(IProperty<float> property, out double speed)
    {
        speed = property.CurrentValue / 100d;
        return IsStatic(property) && double.IsFinite(speed) && speed >= 0;
    }

    private bool AddMedia(
        EngineObject media, IProperty<TimeSpan> offset, IProperty<float> speedProperty,
        TimeSpan rootStart, double clockShift, bool middle)
    {
        if (!IsStatic(offset) || offset.CurrentValue < TimeSpan.Zero
            || !TrySpeed(speedProperty, out double speed)) return false;
        // Aligned moving origins keep the retained media clock nonnegative. A differently
        // anchored moving source can cross the video's negative-position wrap at the cut.
        if (StartMoves(media) && media.TimeRange.Start != rootStart) return false;
        double scale = speed * ((StartMoves(media) ? 1 : 0) - clockShift);
        if (!double.IsFinite(scale) || scale < 0 || middle && scale != 0) return false;
        if (middle) return true;
        if (_scales.TryGetValue(offset, out double previous)) return previous == scale;
        _scales.Add(offset, scale);
        return true;
    }

    private bool Visit(EngineObject obj, TimeSpan rootStart, double clockShift, bool middle, HashSet<object> active)
    {
        // Flow operators can consume resources other than their declared Target/Children.
        // Their effective mapping cannot be proven by walking the property graph alone.
        if (obj is IFlowOperator or PortalObject) return false;
        if (!active.Add(obj)) return false;
        try
        {
            switch (obj)
            {
                case SourceVideo video:
                    return video.GetType() == typeof(SourceVideo)
                        && IsStatic(video.Source) && IsStatic(video.IsLoop) && !video.IsLoop.CurrentValue
                        && AddMedia(video, video.OffsetPosition, video.Speed, rootStart, clockShift, middle);
                case SourceSound sound:
                    return AddMedia(sound, sound.OffsetPosition, sound.Speed, rootStart, clockShift, middle);
                case SceneSound sound:
                    return AddMedia(sound, sound.OffsetPosition, sound.Speed, rootStart, clockShift, middle);
                case DrawablePresenter presenter:
                    return IsStatic(presenter.Target)
                        && (presenter.Target.CurrentValue is not { } presented
                            || Visit(presented, rootStart, clockShift, middle, active));
                case ITimeMappingPresenter:
                case ITargetStatePresenter:
                case IPresenter<Drawable>:
                    return false;
                default:
                    return true;
            }
        }
        finally
        {
            active.Remove(obj);
        }
    }

    public (TimeSpan Min, TimeSpan Max) GetBounds()
    {
        long backward = long.MaxValue;
        long forward = long.MaxValue;
        foreach ((IProperty<TimeSpan> offset, double scale) in _scales)
        {
            if (scale == 0) continue;
            backward = Math.Min(backward, TimelineTicks(offset.CurrentValue.Ticks, scale));
            forward = Math.Min(forward, TimelineTicks(long.MaxValue - offset.CurrentValue.Ticks, scale));
        }
        return (TimeSpan.FromTicks(-backward), TimeSpan.FromTicks(forward));
    }

    private static long TimelineTicks(long sourceTicks, double scale)
    {
        double ticks = Math.Floor(sourceTicks / scale);
        return ticks >= long.MaxValue ? long.MaxValue : (long)ticks;
    }

    public bool TryPrepare(TimeSpan delta, out Dictionary<IProperty<TimeSpan>, TimeSpan> values)
    {
        values = new();
        foreach ((IProperty<TimeSpan> offset, double scale) in _scales)
        {
            if (scale == 0) continue;
            try
            {
                decimal ticks = offset.CurrentValue.Ticks + decimal.Truncate(delta.Ticks * (decimal)scale);
                if (ticks < 0 || ticks > long.MaxValue) return false;
                values.Add(offset, TimeSpan.FromTicks((long)ticks));
            }
            catch (OverflowException)
            {
                return false;
            }
        }
        return true;
    }
}
