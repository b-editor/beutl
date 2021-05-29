// Light.cs
//
// Copyright (C) BEditor
//
// This software may be modified and distributed under the terms
// of the MIT license. See the LICENSE file for details.

using System.Numerics;

using BEditor.Drawing;

namespace BEditor.Graphics
{
    /// <summary>
    /// Represents an OpenGL light.
    /// </summary>
    public class Light
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="Light"/> class.
        /// </summary>
        /// <param name="pos">The position of the lights.</param>
        /// <param name="ambient">The ambient color.</param>
        /// <param name="diffuse">The diffuse color.</param>
        /// <param name="specular">The specular color.</param>
        public Light(Vector3 pos, Color ambient, Color diffuse, Color specular)
        {
            Position = pos;
            Ambient = ambient;
            Diffuse = diffuse;
            Specular = specular;
        }

        /// <summary>
        /// Gets or sets the ambient color of this <see cref="Light"/>.
        /// </summary>
        public Color Ambient { get; set; }

        /// <summary>
        /// Gets or sets the diffuse color of this <see cref="Light"/>.
        /// </summary>
        public Color Diffuse { get; set; }

        /// <summary>
        /// Gets or sets the specular color of this <see cref="Light"/>.
        /// </summary>
        public Color Specular { get; set; }

        /// <summary>
        /// Gets or sets the position of this <see cref="Light"/>.
        /// </summary>
        public Vector3 Position { get; set; }
    }
}