using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using Vector3 = System.Numerics.Vector3;
using Matrix4 = System.Numerics.Matrix4x4;
using OpenTK.Mathematics;
using System.Diagnostics.Contracts;

namespace BEditor.Graphics
{
    public struct Transform
    {
        public static readonly Transform Zero;
        public static readonly Transform Default = new(new(0, 0, 0), new(0, 0, 0), new(0, 0, 0), new(1, 1, 1));

        private Transform(Vector3 coord, Vector3 center, Vector3 rotate, Vector3 scale)
        {
            Coordinate = coord;
            Center = center;
            Rotate = rotate;
            Scale = scale;
        }

        public Vector3 Coordinate { get; set; }
        public Vector3 Center { get; set; }
        public Vector3 Rotate { get; set; }
        public Vector3 Scale { get; set; }
        public Matrix4 Matrix
        {
            get
            {
                return Matrix4.Identity
                    * Matrix4.CreateTranslation(Center)
                        * Matrix4.CreateRotationX(MathHelper.DegreesToRadians(Rotate.X))
                        * Matrix4.CreateRotationY(MathHelper.DegreesToRadians(Rotate.Y))
                        * Matrix4.CreateRotationZ(MathHelper.DegreesToRadians(Rotate.Z))
                            * Matrix4.CreateScale(Scale)
                                * Matrix4.CreateTranslation(Coordinate);
            }
        }

        [Pure]
        public static Transform Create(Vector3 coord, Vector3 center, Vector3 rotate, Vector3 scale)
        {
            return new Transform(coord, center, rotate, scale);
        }

        public static Transform operator +(Transform left, Transform right)
        {
            return new(
                left.Coordinate + right.Coordinate,
                left.Center + right.Center,
                left.Rotate + right.Rotate,
                left.Scale + right.Scale);
        }
        public static Transform operator -(Transform left, Transform right)
        {
            return new(
                left.Coordinate - right.Coordinate,
                left.Center - right.Center,
                left.Rotate - right.Rotate,
                left.Scale - right.Scale);
        }
        public static Transform operator *(Transform left, Transform right)
        {
            return new(
                left.Coordinate * right.Coordinate,
                left.Center * right.Center,
                left.Rotate * right.Rotate,
                left.Scale * right.Scale);
        }
        public static Transform operator /(Transform left, Transform right)
        {
            return new(
                left.Coordinate / right.Coordinate,
                left.Center / right.Center,
                left.Rotate / right.Rotate,
                left.Scale / right.Scale);
        }
    }
}
