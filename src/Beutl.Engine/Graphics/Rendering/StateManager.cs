using Beutl.Media;
using SkiaSharp;

namespace Beutl.Graphics.Rendering;

/// <summary>
/// Simplified state manager for canvas operations.
/// Extracted from ImmediateCanvas to improve separation of concerns.
/// </summary>
internal sealed class StateManager
{
    private readonly Stack<int> _saveCounts = [];
    private readonly SKCanvas _canvas;

    public StateManager(SKCanvas canvas)
    {
        _canvas = canvas;
    }

    public int StateCount => _saveCounts.Count;

    /// <summary>
    /// Pushes a basic canvas state (transform, clip, etc.)
    /// </summary>
    public int Push()
    {
        int count = _canvas.Save();
        _saveCounts.Push(count);
        return count;
    }

    /// <summary>
    /// Pushes a layer state for compositing operations.
    /// </summary>
    public int PushLayer(Rect? bounds = null, SKPaint? paint = null)
    {
        int count = bounds.HasValue
            ? _canvas.SaveLayer(bounds.Value.ToSKRect(), paint)
            : _canvas.SaveLayer(paint);
            
        _saveCounts.Push(count);
        return count;
    }

    /// <summary>
    /// Pops states from the stack.
    /// </summary>
    /// <param name="count">Number of states to pop. -1 pops all states.</param>
    public void Pop(int count = 1)
    {
        for (int i = 0; i < count && _saveCounts.TryPop(out int saveCount); i++)
        {
            _canvas.RestoreToCount(saveCount);
        }
    }

    /// <summary>
    /// Pops all states from the stack.
    /// </summary>
    public void PopAll()
    {
        while (_saveCounts.TryPop(out int saveCount))
        {
            _canvas.RestoreToCount(saveCount);
        }
    }

    /// <summary>
    /// Clears all states from the stack.
    /// </summary>
    public void Clear()
    {
        PopAll();
    }
}