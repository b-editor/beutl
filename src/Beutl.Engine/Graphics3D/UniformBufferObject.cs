using System.Numerics;
using System.Runtime.InteropServices;

namespace Beutl.Graphics3D;

/// <summary>
/// Uniform buffer object for 3D rendering containing transformation matrices and lighting data.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
internal struct UniformBufferObject
{
    /// <summary>
    /// The model transformation matrix.
    /// </summary>
    public Matrix4x4 Model;

    /// <summary>
    /// The view transformation matrix.
    /// </summary>
    public Matrix4x4 View;

    /// <summary>
    /// The projection transformation matrix.
    /// </summary>
    public Matrix4x4 Projection;

    /// <summary>
    /// The direction of the light source.
    /// </summary>
    public Vector3 LightDirection;

    private float _pad1;

    /// <summary>
    /// The color of the light source.
    /// </summary>
    public Vector3 LightColor;

    private float _pad2;

    /// <summary>
    /// The ambient light color.
    /// </summary>
    public Vector3 AmbientColor;

    private float _pad3;

    /// <summary>
    /// The camera/view position in world space.
    /// </summary>
    public Vector3 ViewPosition;

    private float _pad4;

    /// <summary>
    /// The object's diffuse color.
    /// </summary>
    public Vector4 ObjectColor;
}
