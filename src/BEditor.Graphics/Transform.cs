using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using OpenTK.Mathematics;

using Vector3 = System.Numerics.Vector3;

namespace BEditor.Graphics
{
    public struct Transform
    {
        public Transform(Matrix4 matrix)
        {
            Matrix = matrix;
        }

        public Matrix4 Matrix { get; }

        public static Transform Create(Vector3 coord, Vector3 center, Vector3 rotate, Vector3 scale)
        {
            var model = Matrix4.Identity
                * Matrix4.CreateTranslation(center.ToOpenTK())
                    * Matrix4.CreateRotationX(MathHelper.DegreesToRadians(rotate.X))
                    * Matrix4.CreateRotationY(MathHelper.DegreesToRadians(rotate.Y))
                    * Matrix4.CreateRotationZ(MathHelper.DegreesToRadians(rotate.Z))
                        * Matrix4.CreateTranslation(coord.ToOpenTK())
                            * Matrix4.CreateScale(scale.ToOpenTK());

            return new Transform(model);
        }
    }
}
