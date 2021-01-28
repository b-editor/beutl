using System;
using System.Collections.Generic;
using System.Text;

using OpenTK.Mathematics;

namespace BEditor.Graphics
{
    /// <summary>
    /// Represents an OpenGL camera
    /// </summary>
    public abstract class Camera
    {
        private float _fov = MathHelper.PiOver2;

        /// <summary>
        /// Initializes a new instance of the <see cref="Camera"/> class.
        /// </summary>
        /// <param name="position">Camera position</param>
        public Camera(Vector3 position)
        {
            Position = position;
        }

        /// <summary>
        /// Gets or sets the position of this <see cref="Camera"/>.
        /// </summary>
        public Vector3 Position { get; set; }
        /// <summary>
        /// Gets or sets the target position of this <see cref="Camera"/>.
        /// </summary>
        public Vector3 Target { get; set; }
        /// <summary>
        /// Sets or gets the Degrees representing the Fov of this <see cref="Camera"/>.
        /// </summary>
        public float Fov
        {
            get => MathHelper.RadiansToDegrees(_fov);
            set
            {
                var angle = MathHelper.Clamp(value, 1f, 45f);
                _fov = MathHelper.DegreesToRadians(angle);
            }
        }
        /// <summary>
        /// Sets or gets the range to be drawn by this <see cref="Camera"/>.
        /// </summary>
        public float Near { get; set; } = 0.1f;
        /// <summary>
        /// Sets or gets the range to be drawn by this <see cref="Camera"/>.
        /// </summary>
        public float Far { get; set; } = 20000;

        /// <summary>
        /// Get ViewMatrix.
        /// </summary>
        public Matrix4 GetViewMatrix()
        {
            return Matrix4.LookAt(Position, Target, Vector3.UnitY);
        }
        /// <summary>
        /// Get ProjectionMatrix.
        /// </summary>
        public abstract Matrix4 GetProjectionMatrix();
    }
}
