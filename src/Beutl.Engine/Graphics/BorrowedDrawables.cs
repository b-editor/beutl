using Beutl.Engine;

namespace Beutl.Graphics;

// Evaluated drawables borrowed from the scene compositor, which owns and disposes them. Each keeps the version it
// was taken at, so an animated change to a drawable is a change to the list.
internal class BorrowedDrawables
{
    private readonly List<Drawable.Resource> _drawables = [];
    private readonly List<int> _versions = [];

    public IReadOnlyList<Drawable.Resource> Drawables => _drawables;

    // Moves whenever Update reports a change.
    public int Version { get; private set; }

    // Takes the drawables among the inputs, in order, and reports whether any of them, its version or their count
    // differs from what was taken before.
    public bool Update(ReadOnlySpan<EngineObject.Resource> inputs)
    {
        bool changed = false;
        int count = 0;
        foreach (EngineObject.Resource input in inputs)
        {
            if (input is not Drawable.Resource drawable) continue;
            if (count == _drawables.Count)
            {
                _drawables.Add(drawable);
                _versions.Add(drawable.Version);
                changed = true;
            }
            else if (_drawables[count] != drawable || _versions[count] != drawable.Version)
            {
                _drawables[count] = drawable;
                _versions[count] = drawable.Version;
                changed = true;
            }

            count++;
        }

        if (count < _drawables.Count)
        {
            _drawables.RemoveRange(count, _drawables.Count - count);
            _versions.RemoveRange(count, _versions.Count - count);
            changed = true;
        }

        if (changed) Version++;
        return changed;
    }

    // Lets go of the drawables without reporting a change.
    public void Clear()
    {
        _drawables.Clear();
        _versions.Clear();
    }
}
