using System.Numerics;
using System.Runtime.InteropServices;

namespace Beutl.Graphics3D.Lighting;

/// <summary>
/// Represents the type of light.
/// </summary>
public enum LightType
{
    /// <summary>
    /// Directional light (like sunlight).
    /// </summary>
    Directional = 0,

    /// <summary>
    /// Point light (omnidirectional).
    /// </summary>
    Point = 1,

    /// <summary>
    /// Spotlight (cone-shaped).
    /// </summary>
    Spot = 2
}

/// <summary>
/// Shader-compatible light data structure.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public struct LightData
{
    /// <summary>
    /// Position (for point/spot lights) or direction (for directional lights).
    /// </summary>
    public Vector3 PositionOrDirection;

    /// <summary>
    /// Light type (0=directional, 1=point, 2=spot).
    /// </summary>
    public int Type;

    /// <summary>
    /// Light color (RGB).
    /// </summary>
    public Vector3 Color;

    /// <summary>
    /// Light intensity.
    /// </summary>
    public float Intensity;

    /// <summary>
    /// Direction for spotlights.
    /// </summary>
    public Vector3 Direction;

    /// <summary>
    /// Maximum range.
    /// </summary>
    public float Range;

    /// <summary>
    /// Constant attenuation factor.
    /// </summary>
    public float ConstantAttenuation;

    /// <summary>
    /// Linear attenuation factor.
    /// </summary>
    public float LinearAttenuation;

    /// <summary>
    /// Quadratic attenuation factor.
    /// </summary>
    public float QuadraticAttenuation;

    /// <summary>
    /// Cosine of inner cone angle (for spotlights).
    /// </summary>
    public float InnerCutoff;

    /// <summary>
    /// Cosine of outer cone angle (for spotlights).
    /// </summary>
    public float OuterCutoff;

    /// <summary>
    /// Index into the shadow info array (-1 = no shadow).
    /// </summary>
    public int ShadowIndex;

    // Padding to align struct to 80 bytes (multiple of 16 for std140 array alignment)
    private int _pad1;
    private int _pad2;

    /// <summary>
    /// Creates light data from a DirectionalLight3D resource.
    /// </summary>
    public static LightData FromDirectional(DirectionalLight3D.Resource light)
    {
        var direction = light.Direction;
        if (direction != Vector3.Zero)
            direction = Vector3.Normalize(direction);

        return new LightData
        {
            PositionOrDirection = direction,
            Type = (int)LightType.Directional,
            Color = new Vector3(light.Color.R / 255f, light.Color.G / 255f, light.Color.B / 255f),
            Intensity = light.Intensity,
            Direction = Vector3.Zero,
            Range = float.MaxValue,
            ConstantAttenuation = 1f,
            LinearAttenuation = 0f,
            QuadraticAttenuation = 0f,
            InnerCutoff = 0f,
            OuterCutoff = 0f,
            ShadowIndex = -1
        };
    }

    /// <summary>
    /// Creates light data from a PointLight3D resource.
    /// </summary>
    public static LightData FromPoint(PointLight3D.Resource light)
    {
        return new LightData
        {
            PositionOrDirection = light.Position,
            Type = (int)LightType.Point,
            Color = new Vector3(light.Color.R / 255f, light.Color.G / 255f, light.Color.B / 255f),
            Intensity = light.Intensity,
            Direction = Vector3.Zero,
            Range = light.Range,
            ConstantAttenuation = light.ConstantAttenuation,
            LinearAttenuation = light.LinearAttenuation,
            QuadraticAttenuation = light.QuadraticAttenuation,
            InnerCutoff = 0f,
            OuterCutoff = 0f,
            ShadowIndex = -1
        };
    }

    /// <summary>
    /// Creates light data from a SpotLight3D resource.
    /// </summary>
    public static LightData FromSpot(SpotLight3D.Resource light)
    {
        var direction = light.Direction;
        if (direction != Vector3.Zero)
            direction = Vector3.Normalize(direction);

        return new LightData
        {
            PositionOrDirection = light.Position,
            Type = (int)LightType.Spot,
            Color = new Vector3(light.Color.R / 255f, light.Color.G / 255f, light.Color.B / 255f),
            Intensity = light.Intensity,
            Direction = direction,
            Range = light.Range,
            ConstantAttenuation = light.ConstantAttenuation,
            LinearAttenuation = light.LinearAttenuation,
            QuadraticAttenuation = light.QuadraticAttenuation,
            InnerCutoff = MathF.Cos(light.InnerConeAngle * MathF.PI / 180f),
            OuterCutoff = MathF.Cos(light.OuterConeAngle * MathF.PI / 180f),
            ShadowIndex = -1
        };
    }

    /// <summary>
    /// Creates light data from any Light3D resource.
    /// </summary>
    public static LightData FromLight(Light3D.Resource light)
    {
        return light switch
        {
            DirectionalLight3D.Resource dir => FromDirectional(dir),
            PointLight3D.Resource point => FromPoint(point),
            SpotLight3D.Resource spot => FromSpot(spot),
            _ => default
        };
    }
}
