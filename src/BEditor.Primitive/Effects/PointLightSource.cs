using System.Collections.Generic;

using BEditor.Data;
using BEditor.Data.Property;
using BEditor.Data.Property.PrimitiveGroup;
using BEditor.Primitive.Resources;

namespace BEditor.Primitive.Effects
{
    /// <summary>
    /// Represents an <see cref="EffectElement"/> that sets the OpenGL point light source.
    /// </summary>
    public class PointLightSource : EffectElement
    {
        /// <summary>
        /// Defines the <see cref="X"/> property.
        /// </summary>
        public static readonly DirectEditingProperty<PointLightSource, EaseProperty> XProperty = Coordinate.XProperty.WithOwner<PointLightSource>(
            owner => owner.X,
            (owner, obj) => owner.X = obj);

        /// <summary>
        /// Defines the <see cref="Y"/> property.
        /// </summary>
        public static readonly DirectEditingProperty<PointLightSource, EaseProperty> YProperty = Coordinate.YProperty.WithOwner<PointLightSource>(
            owner => owner.Y,
            (owner, obj) => owner.Y = obj);

        /// <summary>
        /// Defines the <see cref="Z"/> property.
        /// </summary>
        public static readonly DirectEditingProperty<PointLightSource, EaseProperty> ZProperty = Coordinate.ZProperty.WithOwner<PointLightSource>(
            owner => owner.Z,
            (owner, obj) => owner.Z = obj);

        /// <summary>
        /// Defines the <see cref="Ambient"/> property.
        /// </summary>
        public static readonly DirectEditingProperty<PointLightSource, ColorAnimationProperty> AmbientProperty = Material.AmbientProperty.WithOwner<PointLightSource>(
            owner => owner.Ambient,
            (owner, obj) => owner.Ambient = obj);

        /// <summary>
        /// Defines the <see cref="Diffuse"/> property.
        /// </summary>
        public static readonly DirectEditingProperty<PointLightSource, ColorAnimationProperty> DiffuseProperty = Material.DiffuseProperty.WithOwner<PointLightSource>(
            owner => owner.Diffuse,
            (owner, obj) => owner.Diffuse = obj);

        /// <summary>
        /// Defines the <see cref="Specular"/> property.
        /// </summary>
        public static readonly DirectEditingProperty<PointLightSource, ColorAnimationProperty> SpecularProperty = Material.SpecularProperty.WithOwner<PointLightSource>(
            owner => owner.Specular,
            (owner, obj) => owner.Specular = obj);

        /// <summary>
        /// Initializes a new instance of the <see cref="PointLightSource"/> class.
        /// </summary>
#pragma warning disable CS8618
        public PointLightSource()
#pragma warning restore CS8618
        {
        }

        /// <inheritdoc/>
        public override string Name => Strings.PointLightSource;

        /// <inheritdoc/>
        public override IEnumerable<PropertyElement> Properties
        {
            get
            {
                yield return X;
                yield return Y;
                yield return Z;
                yield return Ambient;
                yield return Diffuse;
                yield return Specular;
            }
        }

        /// <summary>
        /// Get the <see cref="EaseProperty"/> representing the X coordinate.
        /// </summary>
        public EaseProperty X { get; private set; }

        /// <summary>
        /// Get the <see cref="EaseProperty"/> representing the Y coordinate.
        /// </summary>
        public EaseProperty Y { get; private set; }

        /// <summary>
        /// Get the <see cref="EaseProperty"/> representing the Z coordinate.
        /// </summary>
        public EaseProperty Z { get; private set; }

        /// <summary>
        /// Gets the <see cref="ColorAnimationProperty"/> representing ambient.
        /// </summary>
        public ColorAnimationProperty Ambient { get; private set; }

        /// <summary>
        /// Gets the <see cref="ColorAnimationProperty"/> representing diffuse.
        /// </summary>
        public ColorAnimationProperty Diffuse { get; private set; }

        /// <summary>
        ///Gets the <see cref="ColorAnimationProperty"/> representing specular.
        /// </summary>
        public ColorAnimationProperty Specular { get; private set; }

        /// <inheritdoc/>
        public override void Render(EffectRenderArgs args)
        {
            var frame = args.Frame;

            Parent!.Parent!.GraphicsContext!.Light = new(
                new(X.GetValue(frame), Y.GetValue(frame), Z.GetValue(frame)),
                Ambient[frame],
                Diffuse[frame],
                Specular[frame]);
        }
    }
}