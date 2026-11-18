using Beutl.Graphics.Effects;
using Beutl.Media;
using BtlPoint = Beutl.Graphics.Point;

namespace Beutl.Editor.Components.Helpers;

// What an eyedropper on the preview needs from the player: the point of the frame the user clicks, and the frame at the
// playhead as the effect whose colors are picked receives them, so that neither what the effect does nor what the effects
// after it do to colors can move what it is aimed at.
internal interface IPreviewEyedropper
{
    // Waits for a left click on the preview's frame and gives its point in frame pixels, or null when the user cancels
    // with a right click, another pick starts or the token is cancelled. Until then hovered receives the point under the
    // pointer, or null off the frame. Call it on the UI thread.
    Task<BtlPoint?> PickPointAsync(Action<BtlPoint?> hovered, CancellationToken cancellationToken);

    // Draws the frame at the playhead at the preview's scale for this frame only with effect turned off and the effects
    // after it changing no colors, while those that move pixels still put them where the preview shows them. Gives it as
    // the renderer makes it (premultiplied RgbaF16 in linear sRGB) with that scale, or null with no scene to draw. Other
    // layers still draw. The caller owns the bitmap. Playback pauses first.
    Task<(Bitmap Frame, float Scale)?> DrawFrameReceivedBy(FilterEffect effect, CancellationToken cancellationToken);
}
