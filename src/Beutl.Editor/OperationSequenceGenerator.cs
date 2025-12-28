namespace Beutl.Editor;

public sealed class OperationSequenceGenerator
{
    private long _current = 0;

    public long GetNext() => Interlocked.Increment(ref _current);
}
