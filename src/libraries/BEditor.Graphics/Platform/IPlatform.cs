// IPlatform.cs
//
// Copyright (C) BEditor
//
// This software may be modified and distributed under the terms
// of the MIT license. See the LICENSE file for details.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;

using BEditor.Drawing;
using BEditor.Drawing.Pixel;
using BEditor.Graphics.Resources;

namespace BEditor.Graphics.Platform
{
    /// <summary>
    /// Represents the platform.
    /// </summary>
    public interface IPlatform
    {
        private static IPlatform? _current;

        /// <summary>
        /// Gets or sets the current platform.
        /// </summary>
        public static IPlatform Current
        {
            get => _current ?? throw new GraphicsException(Strings.PlatformIsNotSet);
            set => _current = value;
        }

        /// <summary>
        /// Creates the graphics context.
        /// </summary>
        /// <param name="width">The width of the graphics context.</param>
        /// <param name="height">The height of the graphics context.</param>
        /// <returns>Returns the implementation created by this method.</returns>
        public IGraphicsContextImpl CreateContext(int width, int height);
    }
}