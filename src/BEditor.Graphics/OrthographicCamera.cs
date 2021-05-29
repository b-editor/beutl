// OrthographicCamera.cs
//
// Copyright (C) BEditor
//
// This software may be modified and distributed under the terms
// of the MIT license. See the LICENSE file for details.

using System.Numerics;

namespace BEditor.Graphics
{
    /// <summary>
    /// Represents an orthographic camera.
    /// </summary>
    public class OrthographicCamera : Camera
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="OrthographicCamera"/> class.
        /// </summary>
        /// <param name="position">The position of the camera.</param>
        /// <param name="width">The width of the view volume.</param>
        /// <param name="height">The height of the view volume.</param>
        public OrthographicCamera(Vector3 position, float width, float height)
            : base(position)
        {
            Width = width;
            Height = height;
        }

        /// <summary>
        /// Gets or sets the width of the view volume.
        /// </summary>
        public float Width { get; set; }

        /// <summary>
        /// Gets or sets the height of the view volume.
        /// </summary>
        public float Height { get; set; }

        /// <inheritdoc/>
        public override Matrix4x4 GetProjectionMatrix()
        {
            return Matrix4x4.CreateOrthographic(Width, Height, Near, Far);
        }
    }
}