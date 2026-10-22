namespace Beutl.Engine;

public interface IOriginalDurationProvider
{
    bool HasOriginalDuration();

    bool TryGetOriginalDuration(out MediaTime timeSpan);
}
