using Beutl.Configuration;
using Beutl.Media;

namespace Beutl.ProjectSystem;

public partial class Scene
{
    private int NearestLayerNumber(Element element)
    {
        if (IsOverlapping(element.Range, element.ZIndex))
        {
            int layerMax = Children.Max(i => i.ZIndex);

            // 使うことができるレイヤー番号。ロックされたレイヤーには自動配置しない。
            var numbers = new List<int>();

            for (int l = 0; l <= layerMax; l++)
            {
                if (!IsLayerLocked(l)
                    && !Children.Any(i => i.ZIndex == l && i.Range.Intersects(element.Range)))
                {
                    numbers.Add(l);
                }
            }

            if (numbers.Count < 1)
            {
                int next = layerMax + 1;
                while (IsLayerLocked(next)) next++;
                return next;
            }

            return numbers.Nearest(element.ZIndex);
        }

        return element.ZIndex;
    }

    internal (Element? Before, Element? After, Element? Cover) GetBeforeAndAfterAndCover(Element element)
    {
        Element? beforeTmp = null;
        Element? afterTmp = null;
        Element? coverTmp = null;
        TimeRange range = element.Range;

        foreach (Element? item in Children.AsSpan())
        {
            if (item != element && item.ZIndex == element.ZIndex)
            {
                Element.AccumulateNeighbor(item, range, ref beforeTmp, ref afterTmp, ref coverTmp);
            }
        }

        return (beforeTmp, afterTmp, coverTmp);
    }

    private bool IsOverlapping(TimeRange timeRange, int zindex)
    {
        return Children.Any(i =>
        {
            if (i.ZIndex == zindex)
            {
                if (i.Range == timeRange
                    || i.Range.Intersects(timeRange)
                    || i.Range.Contains(timeRange)
                    || timeRange.Contains(i.Range))
                {
                    return true;
                }
            }

            return false;
        });
    }

    private bool IsFreeRange(TimeRange range, int zindex)
    {
        return range.Duration > MediaTime.Zero && !IsOverlapping(range, zindex);
    }

    private (TimeRange Range, int ZIndex) GetCorrectPosition(Element element, ElementOverlapHandling handling)
    {
        bool overlapping = IsOverlapping(element.Range, element.ZIndex);

        if (!overlapping || handling.HasFlag(ElementOverlapHandling.Allow))
            return (element.Range, element.ZIndex);

        if (handling == ElementOverlapHandling.ThrowException)
            throw new InvalidOperationException("要素の位置が無効です");

        (Element? before, Element? after, Element? cover) = GetBeforeAndAfterAndCover(element);
        var candidateStart = new List<MediaTime>(2);
        var candidateEnd = new List<MediaTime>(2);
        if (cover != null)
        {
            candidateEnd.Add(cover.Start);
            candidateStart.Add(cover.Range.End);
        }

        if (after != null) candidateEnd.Add(after.Start);
        if (before != null) candidateStart.Add(before.Range.End);

        MediaTime start = element.Start;
        MediaTime end = element.Range.End;

        if (handling.HasFlag(ElementOverlapHandling.Start) && handling.HasFlag(ElementOverlapHandling.Length))
        {
            foreach (MediaTime cEnd in candidateEnd)
            {
                TimeRange range = TimeRange.FromRange(start, cEnd);
                if (IsFreeRange(range, element.ZIndex))
                {
                    return (range, element.ZIndex);
                }

                foreach (MediaTime cStart in candidateStart)
                {
                    range = TimeRange.FromRange(cStart, cEnd);
                    if (IsFreeRange(range, element.ZIndex))
                    {
                        return (range, element.ZIndex);
                    }
                }
            }
        }

        if (handling.HasFlag(ElementOverlapHandling.Length))
        {
            foreach (MediaTime item in candidateEnd)
            {
                TimeRange range = TimeRange.FromRange(start, item);
                if (IsFreeRange(range, element.ZIndex))
                {
                    return (range, element.ZIndex);
                }
            }
        }

        if (handling.HasFlag(ElementOverlapHandling.Start))
        {
            foreach (MediaTime item in candidateStart)
            {
                TimeRange range = TimeRange.FromRange(item, end);
                if (IsFreeRange(range, element.ZIndex))
                {
                    return (range, element.ZIndex);
                }
            }
        }

        return (element.Range, NearestLayerNumber(element));
    }

    private void AddChildCore(Element element, ElementOverlapHandling overlapHandling)
    {
        bool adjustSceneDuration = GlobalConfiguration.Instance.EditorConfig.AutoAdjustSceneDuration;
        (TimeRange range, int zIndex) = GetCorrectPosition(element, overlapHandling);
        element.Start = range.Start;
        element.Length = range.Duration;
        element.ZIndex = zIndex;
        Children.Add(element);

        if (adjustSceneDuration && Duration + Start < range.End)
        {
            Duration = range.End - Start;
        }
    }

    private void MoveChildCore(int zIndex, MediaTime newStart, MediaTime newLength, Element element)
    {
        bool adjustSceneDuration = GlobalConfiguration.Instance.EditorConfig.AutoAdjustSceneDuration;
        MediaTime newEnd = newStart + newLength;
        (Element? before, Element? after, Element? cover) =
            element.GetBeforeAndAfterAndCover(zIndex, newStart, newEnd);

        if (before != null && before.Range.End >= newStart)
        {
            if ((after != null && (after.Start - before.Range.End) >= newLength) || after == null)
            {
                element.Start = before.Range.End;
                element.Length = newLength;
                element.ZIndex = zIndex;
            }
        }
        else if (after != null && after.Start < newEnd)
        {
            MediaTime ns = after.Start - newLength;
            if (((before != null && (after.Start - before.Range.End) >= newLength) || before == null) &&
                ns >= MediaTime.Zero)
            {
                element.Start = ns;
                element.Length = newLength;
                element.ZIndex = zIndex;
            }
        }
        else if (cover == null)
        {
            element.Start = newStart;
            element.Length = newLength;
            element.ZIndex = zIndex;
        }

        TimeRange range = element.Range;
        if (adjustSceneDuration && Duration + Start < range.End)
        {
            Duration = range.End - Start;
        }
    }

    private void MoveChildrenCore(Element[] elements, int deltaZIndex, MediaTime deltaTime)
    {
        foreach (Element item in elements)
        {
            if (!HasConflict(elements, deltaZIndex, deltaTime))
            {
                break;
            }

            MediaTime? newDeltaStart = DeltaStart(item, elements, deltaZIndex, deltaTime);
            if (newDeltaStart.HasValue)
            {
                deltaTime = newDeltaStart.Value;
            }
        }

        bool conflict = HasConflict(elements, deltaZIndex, deltaTime);
        bool adjustSceneDuration = GlobalConfiguration.Instance.EditorConfig.AutoAdjustSceneDuration;
        MediaTime newSceneDuration = Duration;
        if (adjustSceneDuration)
        {
            MediaTime maxEndingTime = elements.Max(i => i.Range.End + deltaTime);
            if (newSceneDuration + Start < maxEndingTime)
            {
                newSceneDuration = maxEndingTime - Start;
            }
        }

        if (!conflict)
        {
            foreach (Element item in elements)
            {
                item.Start += deltaTime;
                item.ZIndex += deltaZIndex;
            }

            if (adjustSceneDuration)
            {
                Duration = newSceneDuration;
            }
        }
    }

    private bool HasConflict(Element[] elements, int deltaZIndex, MediaTime deltaTime)
    {
        Element[] others = Children.Except(elements).ToArray();
        foreach (Element item in elements)
        {
            TimeRange newRange = item.Range.AddStart(deltaTime);
            int newLayer = item.ZIndex + deltaZIndex;
            if (newLayer < 0 || newRange.Start < MediaTime.Zero)
                return true;

            foreach (Element other in others)
            {
                if (other.ZIndex == newLayer && other.Range.Intersects(newRange))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static MediaTime? DeltaStart(Element element, Element[] elements, int deltaZIndex, MediaTime deltaTime)
    {
        MediaTime newStart = element.Start + deltaTime;

        MediaTime newEnd = newStart + element.Length;
        int newIndex = element.ZIndex + deltaZIndex;
        (Element? before, Element? after, Element? _) =
            element.GetBeforeAndAfterAndCover(newIndex, newStart, elements);

        if (before != null && before.Range.End >= newStart)
        {
            if ((after != null && (after.Start - before.Range.End) >= element.Length) || after == null)
            {
                return before.Range.End - element.Start;
            }
        }
        else if (after != null && after.Start < newEnd)
        {
            MediaTime ns = after.Start - element.Length;
            if (((before != null && (after.Start - before.Range.End) >= element.Length) || before == null) &&
                ns >= MediaTime.Zero)
            {
                return ns - element.Start;
            }
        }
        else if (newStart < MediaTime.Zero)
        {
            return -element.Start;
        }

        return null;
    }
}
