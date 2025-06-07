using System.ComponentModel.DataAnnotations;
using System.Numerics;
using Beutl.Language;

namespace Beutl.Graphics.Rendering;

/// <summary>
/// プリミティブボックス
/// </summary>
[Display(Name = "Box3D")]
public class Box3D : Drawable3D
{
    public static readonly CoreProperty<Vector3> SizeProperty;

    private Vector3 _size = Vector3.One;
    private I3DMeshResource? _meshResource;

    static Box3D()
    {
        SizeProperty = ConfigureProperty<Vector3, Box3D>(nameof(Size))
            .Accessor(o => o.Size, (o, v) => o.Size = v)
            .DefaultValue(Vector3.One)
            .Register();

        AffectsRender<Box3D>(SizeProperty);
    }

    /// <summary>
    /// ボックスのサイズ
    /// </summary>
    [Display(Name = nameof(Strings.Size), ResourceType = typeof(Strings),
        GroupName = nameof(Strings.Geometry))]
    public Vector3 Size
    {
        get => _size;
        set
        {
            if (SetAndRaise(SizeProperty, ref _size, Vector3.Max(value, Vector3.Zero)))
            {
                // サイズが変更されたらメッシュを再作成
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
        Vector3 halfSize = _size * 0.5f;
        return new BoundingBox(-halfSize, halfSize);
    }

    private I3DMeshResource CreateMeshResource()
    {
        var mesh = BasicMesh.CreateCube(_size.X);
        // 必要に応じてY, Zスケールを適用
        if (_size.Y != _size.X || _size.Z != _size.X)
        {
            mesh = ScaleMesh(mesh, _size);
        }

        var renderer = Scene3DManager.Current?.Renderer;
        if (renderer == null)
            throw new InvalidOperationException("3D renderer is not available");

        return renderer.CreateMesh(mesh);
    }

    private static I3DMesh ScaleMesh(I3DMesh mesh, Vector3 scale)
    {
        var vertices = mesh.Vertices.ToArray();
        for (int i = 0; i < vertices.Length; i++)
        {
            vertices[i] = Vector3.Multiply(vertices[i], scale);
        }

        return new BasicMesh(vertices, mesh.Normals.ToArray(), mesh.TexCoords.ToArray(), mesh.Indices.ToArray());
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
