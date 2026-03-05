namespace Beutl.Engine;

public interface IOriginalDurationProvider
{
    bool HasOriginalDuration();

    bool TryGetOriginalDuration(out TimeSpan timeSpan);
}
