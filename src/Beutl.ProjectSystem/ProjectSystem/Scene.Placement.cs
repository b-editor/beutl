using Beutl.Configuration;
using Beutl.Media;

namespace Beutl.ProjectSystem;

public partial class Scene
{
    private int NearestLayerNumber(Element element)
    {
        if (IsOverlapping(element.Range, element.ZIndex.Value))
        {
            int layerMax = Children.Max(i => i.ZIndex.Value);

            // 使うことができるレイヤー番号。ロックされたレイヤーには自動配置しない。
            var numbers = new List<int>();

            for (int l = 0; l <= layerMax; l++)
            {
                if (!IsLayerLocked(l)
                    && !Children.Any(i => i.ZIndex.Value == l && i.Range.Intersects(element.Range)))
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

            return numbers.Nearest(element.ZIndex.Value);
        }

        return element.ZIndex.Value;
    }

    internal (Element? Before, Element? After, Element? Cover) GetBeforeAndAfterAndCover(Element element)
    {
        Element? beforeTmp = null;
        Element? afterTmp = null;
        Element? coverTmp = null;
        TimeRange range = element.Range;

        foreach (Element? item in Children.AsSpan())
        {
            if (item != element && item.ZIndex.Value == element.ZIndex.Value)
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
            if (i.ZIndex.Value == zindex)
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
        bool overlapping = IsOverlapping(element.Range, element.ZIndex.Value);

        if (!overlapping || handling.HasFlag(ElementOverlapHandling.Allow))
            return (element.Range, element.ZIndex.Value);

        if (handling == ElementOverlapHandling.ThrowException)
            throw new InvalidOperationException("要素の位置が無効です");

        (Element? before, Element? after, Element? cover) = GetBeforeAndAfterAndCover(element);
        var candidateStart = new List<MediaTime>(2);
        var candidateEnd = new List<MediaTime>(2);
        if (cover != null)
        {
            candidateEnd.Add(cover.Start.Value);
            candidateStart.Add(cover.Range.End);
        }

        if (after != null) candidateEnd.Add(after.Start.Value);
        if (before != null) candidateStart.Add(before.Range.End);

        MediaTime start = element.Start.Value;
        MediaTime end = element.Range.End;

        if (handling.HasFlag(ElementOverlapHandling.Start) && handling.HasFlag(ElementOverlapHandling.Length))
        {
            foreach (MediaTime cEnd in candidateEnd)
            {
                TimeRange range = TimeRange.FromRange(start, cEnd);
                if (IsFreeRange(range, element.ZIndex.Value))
                {
                    return (range, element.ZIndex.Value);
                }

                foreach (MediaTime cStart in candidateStart)
                {
                    range = TimeRange.FromRange(cStart, cEnd);
                    if (IsFreeRange(range, element.ZIndex.Value))
                    {
                        return (range, element.ZIndex.Value);
                    }
                }
            }
        }

        if (handling.HasFlag(ElementOverlapHandling.Length))
        {
            foreach (MediaTime item in candidateEnd)
            {
                TimeRange range = TimeRange.FromRange(start, item);
                if (IsFreeRange(range, element.ZIndex.Value))
                {
                    return (range, element.ZIndex.Value);
                }
            }
        }

        if (handling.HasFlag(ElementOverlapHandling.Start))
        {
            foreach (MediaTime item in candidateStart)
            {
                TimeRange range = TimeRange.FromRange(item, end);
                if (IsFreeRange(range, element.ZIndex.Value))
                {
                    return (range, element.ZIndex.Value);
                }
            }
        }

        return (element.Range, NearestLayerNumber(element));
    }

    private void AddChildCore(Element element, ElementOverlapHandling overlapHandling)
    {
        bool adjustSceneDuration = GlobalConfiguration.Instance.EditorConfig.AutoAdjustSceneDuration.Value;
        (TimeRange range, int zIndex) = GetCorrectPosition(element, overlapHandling);
        element.Start.Value = range.Start;
        element.Length.Value = range.Duration;
        element.ZIndex.Value = zIndex;
        Children.Add(element);

        if (adjustSceneDuration && Duration.Value + Start.Value < range.End)
        {
            Duration.Value = range.End - Start.Value;
        }
    }

    private void MoveChildCore(int zIndex, MediaTime newStart, MediaTime newLength, Element element)
    {
        bool adjustSceneDuration = GlobalConfiguration.Instance.EditorConfig.AutoAdjustSceneDuration.Value;
        MediaTime newEnd = newStart + newLength;
        (Element? before, Element? after, Element? cover) =
            element.GetBeforeAndAfterAndCover(zIndex, newStart, newEnd);

        if (before != null && before.Range.End >= newStart)
        {
            if ((after != null && (after.Start.Value - before.Range.End) >= newLength) || after == null)
            {
                element.Start.Value = before.Range.End;
                element.Length.Value = newLength;
                element.ZIndex.Value = zIndex;
            }
        }
        else if (after != null && after.Start.Value < newEnd)
        {
            MediaTime ns = after.Start.Value - newLength;
            if (((before != null && (after.Start.Value - before.Range.End) >= newLength) || before == null) &&
                ns >= MediaTime.Zero)
            {
                element.Start.Value = ns;
                element.Length.Value = newLength;
                element.ZIndex.Value = zIndex;
            }
        }
        else if (cover == null)
        {
            element.Start.Value = newStart;
            element.Length.Value = newLength;
            element.ZIndex.Value = zIndex;
        }

        TimeRange range = element.Range;
        if (adjustSceneDuration && Duration.Value + Start.Value < range.End)
        {
            Duration.Value = range.End - Start.Value;
        }
    }

    private void MoveChildrenCore(Element[] elements, int deltaZIndex, MediaTime deltaTime)
    {
        var selected = elements.ToHashSet();
        var obstacles = new SceneElementIndex(Children.Where(element => !selected.Contains(element)));
        bool conflict = HasConflict(elements, obstacles, deltaZIndex, deltaTime);
        foreach (Element item in elements)
        {
            if (!conflict)
            {
                break;
            }

            MediaTime? newDeltaStart = DeltaStart(item, obstacles, deltaZIndex, deltaTime);
            if (newDeltaStart.HasValue && newDeltaStart.Value != deltaTime)
            {
                deltaTime = newDeltaStart.Value;
                conflict = HasConflict(elements, obstacles, deltaZIndex, deltaTime);
            }
        }

        bool adjustSceneDuration = GlobalConfiguration.Instance.EditorConfig.AutoAdjustSceneDuration.Value;
        MediaTime newSceneDuration = Duration.Value;
        if (adjustSceneDuration)
        {
            MediaTime maxEndingTime = elements.Max(i => i.Range.End + deltaTime);
            if (newSceneDuration + Start.Value < maxEndingTime)
            {
                newSceneDuration = maxEndingTime - Start.Value;
            }
        }

        if (!conflict)
        {
            foreach (Element item in elements)
            {
                item.Start.Value += deltaTime;
                item.ZIndex.Value += deltaZIndex;
            }

            if (adjustSceneDuration)
            {
                Duration.Value = newSceneDuration;
            }
        }
    }

    private static bool HasConflict(Element[] elements, SceneElementIndex obstacles, int deltaZIndex, MediaTime deltaTime)
    {
        foreach (Element item in elements)
        {
            TimeRange newRange = item.Range.AddStart(deltaTime);
            int newLayer = item.ZIndex.Value + deltaZIndex;
            if (newLayer < 0 || newRange.Start < MediaTime.Zero)
                return true;

            if (obstacles.Intersects(newLayer, newRange)) return true;
        }

        return false;
    }

    private static MediaTime? DeltaStart(Element element, SceneElementIndex obstacles, int deltaZIndex, MediaTime deltaTime)
    {
        MediaTime newStart = element.Start.Value + deltaTime;

        MediaTime newEnd = newStart + element.Length.Value;
        int newIndex = element.ZIndex.Value + deltaZIndex;
        (Element? before, Element? after) = obstacles.Neighbors(newIndex, new TimeRange(newStart, element.Length.Value));

        if (before != null && before.Range.End >= newStart)
        {
            if ((after != null && (after.Start.Value - before.Range.End) >= element.Length.Value) || after == null)
            {
                return before.Range.End - element.Start.Value;
            }
        }
        else if (after != null && after.Start.Value < newEnd)
        {
            MediaTime ns = after.Start.Value - element.Length.Value;
            if (((before != null && (after.Start.Value - before.Range.End) >= element.Length.Value) || before == null) &&
                ns >= MediaTime.Zero)
            {
                return ns - element.Start.Value;
            }
        }
        else if (newStart < MediaTime.Zero)
        {
            return -element.Start.Value;
        }

        return null;
    }
}
