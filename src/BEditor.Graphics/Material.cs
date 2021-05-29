// Material.cs
//
// Copyright (C) BEditor
//
// This software may be modified and distributed under the terms
// of the MIT license. See the LICENSE file for details.

using BEditor.Drawing;

namespace BEditor.Graphics
{
    /// <summary>
    /// Represents a <see cref="GraphicsObject"/> material structure.
    /// </summary>
    public struct Material
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
    }
}