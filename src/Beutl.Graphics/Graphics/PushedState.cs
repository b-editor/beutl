namespace Beutl.Graphics;

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
            case PushedStateType.FillBrush:
                Canvas.PopFillBrush(Level);
                break;
            case PushedStateType.Filter:
                Canvas.PopImageFilter(Level);
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
            case PushedStateType.Canvas:
                Canvas.PopCanvas(Level);
                break;
            case PushedStateType.Pen:
                Canvas.PopPen(Level);
                break;
            default:
                break;
        }
    }
}
