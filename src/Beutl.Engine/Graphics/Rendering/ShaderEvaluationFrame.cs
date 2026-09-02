using Beutl.Media;

namespace Beutl.Graphics.Shaders;

/// <summary>
/// Describes the device frame a shader stage's <c>coord</c> argument is expressed in.
/// </summary>
/// <param name="DeviceBounds">The frame's footprint on the composition-device grid.</param>
/// <param name="RasterBounds">The frame's stage-local logical footprint.</param>
/// <param name="FragmentOrigin">
/// The device offset added to a destination-local coordinate to obtain the stage's <c>coord</c>. It is non-zero
/// only when a WholeSource stage was asked for a strict subset of its complete output.
/// </param>
internal readonly record struct ShaderEvaluationFrame(
    PixelRect DeviceBounds,
    Rect RasterBounds,
    PixelPoint FragmentOrigin)
{
    public static ShaderEvaluationFrame Destination(PixelRect deviceBounds, Rect rasterBounds)
        => new(deviceBounds, rasterBounds, default);
}
