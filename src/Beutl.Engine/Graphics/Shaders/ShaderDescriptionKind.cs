namespace Beutl.Graphics.Shaders;

/// <summary>Identifies the execution model and entry-point contract of a shader description.</summary>
public enum ShaderDescriptionKind
{
    /// <summary>
    /// Transforms one coverage-resolved, premultiplied linear-light pixel through
    /// <c>half4 apply(half4 color)</c>.
    /// </summary>
    /// <remarks>
    /// Current-pixel stages have no output-position coordinate and may fuse only with structurally compatible
    /// adjacent stages after analytic or antialiased coverage has been resolved.
    /// </remarks>
    CurrentPixel,

    /// <summary>
    /// Materializes a complete source through <c>half4 main(float2 coord)</c> and may sample arbitrary upstream
    /// locations.
    /// </summary>
    /// <remarks>
    /// Whole-source stages must declare the implicit <c>src</c> child shader. They may lead a fused run but cannot
    /// consume an earlier stage inside that run.
    /// </remarks>
    WholeSource,
}
