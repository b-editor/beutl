namespace BeUtl.Graphics;

public readonly record struct PushedState : IDisposable
{
    private static readonly NullCanvas s_nullCanvas = new();

    public PushedState(
        ICanvas canvas,
        int level,
        PushedStateType type)
    {
        Canvas = canvas;
        Level = level;
        Type = type;
    }

    public PushedState()
    {
        Canvas = s_nullCanvas;
        Level = -1;
        Type = PushedStateType.None;
    }

    public ICanvas Canvas { get; init; }

    public int Level { get; init; }

    public PushedStateType Type { get; init; }

    public void Dispose()
    {
        switch (Type)
        {
            case PushedStateType.None:
                break;
            case PushedStateType.Foreground:
                Canvas.PopForeground(Level);
                break;
            case PushedStateType.Filters:
                Canvas.PopFilters(Level);
                break;
            case PushedStateType.StrokeWidth:
                Canvas.PopStrokeWidth(Level);
                break;
            case PushedStateType.BlendMode:
                Canvas.PopBlendMode(Level);
                break;
            case PushedStateType.Transform:
                Canvas.PopTransform(Level);
                break;
            case PushedStateType.Clip:
                Canvas.PopClip(Level);
                break;
            case PushedStateType.OpacityMask:
                Canvas.PopOpacityMask(Level);
                break;
            default:
                break;
        }
    }
}
