// PerspectiveCamera.cs
//
// Copyright (C) BEditor
//
// This software may be modified and distributed under the terms
// of the MIT license. See the LICENSE file for details.

using System.Numerics;

namespace BEditor.Graphics
{
    /// <summary>
    /// Represents the perspective camera.
    /// </summary>
    public class PerspectiveCamera : Camera
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="PerspectiveCamera"/> class.
        /// </summary>
        /// <param name="position">The position of the camera.</param>
        /// <param name="aspectRatio">The aspect ratio of the camera's viewport.</param>
        public PerspectiveCamera(Vector3 position, float aspectRatio)
            : base(position)
        {
            AspectRatio = aspectRatio;
        }

        /// <summary>
        /// Gets or sets the aspect ratio of the camera's viewport.
        /// </summary>
        public float AspectRatio { get; set; }

        /// <inheritdoc/>
        public override Matrix4x4 GetProjectionMatrix()
        {
            return Matrix4x4.CreatePerspectiveFieldOfView(ToRadians(Fov), AspectRatio, Near, Far);
        }
    }
}