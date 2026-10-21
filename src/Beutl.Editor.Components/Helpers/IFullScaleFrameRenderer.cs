using Beutl.Media;

namespace Beutl.Editor.Components.Helpers;

// Renders the frame at the playhead at the scene's full size, whatever the preview quality.
internal interface IFullScaleFrameRenderer
{
    Task<Bitmap> DrawFrameAtFullScale();
}
