// MockBallImpl.cs
//
// Copyright (C) BEditor
//
// This software may be modified and distributed under the terms
// of the MIT license. See the LICENSE file for details.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using BEditor.Graphics.Platform;

namespace BEditor.Graphics.Skia
{
    /// <summary>
    /// The ball implementation.
    /// </summary>
    public sealed class MockBallImpl : DrawableImpl, IBallImpl
    {
        /// <inheritdoc/>
        public float RadiusX { get; }

        /// <inheritdoc/>
        public float RadiusY { get; }

        /// <inheritdoc/>
        public float RadiusZ { get; }

        /// <inheritdoc/>
        public float[] Vertices { get; } = Array.Empty<float>();
    }
}
