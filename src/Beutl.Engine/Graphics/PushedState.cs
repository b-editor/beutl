namespace Beutl.Graphics;

public interface IPopable
{
    void Pop(int count);
}

public readonly record struct PushedState : IDisposable
{
    public PushedState(IPopable popable, int level)
    {
        Popable = popable;
        Count = level;
    }

    public PushedState()
    {
        Count = -1;
    }

    public IPopable? Popable { get; init; }

    public int Count { get; init; }

    public void Dispose()
    {
        Popable?.Pop(Count);
    }
}
