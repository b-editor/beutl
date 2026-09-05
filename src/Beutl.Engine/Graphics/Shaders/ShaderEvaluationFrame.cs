using Beutl.Media;

namespace Beutl.Graphics.Shaders;

/// <summary>
/// Describes the device frame a shader stage's <c>coord</c> argument is expressed in.
/// </summary>
/// <param name="DeviceBounds">The frame's footprint on the composition-device grid.</param>
/// <param name="RasterBounds">The frame's stage-local logical footprint.</param>
internal readonly record struct ShaderEvaluationFrame(
    PixelRect DeviceBounds,
    Rect RasterBounds)
{
    public static ShaderEvaluationFrame Destination(PixelRect deviceBounds, Rect rasterBounds)
        => new(deviceBounds, rasterBounds);
}
