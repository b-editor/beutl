using Beutl.Collections;

namespace Beutl.Animation;

public class KeyFrames : CoreList<IKeyFrame>
{
    public void Add(IKeyFrame item, out int index)
    {
        if (Count >= 1)
        {
            IKeyFrame first = this[0];
            if (item.KeyTime <= first.KeyTime)
            {
                index = 0;
                Insert(index, item);
                return;
            }
        }

        for (int i = 1; i < Count; i++)
        {
            IKeyFrame prev = this[i - 1];
            IKeyFrame next = this[i];
            if (prev.KeyTime < item.KeyTime
                && item.KeyTime <= next.KeyTime)
            {
                index = i;
                Insert(index, item);
                return;
            }
        }

        if (Count >= 1)
        {
            IKeyFrame last = this[^1];
            if (last.KeyTime <= item.KeyTime)
            {
                index = Count;
                Insert(index, item);
                return;
            }
        }

        Add(item);
        index = Count - 1;
    }

    public (IKeyFrame? Previous, IKeyFrame? Next) GetPreviousAndNextKeyFrame(IKeyFrame keyframe)
    {
        int index = IndexOf(keyframe);
        IKeyFrame? prev = null;
        IKeyFrame? next = null;

        if (index >= 0)
        {
            int prevIndex = index - 1;
            int nextIndex = index + 1;
            if (0 <= prevIndex && prevIndex < Count)
            {
                prev = this[prevIndex];
            }
            if (0 <= nextIndex && nextIndex < Count)
            {
                next = this[nextIndex];
            }
        }

        return (prev, next);
    }

    public (IKeyFrame? Previous, IKeyFrame? Next) GetPreviousAndNextKeyFrame(TimeSpan timeSpan)
    {
        if (Count >= 1)
        {
            IKeyFrame first = this[0];
            if (timeSpan <= first.KeyTime)
            {
                return (null, first);
            }
        }

        for (int i = 1; i < Count; i++)
        {
            IKeyFrame prev = this[i - 1];
            IKeyFrame next = this[i];
            if (prev.KeyTime < timeSpan
                && timeSpan <= next.KeyTime)
            {
                return (prev, next);
            }
        }

        if (Count >= 1)
        {
            IKeyFrame last = this[^1];
            if (last.KeyTime <= timeSpan)
            {
                return (last, null);
            }
        }

        return (null, null);
    }

    public int IndexAt(TimeSpan timeSpan)
    {
        for (int i = 0; i < Count; i++)
        {
            IKeyFrame next = this[i];
            if (timeSpan <= next.KeyTime)
            {
                return i;
            }
        }

        return Count - 1;
    }

    public int IndexAtOrCount(TimeSpan timeSpan)
    {
        for (int i = 0; i < Count; i++)
        {
            IKeyFrame next = this[i];
            if (timeSpan <= next.KeyTime)
            {
                return i;
            }
        }

        return Count;
    }
}
