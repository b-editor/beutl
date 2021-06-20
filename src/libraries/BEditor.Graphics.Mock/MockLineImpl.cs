using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;

using BEditor.Graphics.Platform;

namespace BEditor.Graphics.Mock
{
    public sealed class MockLineImpl : MockDrawableImpl, ILineImpl
    {
        public ReadOnlyMemory<float> Vertices { get; }

        public Vector3 Start { get; }

        public Vector3 End { get; }

        public float Width { get; }
    }
}
