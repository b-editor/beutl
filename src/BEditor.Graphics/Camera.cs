using System.Numerics;

using MathHelper = OpenTK.Mathematics.MathHelper;
using Matrix4 = OpenTK.Mathematics.Matrix4;

namespace BEditor.Graphics
{
    /// <summary>
    /// Represents an OpenGL camera
    /// </summary>
    public abstract class Camera
    {
        private float _fov = MathHelper.PiOver2;
        private Vector3 position;

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
        public Vector3 Position { get => position; set => position = value; }

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
                var angle = MathHelper.Clamp(value, 1f, 179f);
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
        public Matrix4x4 GetViewMatrix()
        {
            return Matrix4.LookAt(Position.ToOpenTK(), Target.ToOpenTK(), OpenTK.Mathematics.Vector3.UnitY).ToNumerics();
        }

        /// <summary>
        /// Get ProjectionMatrix.
        /// </summary>
        public abstract Matrix4x4 GetProjectionMatrix();
    }
}