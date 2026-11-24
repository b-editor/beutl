using Beutl.Graphics.Rendering;
using Beutl.Media;

namespace Beutl.Graphics.Effects;

// Preview pixels can come from the frame cache. Keep the input coordinates and remapped time of those frames.
internal sealed class EffectEditingFrames
{
    private const int Capacity = 512;
    private readonly Lock _lock = new();
    private readonly Dictionary<MediaTime, (Rect Bounds, MediaTime Time)> _frames = [];
    private readonly Queue<MediaTime> _insertionOrder = [];

    public bool Capture(MediaTime displayedTime, Rect bounds, MediaTime evaluationTime)
    {
        if (!HasArea(bounds)) return false;

        lock (_lock)
        {
            if (!_frames.ContainsKey(displayedTime))
            {
                if (_frames.Count >= Capacity)
                    _frames.Remove(_insertionOrder.Dequeue());
                _insertionOrder.Enqueue(displayedTime);
            }
            _frames[displayedTime] = (bounds, evaluationTime);
        }
        return true;
    }

    public bool TryGet(MediaTime displayedTime, out (Rect Bounds, MediaTime Time) frame)
    {
        lock (_lock)
            return _frames.TryGetValue(displayedTime, out frame);
    }

    public void Clear()
    {
        lock (_lock)
        {
            _frames.Clear();
            _insertionOrder.Clear();
        }
    }

    internal static bool HasArea(Rect bounds) => RenderRectValidation.IsFiniteNonNegative(bounds)
        && bounds.Width > 0 && bounds.Height > 0 && float.IsFinite(bounds.Right) && float.IsFinite(bounds.Bottom);
}
