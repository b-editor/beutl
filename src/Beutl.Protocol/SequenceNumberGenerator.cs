namespace Beutl.Protocol;

public class SequenceNumberGenerator
{
    private long _current = 0;

    public long GetNext()
    {
        return Interlocked.Increment(ref _current);
    }
}
