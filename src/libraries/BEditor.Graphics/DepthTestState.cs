// DepthTestState.cs
//
// Copyright (C) BEditor
//
// This software may be modified and distributed under the terms
// of the MIT license. See the LICENSE file for details.

namespace BEditor.Graphics
{
    /// <summary>
    /// A <see cref="GraphicsContext"/> component describing the properties of the depth state.
    /// </summary>
    public readonly struct DepthTestState
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="DepthTestState"/> struct.
        /// </summary>
        /// <param name="depthTestEnabled">Whether depth testing is enabled.</param>
        /// <param name="depthWriteEnabled">Whether new depth values are written to the depth buffer.</param>
        /// <param name="comparisonKind">The <see cref="ComparisonKind"/> used when considering new depth values.</param>
        public DepthTestState(bool depthTestEnabled, bool depthWriteEnabled, ComparisonKind comparisonKind)
        {
            (Enabled, WriteEnabled, Comparison) = (depthTestEnabled, depthWriteEnabled, comparisonKind);
        }

        /// <summary>
        /// Gets whether depth testing is enabled.
        /// </summary>
        public bool Enabled { get; }

        /// <summary>
        /// Gets whether new depth values are written to the depth buffer.
        /// </summary>
        public bool WriteEnabled { get; }

        /// <summary>
        /// Gets the <see cref="ComparisonKind"/> used when considering new depth values.
        /// </summary>
        public ComparisonKind Comparison { get; }
    }
}