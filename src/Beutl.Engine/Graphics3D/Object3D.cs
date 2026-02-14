using System.ComponentModel.DataAnnotations;
using System.Numerics;
using Beutl.Engine;
using Beutl.Graphics3D.Materials;
using Beutl.Graphics3D.Meshes;
using Beutl.Language;
using Beutl.Utilities;

namespace Beutl.Graphics3D;

/// <summary>
/// Base class for all 3D objects in a scene.
/// </summary>
public abstract partial class Object3D : EngineObject
{
    public Object3D()
    {
        ScanProperties<Object3D>();
        Material.CurrentValue = new PBRMaterial();
    }

    /// <summary>
    /// Gets the position of the object in world space.
    /// </summary>
    [Display(Name = nameof(Strings.Position), ResourceType = typeof(Strings))]
    public IProperty<Vector3> Position { get; } = Property.CreateAnimatable(Vector3.Zero);

    /// <summary>
    /// Gets the rotation of the object as Euler angles in radians (X=Pitch, Y=Yaw, Z=Roll).
    /// </summary>
    [Display(Name = nameof(Strings.Rotation), ResourceType = typeof(Strings))]
    public IProperty<Vector3> Rotation { get; } = Property.CreateAnimatable(Vector3.Zero);

    /// <summary>
    /// Gets the scale of the object.
    /// </summary>
    [Display(Name = nameof(Strings.Scale), ResourceType = typeof(Strings))]
    public IProperty<Vector3> Scale { get; } = Property.CreateAnimatable(Vector3.One);

    /// <summary>
    /// Gets the material applied to this object.
    /// </summary>
    [Display(Name = nameof(Strings.Material), ResourceType = typeof(Strings))]
    public IProperty<Material3D?> Material { get; } = Property.Create<Material3D?>();

    /// <summary>
    /// Gets whether this object casts shadows.
    /// </summary>
    [Display(Name = nameof(Strings.CastShadows), ResourceType = typeof(Strings))]
    public IProperty<bool> CastShadows { get; } = Property.CreateAnimatable(true);

    /// <summary>
    /// Gets whether this object receives shadows.
    /// </summary>
    [Display(Name = nameof(Strings.ReceiveShadows), ResourceType = typeof(Strings))]
    public IProperty<bool> ReceiveShadows { get; } = Property.CreateAnimatable(true);

    public abstract partial class Resource
    {
        /// <summary>
        /// Gets the world transformation matrix for this object.
        /// </summary>
        public Matrix4x4 GetWorldMatrix()
        {
            var scale = Matrix4x4.CreateScale(Scale);
            var rotation = Matrix4x4.CreateFromYawPitchRoll(MathUtilities.Deg2Rad(Rotation.Y), MathUtilities.Deg2Rad(Rotation.X), MathUtilities.Deg2Rad(Rotation.Z));
            var translation = Matrix4x4.CreateTranslation(Position);
            return scale * rotation * translation;
        }

        /// <summary>
        /// Gets the mesh resource for this object.
        /// </summary>
        /// <returns>The mesh resource, or null if not available.</returns>
        public abstract Mesh.Resource? GetMesh();

        /// <summary>
        /// Gets the child resources of this object.
        /// </summary>
        /// <returns>The child resources, or an empty list if this object has no children.</returns>
        public virtual IReadOnlyList<Resource> GetChildResources() => [];
    }
}
