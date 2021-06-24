// MockCubeImpl.cs
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
    /// The cube implementation.
    /// </summary>
    public sealed class MockCubeImpl : DrawableImpl, ICubeImpl
    {
        /// <inheritdoc/>
        public float Width { get; }

        /// <inheritdoc/>
        public float Height { get; }

        /// <inheritdoc/>
        public float Depth { get; }

        /// <inheritdoc/>
        public float[] Vertices { get; } = Array.Empty<float>();
    }
}
