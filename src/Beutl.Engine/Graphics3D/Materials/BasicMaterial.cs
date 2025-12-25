using System.ComponentModel.DataAnnotations;
using Beutl.Engine;
using Beutl.Language;
using Beutl.Media;

namespace Beutl.Graphics3D.Materials;

/// <summary>
/// A basic material with diffuse color.
/// </summary>
public partial class BasicMaterial : Material3D
{
    public BasicMaterial()
    {
        ScanProperties<BasicMaterial>();
    }

    /// <summary>
    /// Gets the diffuse color of the material.
    /// </summary>
    [Display(Name = nameof(Strings.Color), ResourceType = typeof(Strings))]
    public IProperty<Color> DiffuseColor { get; } = Property.CreateAnimatable(Colors.White);

    /// <summary>
    /// Gets the ambient color contribution.
    /// </summary>
    public IProperty<Color> AmbientColor { get; } = Property.CreateAnimatable(Colors.White);

    /// <summary>
    /// Gets the specular color for highlights.
    /// </summary>
    public IProperty<Color> SpecularColor { get; } = Property.CreateAnimatable(Colors.White);

    /// <summary>
    /// Gets the shininess factor for specular highlights.
    /// </summary>
    [Range(1f, 256f)]
    public IProperty<float> Shininess { get; } = Property.CreateAnimatable(32f);
}
