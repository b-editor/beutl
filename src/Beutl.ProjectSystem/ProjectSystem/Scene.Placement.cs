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

    private Element? GetBefore(Element element)
    {
        Element? tmp = null;
        foreach (Element? item in Children.GetMarshal().Value)
        {
            if (item != element && item.ZIndex == element.ZIndex && item.Start < element.Range.End)
            {
                if (tmp == null || tmp.Start <= item.Start)
                {
                    tmp = item;
                }
            }
        }

        return tmp;
    }

    private Element? GetAfter(Element element)
    {
        Element? tmp = null;
        foreach (Element? item in Children.GetMarshal().Value)
        {
            if (item != element && item.ZIndex == element.ZIndex && item.Range.End > element.Range.End)
            {
                if (tmp == null || tmp.Range.End >= item.Range.End)
                {
                    tmp = item;
                }
            }
        }

        return tmp;
    }

    internal (Element? Before, Element? After, Element? Cover) GetBeforeAndAfterAndCover(Element element)
    {
        Element? beforeTmp = null;
        Element? afterTmp = null;
        Element? coverTmp = null;
        TimeRange range = element.Range;

        foreach (Element? item in Children.GetMarshal().Value)
        {
            if (item != element && item.ZIndex == element.ZIndex)
            {
                if (item.Start < range.Start
                    && (beforeTmp == null || beforeTmp.Start <= item.Start))
                {
                    beforeTmp = item;
                }

                if (item.Range.End > range.End
                    && (afterTmp == null || afterTmp.Range.End >= item.Range.End))
                {
                    afterTmp = item;
                }

                if (range.Contains(item.Range) || range == item.Range)
                {
                    coverTmp = item;
                }
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

    private (TimeRange Range, int ZIndex) GetCorrectPosition(Element element, ElementOverlapHandling handling)
    {
        bool overlapping = IsOverlapping(element.Range, element.ZIndex);

        if (!overlapping || handling.HasFlag(ElementOverlapHandling.Allow))
            return (element.Range, element.ZIndex);

        if (handling == ElementOverlapHandling.ThrowException)
            throw new InvalidOperationException("要素の位置が無効です");

        (Element? before, Element? after, Element? cover) = GetBeforeAndAfterAndCover(element);
        var candidateStart = new List<TimeSpan>(2);
        var candidateEnd = new List<TimeSpan>(2);
        if (cover != null)
        {
            candidateEnd.Add(cover.Start);
            candidateStart.Add(cover.Range.End);
        }

        if (after != null) candidateEnd.Add(after.Start);
        if (before != null) candidateStart.Add(before.Range.End);

        TimeSpan start = element.Start;
        TimeSpan end = element.Range.End;

        if (handling.HasFlag(ElementOverlapHandling.Start) && handling.HasFlag(ElementOverlapHandling.Length))
        {
            foreach (TimeSpan cEnd in candidateEnd)
            {
                TimeRange range = TimeRange.FromRange(start, cEnd);
                if (range.Duration > TimeSpan.Zero && !IsOverlapping(range, element.ZIndex))
                {
                    return (range, element.ZIndex);
                }

                foreach (TimeSpan cStart in candidateStart)
                {
                    range = TimeRange.FromRange(cStart, cEnd);
                    if (range.Duration > TimeSpan.Zero && !IsOverlapping(range, element.ZIndex))
                    {
                        return (range, element.ZIndex);
                    }
                }
            }
        }

        if (handling.HasFlag(ElementOverlapHandling.Length))
        {
            foreach (TimeSpan item in candidateEnd)
            {
                TimeRange range = TimeRange.FromRange(start, item);
                if (range.Duration > TimeSpan.Zero && !IsOverlapping(range, element.ZIndex))
                {
                    return (range, element.ZIndex);
                }
            }
        }

        if (handling.HasFlag(ElementOverlapHandling.Start))
        {
            foreach (TimeSpan item in candidateStart)
            {
                TimeRange range = TimeRange.FromRange(item, end);
                if (range.Duration > TimeSpan.Zero && !IsOverlapping(range, element.ZIndex))
                {
                    return (range, element.ZIndex);
                }
            }
        }

        return (element.Range, NearestLayerNumber(element));
    }

    private sealed class AddCommand(Scene scene, Element element, ElementOverlapHandling overlapHandling)
    {
        private readonly bool _adjustSceneDuration = GlobalConfiguration.Instance.EditorConfig.AutoAdjustSceneDuration;
        private int _zIndex;
        private TimeRange _range;

        public void Do()
        {
            (_range, _zIndex) = scene.GetCorrectPosition(element, overlapHandling);
            element.Start = _range.Start;
            element.Length = _range.Duration;
            element.ZIndex = _zIndex;
            scene.Children.Add(element);

            if (_adjustSceneDuration && scene.Duration + scene.Start < _range.End)
            {
                scene.Duration = _range.End - scene.Start;
            }
        }
    }

    private sealed class RemoveCommand(Scene scene, Element element)
    {
        public void Do()
        {
            scene.Children.Remove(element);
            element.ZIndex = -1;
        }
    }

    private sealed class DeleteCommand
    {
        private readonly Scene _scene;
        private Element? _element;

        public DeleteCommand(Scene scene, Element element)
        {
            _scene = scene;
            _element = element;
        }

        public void Do()
        {
            if (_element != null)
            {
                string fileName = _element.Uri!.LocalPath;
                if (_element.SuppressedStorageSource is null && File.Exists(fileName))
                {
                    File.Delete(fileName);
                }

                _scene.Children.Remove(_element);
                _element = null;
            }
        }
    }

    private sealed class MoveCommand(
        int zIndex,
        Element element,
        TimeSpan newStart,
        TimeSpan oldStart,
        TimeSpan newLength,
        TimeSpan oldLength,
        Scene scene)
    {
        private readonly int _oldZIndex = element.ZIndex;
        private readonly TimeSpan _oldSceneDuration = scene.Duration;
        private readonly bool _adjustSceneDuration = GlobalConfiguration.Instance.EditorConfig.AutoAdjustSceneDuration;

        public void Do()
        {
            TimeSpan newEnd = newStart + newLength;
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
                else
                {
                    Undo();
                }
            }
            else if (after != null && after.Start < newEnd)
            {
                TimeSpan ns = after.Start - newLength;
                if (((before != null && (after.Start - before.Range.End) >= newLength) || before == null) &&
                    ns >= TimeSpan.Zero)
                {
                    element.Start = ns;
                    element.Length = newLength;
                    element.ZIndex = zIndex;
                }
                else
                {
                    Undo();
                }
            }
            else if (cover != null)
            {
                Undo();
            }
            else
            {
                element.Start = newStart;
                element.Length = newLength;
                element.ZIndex = zIndex;
            }

            TimeRange range = element.Range;
            if (_adjustSceneDuration && scene.Duration + scene.Start < range.End)
            {
                scene.Duration = range.End - scene.Start;
            }
        }

        public void Undo()
        {
            element.ZIndex = _oldZIndex;
            element.Start = oldStart;
            element.Length = oldLength;
            if (_adjustSceneDuration)
            {
                scene.Duration = _oldSceneDuration;
            }
        }
    }

    private sealed class MultipleMoveCommand
    {
        private readonly Scene _scene;
        private readonly Element[] _elements;
        private readonly int _deltaZIndex;
        private readonly TimeSpan _deltaTime;
        private readonly bool _conflict;
        private readonly bool _adjustSceneDuration;
        private readonly TimeSpan _oldSceneDuration;
        private readonly TimeSpan _newSceneDuration;

        public MultipleMoveCommand(
            Scene scene,
            Element[] elements,
            int deltaZIndex,
            TimeSpan deltaTime)
        {
            _scene = scene;
            _elements = elements;
            _deltaZIndex = deltaZIndex;
            _deltaTime = deltaTime;

            foreach (Element item in elements)
            {
                _conflict = HasConflict(scene, _deltaZIndex, _deltaTime);
                if (!_conflict)
                {
                    break;
                }
                else
                {
                    TimeSpan? newDeltaStart = DeltaStart(item);
                    if (newDeltaStart.HasValue)
                    {
                        _deltaTime = newDeltaStart.Value;
                    }
                }
            }

            _conflict = HasConflict(scene, _deltaZIndex, _deltaTime);
            _adjustSceneDuration = GlobalConfiguration.Instance.EditorConfig.AutoAdjustSceneDuration;

            if (_adjustSceneDuration)
            {
                _oldSceneDuration = _newSceneDuration = scene.Duration;

                TimeSpan maxEndingTime = elements.Max(i => i.Range.End + _deltaTime);
                if (_oldSceneDuration + scene.Start < maxEndingTime)
                {
                    _newSceneDuration = maxEndingTime - scene.Start;
                }
            }
        }

        private bool HasConflict(Scene scene, int deltaZIndex, TimeSpan deltaTime)
        {
            Element[] others = scene.Children.Except(_elements).ToArray();
            foreach (Element item in _elements)
            {
                TimeRange newRange = item.Range.AddStart(deltaTime);
                int newLayer = item.ZIndex + deltaZIndex;
                if (newLayer < 0 || newRange.Start.Ticks < 0)
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

        private TimeSpan? DeltaStart(Element element)
        {
            TimeSpan newStart = element.Start + _deltaTime;

            TimeSpan newEnd = newStart + element.Length;
            int newIndex = element.ZIndex + _deltaZIndex;
            (Element? before, Element? after, Element? _) =
                element.GetBeforeAndAfterAndCover(newIndex, newStart, _elements);

            if (before != null && before.Range.End >= newStart)
            {
                if ((after != null && (after.Start - before.Range.End) >= element.Length) || after == null)
                {
                    return before.Range.End - element.Start;
                }
            }
            else if (after != null && after.Start < newEnd)
            {
                TimeSpan ns = after.Start - element.Length;
                if (((before != null && (after.Start - before.Range.End) >= element.Length) || before == null) &&
                    ns >= TimeSpan.Zero)
                {
                    return ns - element.Start;
                }
            }
            else if (newStart.Ticks < 0)
            {
                return -element.Start;
            }

            return null;
        }

        public void Do()
        {
            if (!_conflict)
            {
                foreach (Element item in _elements)
                {
                    item.Start += _deltaTime;
                    item.ZIndex += _deltaZIndex;
                }

                if (_adjustSceneDuration)
                {
                    _scene.Duration = _newSceneDuration;
                }
            }
        }
    }
}
