using System.Numerics;
using System.Runtime.InteropServices;

namespace Beutl.Graphics3D.Lighting;

/// <summary>
/// Types of shadow mapping techniques.
/// </summary>
public enum ShadowType : int
{
    /// <summary>
    /// No shadow.
    /// </summary>
    None = 0,

    /// <summary>
    /// 2D shadow map (for directional and spot lights).
    /// </summary>
    Map2D = 1,

    /// <summary>
    /// Cube shadow map (for point lights).
    /// </summary>
    Cube = 2
}

/// <summary>
/// Shadow information for a single light source.
/// This structure is used to pass shadow data to the GPU.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public struct ShadowInfo
{
    /// <summary>
    /// Light view-projection matrix for 2D shadow mapping.
    /// </summary>
    public Matrix4x4 LightViewProjection;  // 64 bytes, offset 0

    /// <summary>
    /// Light position for point light shadows.
    /// </summary>
    public Vector3 LightPosition;          // 12 bytes, offset 64

    /// <summary>
    /// Far plane distance for point light shadows.
    /// </summary>
    public float FarPlane;                 // 4 bytes, offset 76

    /// <summary>
    /// Depth bias to prevent shadow acne.
    /// </summary>
    public float Bias;                     // 4 bytes, offset 80

    /// <summary>
    /// Normal bias to prevent shadow acne on surfaces facing away from light.
    /// </summary>
    public float NormalBias;               // 4 bytes, offset 84

    /// <summary>
    /// Index into the shadow map array (-1 = no shadow).
    /// </summary>
    public int ShadowMapIndex;             // 4 bytes, offset 88

    /// <summary>
    /// Type of shadow mapping used.
    /// </summary>
    public int ShadowType;                 // 4 bytes, offset 92

    /// <summary>
    /// Shadow strength (0 = no shadow, 1 = full shadow).
    /// </summary>
    public float ShadowStrength;           // 4 bytes, offset 96

    // Padding to align struct to 128 bytes (GLSL std140 requires struct arrays
    // to have elements aligned to 16 bytes, and vec3 requires 16-byte alignment)
    private float _pad1;                   // 4 bytes, offset 100
    private float _pad2;                   // 4 bytes, offset 104
    private float _pad3;                   // 4 bytes, offset 108
    private float _pad4;                   // 4 bytes, offset 112
    private float _pad5;                   // 4 bytes, offset 116
    private float _pad6;                   // 4 bytes, offset 120
    private float _pad7;                   // 4 bytes, offset 124
    // Total: 128 bytes
}

/// <summary>
/// Shadow uniform buffer data for the GPU.
/// Contains information about all shadow-casting lights.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public struct ShadowUBO
{
    /// <summary>
    /// Number of 2D shadow maps in use.
    /// </summary>
    public int ShadowCount2D;

    /// <summary>
    /// Number of cube shadow maps in use.
    /// </summary>
    public int ShadowCountCube;

    /// <summary>
    /// Padding for alignment.
    /// </summary>
    private int _padding1;
    private int _padding2;

    /// <summary>
    /// Shadow information for each light (up to 8 lights).
    /// </summary>
    public ShadowInfoArray Shadows;
}

/// <summary>
/// Fixed-size array of shadow info structures.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public struct ShadowInfoArray
{
    public ShadowInfo Shadow0;
    public ShadowInfo Shadow1;
    public ShadowInfo Shadow2;
    public ShadowInfo Shadow3;
    public ShadowInfo Shadow4;
    public ShadowInfo Shadow5;
    public ShadowInfo Shadow6;
    public ShadowInfo Shadow7;

    public ShadowInfo this[int index]
    {
        readonly get => index switch
        {
            0 => Shadow0,
            1 => Shadow1,
            2 => Shadow2,
            3 => Shadow3,
            4 => Shadow4,
            5 => Shadow5,
            6 => Shadow6,
            7 => Shadow7,
            _ => throw new ArgumentOutOfRangeException(nameof(index))
        };
        set
        {
            switch (index)
            {
                case 0: Shadow0 = value; break;
                case 1: Shadow1 = value; break;
                case 2: Shadow2 = value; break;
                case 3: Shadow3 = value; break;
                case 4: Shadow4 = value; break;
                case 5: Shadow5 = value; break;
                case 6: Shadow6 = value; break;
                case 7: Shadow7 = value; break;
                default: throw new ArgumentOutOfRangeException(nameof(index));
            }
        }
    }

    public const int MaxShadows = 8;
}
