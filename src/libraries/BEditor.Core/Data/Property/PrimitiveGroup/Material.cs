// Material.cs
//
// Copyright (C) BEditor
//
// This software may be modified and distributed under the terms
// of the MIT license. See the LICENSE file for details.

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;

using BEditor.Drawing;
using BEditor.Resources;

namespace BEditor.Data.Property.PrimitiveGroup
{
    /// <summary>
    /// Represents a property that sets the Material.
    /// </summary>
    public sealed class Material : ExpandGroup
    {
        /// <summary>
        /// Defines the <see cref="Ambient"/> property.
        /// </summary>
        public static readonly DirectProperty<Material, ColorAnimationProperty> AmbientProperty = EditingProperty.RegisterDirect<ColorAnimationProperty, Material>(
            nameof(Ambient),
            owner => owner.Ambient,
            (owner, obj) => owner.Ambient = obj,
            EditingPropertyOptions<ColorAnimationProperty>.Create(new ColorAnimationPropertyMetadata(Strings.Ambient, Colors.White, true)).Serialize());

        /// <summary>
        /// Defines the <see cref="Diffuse"/> property.
        /// </summary>
        public static readonly DirectProperty<Material, ColorAnimationProperty> DiffuseProperty = EditingProperty.RegisterDirect<ColorAnimationProperty, Material>(
            nameof(Diffuse),
            owner => owner.Diffuse,
            (owner, obj) => owner.Diffuse = obj,
            EditingPropertyOptions<ColorAnimationProperty>.Create(new ColorAnimationPropertyMetadata(Strings.Diffuse, Colors.White, true)).Serialize());

        /// <summary>
        /// Defines the <see cref="Specular"/> property.
        /// </summary>
        public static readonly DirectProperty<Material, ColorAnimationProperty> SpecularProperty = EditingProperty.RegisterDirect<ColorAnimationProperty, Material>(
            nameof(Specular),
            owner => owner.Specular,
            (owner, obj) => owner.Specular = obj,
            EditingPropertyOptions<ColorAnimationProperty>.Create(new ColorAnimationPropertyMetadata(Strings.Specular, Colors.White, true)).Serialize());

        /// <summary>
        /// Defines the <see cref="Shininess"/> property.
        /// </summary>
        public static readonly DirectProperty<Material, EaseProperty> ShininessProperty = EditingProperty.RegisterDirect<EaseProperty, Material>(
            nameof(Shininess),
            owner => owner.Shininess,
            (owner, obj) => owner.Shininess = obj,
            EditingPropertyOptions<EaseProperty>.Create(new EasePropertyMetadata(Strings.Shininess, 10, float.NaN, 1)).Serialize());

        /// <summary>
        /// Initializes a new instance of the <see cref="Material"/> class.
        /// </summary>
        /// <param name="metadata">Metadata of this property.</param>
        /// <exception cref="ArgumentNullException"><paramref name="metadata"/> is <see langword="null"/>.</exception>
        public Material(MaterialMetadata metadata)
            : base(metadata)
        {
        }

        /// <summary>
        /// Gets the ambient.
        /// </summary>
        [AllowNull]
        public ColorAnimationProperty Ambient { get; private set; }

        /// <summary>
        /// Gets the diffuse.
        /// </summary>
        [AllowNull]
        public ColorAnimationProperty Diffuse { get; private set; }

        /// <summary>
        /// Gets the shininess.
        /// </summary>
        [AllowNull]
        public ColorAnimationProperty Specular { get; private set; }

        /// <summary>
        /// Gets the shininess.
        /// </summary>
        [AllowNull]
        public EaseProperty Shininess { get; private set; }

        /// <inheritdoc/>
        public override IEnumerable<PropertyElement> GetProperties()
        {
            yield return Ambient;
            yield return Diffuse;
            yield return Specular;
            yield return Shininess;
        }
    }

    /// <summary>
    /// The metadata of <see cref="Material"/>.
    /// </summary>
    /// <param name="Name">The string displayed in the property header.</param>
    public record MaterialMetadata(string Name) : PropertyElementMetadata(Name), IEditingPropertyInitializer<Material>
    {
        /// <inheritdoc/>
        public Material Create()
        {
            return new(this);
        }
    }
}