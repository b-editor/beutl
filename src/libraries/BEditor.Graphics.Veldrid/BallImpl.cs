using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;

namespace BEditor.Graphics.Veldrid
{
    public class BallImpl : DrawableImpl
    {
        private const int Count = 8;
        private readonly float[] _vertices;

        public BallImpl(float radiusX, float radiusY, float radiusZ)
        {
            RadiusX = radiusX;
            RadiusY = radiusY;
            RadiusZ = radiusZ;

            const float a = (float)(Math.PI / Count / 2);
            const float b = (float)(Math.PI / Count / 2);
            var verticesList = new List<float>();
            int k;
            for (k = -Count + 1; k <= Count; k++)
            {
                for (var i = 0; i <= Count * 4; i++)
                {
                    var vec1 = new Vector3(
                        radiusX * MathF.Cos(b * k) * MathF.Cos(a * i),
                        radiusY * MathF.Cos(b * k) * MathF.Sin(a * i),
                        radiusZ * MathF.Sin(b * k));
                    verticesList.Add(vec1.X);
                    verticesList.Add(vec1.Y);
                    verticesList.Add(vec1.Z);

                    var vec2 = new Vector3(
                        radiusX * MathF.Cos(b * (k - 1)) * MathF.Cos(a * i),
                        radiusY * MathF.Cos(b * (k - 1)) * MathF.Sin(a * i),
                        radiusZ * MathF.Sin(b * (k - 1)));
                    verticesList.Add(vec2.X);
                    verticesList.Add(vec2.Y);
                    verticesList.Add(vec2.Z);
                }
            }

            _vertices = verticesList.ToArray();
        }

        public float RadiusX { get; }

        public float RadiusY { get; }

        public float RadiusZ { get; }

        public float[] Vertices => _vertices;
    }
}