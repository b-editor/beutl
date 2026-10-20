using Beutl.Media;

namespace Beutl.Editor.Services;

// For edits the preview has to show before the history records them, such as a value being dragged.
public interface IFrameCacheInvalidator
{
    void Invalidate(TimeRange range);
}
