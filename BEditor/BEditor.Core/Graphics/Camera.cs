using System;
using System.Collections.Generic;
using System.Text;

using OpenTK.Mathematics;

namespace BEditor.Core.Graphics
{
    public abstract class Camera
    {
        private float _fov = MathHelper.PiOver2;

        public Camera(Vector3 position)
        {
            Position = position;
        }

        public Vector3 Position { get; set; }
        public Vector3 Target { get; set; }
        public float Fov
        {
            get => MathHelper.RadiansToDegrees(_fov);
            set
            {
                var angle = MathHelper.Clamp(value, 1f, 45f);
                _fov = MathHelper.DegreesToRadians(angle);
            }
        }
        public float Near { get; set; } = 0.1f;
        public float Far { get; set; } = 20000;

        public Matrix4 GetViewMatrix()
        {
            return Matrix4.LookAt(Position, Target, Vector3.UnitY);
        }

        public abstract Matrix4 GetProjectionMatrix();
    }
}
