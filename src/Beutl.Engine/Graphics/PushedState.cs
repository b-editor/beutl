namespace Beutl.Graphics;

public readonly record struct PushedState : IDisposable
{
    public PushedState(ICanvas canvas, int level)
    {
        Canvas = canvas;
        Count = level;
    }

    public PushedState()
    {
        Count = -1;
    }

    public ICanvas? Canvas { get; init; }

    public int Count { get; init; }

    public void Dispose()
    {
        Canvas?.Pop(Count);
    }
}
