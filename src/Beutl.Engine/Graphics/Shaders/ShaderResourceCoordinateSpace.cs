namespace Beutl.Graphics.Shaders;

/// <summary>Declares how coordinates passed to a child shader are interpreted.</summary>
/// <remarks>
/// The resource binder uses its <see cref="ShaderExecutionContext"/> to create a shader or local matrix that matches
/// the declared space. The binder must not retain its writer, context, or callback-provided raw resource and must not
/// dispose the raw resource; disposal ownership remains defined by the original owned or borrowed registration.
/// </remarks>
public enum ShaderResourceCoordinateSpace
{
    /// <summary>Interprets coordinates as author-defined value coordinates without an output-space conversion.</summary>
    /// <remarks>This is the only coordinate space accepted by <see cref="ShaderDescriptionKind.CurrentPixel"/>.</remarks>
    Value,

    /// <summary>
    /// Interprets coordinates in local output-device pixels, matching the <c>coord</c> argument of a whole-source
    /// shader.
    /// </summary>
    /// <remarks>
    /// For a coordinate <c>coord</c>, the corresponding logical point is
    /// <c>LogicalOrigin + coord / WorkingScale</c>.
    /// </remarks>
    OutputDevice,
}
