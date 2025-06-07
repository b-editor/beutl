using System.ComponentModel.DataAnnotations;
using Beutl.Animation;
using Beutl.Language;
using Beutl.Media;
using Beutl.Serialization;

namespace Beutl.Graphics.Rendering;

/// <summary>
/// 3Dメッシュオブジェクト
/// </summary>
[Display(Name = "Mesh3D")]
public class Mesh3D : Drawable3D
{
    public static readonly CoreProperty<I3DMesh?> GeometryProperty;
    public static readonly CoreProperty<bool> SmoothNormalsProperty;

    private I3DMesh? _geometry;
    private bool _smoothNormals = true;
    private I3DMeshResource? _meshResource;

    static Mesh3D()
    {
        GeometryProperty = ConfigureProperty<I3DMesh?, Mesh3D>(nameof(Geometry))
            .Accessor(o => o.Geometry, (o, v) => o.Geometry = v)
            .DefaultValue(null)
            .Register();

        SmoothNormalsProperty = ConfigureProperty<bool, Mesh3D>(nameof(SmoothNormals))
            .Accessor(o => o.SmoothNormals, (o, v) => o.SmoothNormals = v)
            .DefaultValue(true)
            .Register();

        AffectsRender<Mesh3D>(GeometryProperty, SmoothNormalsProperty);
        Hierarchy<Mesh3D>(GeometryProperty);
    }

    /// <summary>
    /// メッシュジオメトリ
    /// </summary>
    [Display(Name = nameof(Strings.Geometry), ResourceType = typeof(Strings),
        GroupName = nameof(Strings.Geometry))]
    public I3DMesh? Geometry
    {
        get => _geometry;
        set
        {
            if (SetAndRaise(GeometryProperty, ref _geometry, value))
            {
                // ジオメトリが変更されたらメッシュリソースを再作成
                _meshResource?.Dispose();
                _meshResource = null;
            }
        }
    }

    /// <summary>
    /// 法線をスムージングするかどうか
    /// </summary>
    [Display(Name = nameof(Strings.SmoothNormals), ResourceType = typeof(Strings),
        GroupName = nameof(Strings.Geometry))]
    public bool SmoothNormals
    {
        get => _smoothNormals;
        set => SetAndRaise(SmoothNormalsProperty, ref _smoothNormals, value);
    }

    public override I3DMeshResource Mesh => _meshResource ??= CreateMeshResource();

    protected override void RenderCore3D(I3DCanvas canvas)
    {
        if (_geometry == null)
            return;

        // メッシュを描画
        canvas.DrawMesh(Mesh, Material);
    }

    public override BoundingBox GetBounds3D()
    {
        if (_geometry == null)
            return BoundingBox.Empty;

        return BoundingBox.FromPoints(_geometry.Vertices.ToArray());
    }

    private I3DMeshResource CreateMeshResource()
    {
        if (_geometry == null)
            throw new InvalidOperationException("Geometry is not set");

        var renderer = Scene3DManager.Current?.Renderer;
        if (renderer == null)
            throw new InvalidOperationException("3D renderer is not available");

        return renderer.CreateMesh(_geometry);
    }

    public override void Serialize(ICoreSerializationContext context)
    {
        base.Serialize(context);
        context.SetValue(nameof(Geometry), Geometry);
        context.SetValue(nameof(SmoothNormals), SmoothNormals);
    }

    public override void Deserialize(ICoreSerializationContext context)
    {
        base.Deserialize(context);

        if (context.GetValue<I3DMesh>(nameof(Geometry)) is { } geometry)
        {
            Geometry = geometry;
        }

        if (context.GetValue<bool>(nameof(SmoothNormals)) is { } smoothNormals)
        {
            SmoothNormals = smoothNormals;
        }
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
