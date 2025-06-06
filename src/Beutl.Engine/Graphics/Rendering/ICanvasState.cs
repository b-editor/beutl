namespace Beutl.Graphics.Rendering;

/// <summary>
/// Interface for canvas state objects that can be pushed and popped.
/// </summary>
internal interface ICanvasState
{
    /// <summary>
    /// Restores the canvas state when popped from the stack.
    /// </summary>
    void Restore();
}

/// <summary>
/// Basic canvas state for save/restore operations.
/// </summary>
internal sealed record BasicCanvasState(int SaveCount) : ICanvasState
{
    public void Restore()
    {
        // Canvas restore is handled by SkiaSharp internally
        // when RestoreToCount is called
    }
}

/// <summary>
/// Opacity state that tracks the previous opacity value.
/// </summary>
internal sealed record OpacityState(int SaveCount, float PreviousOpacity) : ICanvasState
{
    public void Restore()
    {
        // Canvas restore is handled by SkiaSharp internally
    }
}

/// <summary>
/// Blend mode state that tracks the previous blend mode.
/// </summary>
internal sealed record BlendModeState(int SaveCount, BlendMode PreviousBlendMode) : ICanvasState
{
    public void Restore()
    {
        // Canvas restore is handled by SkiaSharp internally
    }
}