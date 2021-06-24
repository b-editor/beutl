// Material.cs
//
// Copyright (C) BEditor
//
// This software may be modified and distributed under the terms
// of the MIT license. See the LICENSE file for details.

using System;

using BEditor.Drawing;

namespace BEditor.Graphics
{
    /// <summary>
    /// Represents a <see cref="Drawable"/> material structure.
    /// </summary>
    public struct Material : IEquatable<Material>
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="Material"/> struct.
        /// </summary>
        /// <param name="ambient">The ambient color.</param>
        /// <param name="diffuse">The diffuse color.</param>
        /// <param name="specular">The specular color.</param>
        /// <param name="shininess">The shininess.</param>
        public Material(Color ambient, Color diffuse, Color specular, float shininess)
        {
            Ambient = ambient;
            Diffuse = diffuse;
            Specular = specular;
            Shininess = shininess;
        }

        /// <summary>
        /// Gets the ambient color.
        /// </summary>
        public Color Ambient { get; }

        /// <summary>
        /// Gets the diffuse color.
        /// </summary>
        public Color Diffuse { get; }

        /// <summary>
        /// Gets the specular color.
        /// </summary>
        public Color Specular { get; }

        /// <summary>
        /// Gets the shininess.
        /// </summary>
        public float Shininess { get; }

        /// <summary>
        /// Compares two <see cref="Material"/>. The result specifies whether the values of the two <see cref="Material"/> are equal.
        /// </summary>
        /// <param name="left">A <see cref="Material"/> to compare.</param>
        /// <param name="right">A <see cref="Material"/> to compare.</param>
        /// <returns>true if the left and right <see cref="Material"/> structures are equal; otherwise, false.</returns>
        public static bool operator ==(Material left, Material right)
        {
            return left.Equals(right);
        }

        /// <summary>
        /// Determines whether or not the specified <see cref="Material"/> is not equal.
        /// </summary>
        /// <param name="left">A <see cref="Material"/> to compare.</param>
        /// <param name="right">A <see cref="Material"/> to compare.</param>
        /// <returns>True if the left and right <see cref="Material"/> are different, false otherwise.</returns>
        public static bool operator !=(Material left, Material right)
        {
            return !(left == right);
        }

        /// <inheritdoc/>
        public override bool Equals(object? obj)
        {
            return obj is Material material && Equals(material);
        }

        /// <inheritdoc/>
        public bool Equals(Material other)
        {
            return Ambient.Equals(other.Ambient) &&
                   Diffuse.Equals(other.Diffuse) &&
                   Specular.Equals(other.Specular) &&
                   Shininess == other.Shininess;
        }

        /// <inheritdoc/>
        public override int GetHashCode()
        {
            return HashCode.Combine(Ambient, Diffuse, Specular, Shininess);
        }
    }
}