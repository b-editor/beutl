using Beutl.Collections;

namespace Beutl.Animation;

// The lookups by time over a key frame list, which is kept in time order.
public static class KeyFrameListExtensions
{
    // Move the actual items to preserve attachment and keep equal-time frames in their original order.
    internal static void SortByKeyTime(this ICoreList<IKeyFrame> keyFrames)
    {
        IKeyFrame[] sorted = keyFrames.OrderBy(frame => frame.KeyTime.Value).ToArray();
        for (int index = 0; index < sorted.Length; index++)
        {
            if (!ReferenceEquals(keyFrames[index], sorted[index]))
            {
                keyFrames.Move(keyFrames.IndexOf(sorted[index]), index);
            }
        }
    }

    public static void Add(this ICoreList<IKeyFrame> keyFrames, IKeyFrame item, out int index)
    {
        index = keyFrames.IndexAtOrCount(item.KeyTime.Value);
        keyFrames.Insert(index, item);
    }

    public static (IKeyFrame? Previous, IKeyFrame? Next) GetPreviousAndNextKeyFrame(
        this ICoreList<IKeyFrame> keyFrames, IKeyFrame keyframe)
    {
        int index = keyFrames.IndexOf(keyframe);
        IKeyFrame? prev = null;
        IKeyFrame? next = null;

        if (index >= 0)
        {
            int prevIndex = index - 1;
            int nextIndex = index + 1;
            if (0 <= prevIndex && prevIndex < keyFrames.Count)
            {
                prev = keyFrames[prevIndex];
            }
            if (0 <= nextIndex && nextIndex < keyFrames.Count)
            {
                next = keyFrames[nextIndex];
            }
        }

        return (prev, next);
    }

    public static (IKeyFrame? Previous, IKeyFrame? Next) GetPreviousAndNextKeyFrame(
        this ICoreList<IKeyFrame> keyFrames, MediaTime timeSpan)
    {
        int index = keyFrames.IndexAtOrCount(timeSpan);
        IKeyFrame? prev = index > 0 ? keyFrames[index - 1] : null;
        IKeyFrame? next = index < keyFrames.Count ? keyFrames[index] : null;
        return (prev, next);
    }

    public static int IndexAt(this ICoreList<IKeyFrame> keyFrames, MediaTime timeSpan)
    {
        // Past the last keyframe this is the last index, and -1 on an empty list.
        return Math.Min(keyFrames.IndexAtOrCount(timeSpan), keyFrames.Count - 1);
    }

    public static int IndexAtOrCount(this ICoreList<IKeyFrame> keyFrames, MediaTime timeSpan)
    {
        for (int i = 0; i < keyFrames.Count; i++)
        {
            IKeyFrame next = keyFrames[i];
            if (timeSpan <= next.KeyTime.Value)
            {
                return i;
            }
        }

        return keyFrames.Count;
    }
}
