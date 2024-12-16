using Beutl.Graphics3D.Meshes;

namespace Beutl.Graphics3D;

public class MeshDrawable3D : Drawable3D
{
    public static readonly CoreProperty<Mesh?> MeshProperty;
    private Mesh? _mesh;

    static MeshDrawable3D()
    {
        MeshProperty = ConfigureProperty<Mesh?, MeshDrawable3D>(nameof(Mesh))
            .Accessor(o => o.Mesh, (o, v) => o.Mesh = v)
            .Register();

        AffectsRender<MeshDrawable3D>(MeshProperty);
    }

    public Mesh? Mesh
    {
        get => _mesh;
        set => SetAndRaise(MeshProperty, ref _mesh, value);
    }

    public override void Render(GraphicsContext3D context)
    {
    }
}
