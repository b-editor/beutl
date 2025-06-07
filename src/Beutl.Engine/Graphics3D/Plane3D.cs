using System.ComponentModel.DataAnnotations;
using System.Numerics;
using Beutl.Language;

namespace Beutl.Graphics.Rendering;

/// <summary>
/// プリミティブプレーン
/// </summary>
[Display(Name = "Plane3D")]
public class Plane3D : Drawable3D
{
    public static readonly CoreProperty<Vector2> SizeProperty;
    public static readonly CoreProperty<int> SubdivisionsProperty;

    private Vector2 _size = new Vector2(1.0f);
    private int _subdivisions = 1;
    private I3DMeshResource? _meshResource;

    static Plane3D()
    {
        SizeProperty = ConfigureProperty<Vector2, Plane3D>(nameof(Size))
            .Accessor(o => o.Size, (o, v) => o.Size = v)
            .DefaultValue(new Vector2(1.0f))
            .Register();

        SubdivisionsProperty = ConfigureProperty<int, Plane3D>(nameof(Subdivisions))
            .Accessor(o => o.Subdivisions, (o, v) => o.Subdivisions = v)
            .DefaultValue(1)
            .Register();

        AffectsRender<Plane3D>(SizeProperty, SubdivisionsProperty);
    }

    /// <summary>
    /// プレーンのサイズ
    /// </summary>
    [Display(Name = nameof(Strings.Size), ResourceType = typeof(Strings),
        GroupName = nameof(Strings.Geometry))]
    public Vector2 Size
    {
        get => _size;
        set
        {
            if (SetAndRaise(SizeProperty, ref _size, Vector2.Max(value, Vector2.Zero)))
            {
                _meshResource?.Dispose();
                _meshResource = null;
            }
        }
    }

    /// <summary>
    /// 分割数
    /// </summary>
    [Display(Name = nameof(Strings.Subdivisions), ResourceType = typeof(Strings),
        GroupName = nameof(Strings.Geometry))]
    [Range(1, 64)]
    public int Subdivisions
    {
        get => _subdivisions;
        set
        {
            if (SetAndRaise(SubdivisionsProperty, ref _subdivisions, Math.Max(1, Math.Min(64, value))))
            {
                _meshResource?.Dispose();
                _meshResource = null;
            }
        }
    }

    public override I3DMeshResource Mesh => _meshResource ??= CreateMeshResource();

    protected override void RenderCore3D(I3DCanvas canvas)
    {
        canvas.DrawMesh(Mesh, Material);
    }

    public override BoundingBox GetBounds3D()
    {
        Vector3 halfSize = new Vector3(_size.X * 0.5f, 0, _size.Y * 0.5f);
        return new BoundingBox(-halfSize, halfSize);
    }

    private I3DMeshResource CreateMeshResource()
    {
        var mesh = BasicMesh.CreatePlane(_size, _subdivisions);

        var renderer = Scene3DManager.Current?.Renderer;
        if (renderer == null)
            throw new InvalidOperationException("3D renderer is not available");

        return renderer.CreateMesh(mesh);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _meshResource?.Dispose();
            _meshResource = null;
        }
        base.Dispose(disposing);
    }
}
