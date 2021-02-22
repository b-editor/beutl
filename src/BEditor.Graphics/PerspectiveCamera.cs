using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;

using MathHelper = OpenTK.Mathematics.MathHelper;

namespace BEditor.Graphics
{
    public class PerspectiveCamera : Camera
    {
        public PerspectiveCamera(Vector3 position, float aspectRatio) : base(position)
        {
            AspectRatio = aspectRatio;
        }

        public float AspectRatio { get; set; }

        public override Matrix4x4 GetProjectionMatrix()
        {
            return Matrix4x4.CreatePerspectiveFieldOfView(MathHelper.DegreesToRadians(Fov), AspectRatio, Near, Far);
        }
    }
}
