using System.ComponentModel.DataAnnotations;
using System.Numerics;

namespace Beutl.Graphics.Rendering;

/// <summary>
/// 方向光源（太陽光など）
/// </summary>
[Display(Name = "DirectionalLight3D")]
public class DirectionalLight3D : Light3D
{
    public static readonly CoreProperty<Vector3> DirectionProperty;

    private Vector3 _direction = new Vector3(0, -1, 0);

    static DirectionalLight3D()
    {
        DirectionProperty = ConfigureProperty<Vector3, DirectionalLight3D>(nameof(Direction))
            .Accessor(o => o.Direction, (o, v) => o.Direction = v)
            .DefaultValue(new Vector3(0, -1, 0))
            .Register();

        AffectsRender<DirectionalLight3D>(DirectionProperty);
    }

    /// <summary>
    /// ライトの方向
    /// </summary>
    [Display(Name = "Direction", GroupName = "Lighting")]
    public Vector3 Direction
    {
        get => _direction;
        set => SetAndRaise(DirectionProperty, ref _direction, Vector3.Normalize(value));
    }

    public override LightType Type => LightType.Directional;

    /// <summary>
    /// 方向光源を既存のDirectionalLightに変換
    /// </summary>
    public DirectionalLight ToDirectionalLight()
    {
        return new DirectionalLight
        {
            Direction = _direction,
            Color = Color,
            Intensity = Intensity,
            Enabled = Enabled,
            CastShadows = CastShadows
        };
    }

    protected override I3DMeshResource GetDebugMesh()
    {
        // 方向光源用のアローメッシュを作成
        var mesh = CreateArrowMesh();
        var renderer = Scene3DManager.Current?.Renderer;
        if (renderer == null)
            throw new InvalidOperationException("3D renderer is not available");

        return renderer.CreateMesh(mesh);
    }

    private static I3DMesh CreateArrowMesh()
    {
        // 簡単な矢印メッシュを作成
        Vector3[] vertices = [
            // 矢印の軸
            new Vector3(0, 0, 0),
            new Vector3(0, 0, -1),
            // 矢印の頭部
            new Vector3(-0.1f, 0, -0.8f),
            new Vector3(0.1f, 0, -0.8f),
            new Vector3(0, -0.1f, -0.8f),
            new Vector3(0, 0.1f, -0.8f)
        ];

        Vector3[] normals = new Vector3[vertices.Length];
        Array.Fill(normals, Vector3.UnitY);

        Vector2[] texCoords = new Vector2[vertices.Length];
        Array.Fill(texCoords, Vector2.Zero);

        uint[] indices = [
            0, 1, // 軸
            1, 2, 1, 3, 1, 4, 1, 5 // 頭部の線
        ];

        return new BasicMesh(vertices, normals, texCoords, indices);
    }
}
