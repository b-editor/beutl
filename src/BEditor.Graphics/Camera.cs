// Camera.cs
//
// Copyright (C) BEditor
//
// This software may be modified and distributed under the terms
// of the MIT license. See the LICENSE file for details.

using System.Numerics;

using MathHelper = OpenTK.Mathematics.MathHelper;
using Matrix4 = OpenTK.Mathematics.Matrix4;

namespace BEditor.Graphics
{
    /// <summary>
    /// Represents an OpenGL camera.
    /// </summary>
    public abstract class Camera
    {
        private float _fov = MathHelper.PiOver2;
        private Vector3 _position;

        /// <summary>
        /// Initializes a new instance of the <see cref="Camera"/> class.
        /// </summary>
        /// <param name="position">The position of the camera.</param>
        protected Camera(Vector3 position)
        {
            Position = position;
        }

        /// <summary>
        /// Gets or sets the position of this <see cref="Camera"/>.
        /// </summary>
        public Vector3 Position { get => _position; set => _position = value; }

        /// <summary>
        /// Gets or sets the target position of this <see cref="Camera"/>.
        /// </summary>
        public Vector3 Target { get; set; }

        /// <summary>
        /// Gets or sets the Degrees representing the Fov of this <see cref="Camera"/>.
        /// </summary>
        public float Fov
        {
            get => MathHelper.RadiansToDegrees(_fov);
            set
            {
                var angle = MathHelper.Clamp(value, 1f, 179f);
                _fov = MathHelper.DegreesToRadians(angle);
            }
        }

        /// <summary>
        /// Gets or sets the range to be drawn by this <see cref="Camera"/>.
        /// </summary>
        public float Near { get; set; } = 0.1f;

        /// <summary>
        /// Gets or sets the range to be drawn by this <see cref="Camera"/>.
        /// </summary>
        public float Far { get; set; } = 20000;

        /// <summary>
        /// Gets the ViewMatrix.
        /// </summary>
        /// <returns>Returns the view matrix.</returns>
        public Matrix4x4 GetViewMatrix()
        {
            return Matrix4.LookAt(Position.ToOpenTK(), Target.ToOpenTK(), OpenTK.Mathematics.Vector3.UnitY).ToNumerics();
        }

        /// <summary>
        /// Gets ProjectionMatrix.
        /// </summary>
        /// <returns>Returns the projection matrix.</returns>
        public abstract Matrix4x4 GetProjectionMatrix();
    }
}