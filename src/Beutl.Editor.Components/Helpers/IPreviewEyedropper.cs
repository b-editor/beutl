using Beutl.Engine;
using Beutl.Media;
using BtlPoint = Beutl.Graphics.Point;

namespace Beutl.Editor.Components.Helpers;

// What an eyedropper on the preview needs from the player: the point of the frame the user clicks, and the frame at the
// playhead drawn without the object whose colors are picked, so that what the object does cannot move what it is aimed at.
internal interface IPreviewEyedropper
{
    // Waits for a left click on the preview's frame and gives its point in frame pixels, or null when the user cancels
    // with a right click, another pick starts or the token is cancelled. Until then hovered receives the point under the
    // pointer, or null off the frame. Call it on the UI thread.
    Task<BtlPoint?> PickPointAsync(Action<BtlPoint?> hovered, CancellationToken cancellationToken);

    // Draws the frame at the playhead at the preview's scale, with excluded turned off for this frame only, and gives it
    // as the renderer makes it (premultiplied RgbaF16 in linear sRGB) with that scale, or null with no scene to draw.
    // The caller owns the bitmap. Playback pauses first.
    Task<(Bitmap Frame, float Scale)?> DrawFrameWithout(EngineObject excluded, CancellationToken cancellationToken);
}
