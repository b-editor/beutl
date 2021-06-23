using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;

using BEditor.Graphics.Platform;

namespace BEditor.Graphics.Veldrid
{
    public sealed class LineImpl : DrawableImpl, ILineImpl
    {
        public LineImpl(Vector3 start, Vector3 end, float width)
        {
            Start = start;
            End = end;
            Width = width;

            Vertices = new float[]
            {
                start.X, start.Y, start.Z,
                end.X, end.Y, end.Z,
            };
        }

        public float[] Vertices { get; }

        public Vector3 Start { get; }

        public Vector3 End { get; }

        public float Width { get; }
    }
}