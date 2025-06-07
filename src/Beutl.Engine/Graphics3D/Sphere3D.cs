using System.ComponentModel.DataAnnotations;
using System.Numerics;
using Beutl.Language;

namespace Beutl.Graphics.Rendering;

/// <summary>
/// プリミティブスフィア
/// </summary>
[Display(Name = "Sphere3D")]
public class Sphere3D : Drawable3D
{
    public static readonly CoreProperty<float> RadiusProperty;
    public static readonly CoreProperty<int> SegmentsProperty;
    public static readonly CoreProperty<int> RingsProperty;

    private float _radius = 1.0f;
    private int _segments = 32;
    private int _rings = 16;
    private I3DMeshResource? _meshResource;

    static Sphere3D()
    {
        RadiusProperty = ConfigureProperty<float, Sphere3D>(nameof(Radius))
            .Accessor(o => o.Radius, (o, v) => o.Radius = v)
            .DefaultValue(1.0f)
            .Register();

        SegmentsProperty = ConfigureProperty<int, Sphere3D>(nameof(Segments))
            .Accessor(o => o.Segments, (o, v) => o.Segments = v)
            .DefaultValue(32)
            .Register();

        RingsProperty = ConfigureProperty<int, Sphere3D>(nameof(Rings))
            .Accessor(o => o.Rings, (o, v) => o.Rings = v)
            .DefaultValue(16)
            .Register();

        AffectsRender<Sphere3D>(RadiusProperty, SegmentsProperty, RingsProperty);
    }

    /// <summary>
    /// 球の半径
    /// </summary>
    [Display(Name = nameof(Strings.Radius), ResourceType = typeof(Strings),
        GroupName = nameof(Strings.Geometry))]
    [Range(0.001f, float.MaxValue)]
    public float Radius
    {
        get => _radius;
        set
        {
            if (SetAndRaise(RadiusProperty, ref _radius, Math.Max(0.001f, value)))
            {
                _meshResource?.Dispose();
                _meshResource = null;
            }
        }
    }

    /// <summary>
    /// セグメント数（経度方向の分割数）
    /// </summary>
    [Display(Name = nameof(Strings.Segments), ResourceType = typeof(Strings),
        GroupName = nameof(Strings.Geometry))]
    [Range(3, 128)]
    public int Segments
    {
        get => _segments;
        set
        {
            if (SetAndRaise(SegmentsProperty, ref _segments, Math.Max(3, Math.Min(128, value))))
            {
                _meshResource?.Dispose();
                _meshResource = null;
            }
        }
    }

    /// <summary>
    /// リング数（緯度方向の分割数）
    /// </summary>
    [Display(Name = nameof(Strings.Rings), ResourceType = typeof(Strings),
        GroupName = nameof(Strings.Geometry))]
    [Range(2, 64)]
    public int Rings
    {
        get => _rings;
        set
        {
            if (SetAndRaise(RingsProperty, ref _rings, Math.Max(2, Math.Min(64, value))))
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
        Vector3 extents = new Vector3(_radius);
        return new BoundingBox(-extents, extents);
    }

    private I3DMeshResource CreateMeshResource()
    {
        var mesh = BasicMesh.CreateSphere(_radius, _segments, _rings);

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
