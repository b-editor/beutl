using Beutl.Media;

namespace Beutl.Graphics.Rendering;

internal interface IBuiltInBackdropCaptureSink
{
    bool TryCommitBackdropCapture(Bitmap bitmap, float density)
    {
        CommitBackdropCapture(bitmap, density);
        return true;
    }

    void CommitBackdropCapture(Bitmap bitmap, float density);
}
