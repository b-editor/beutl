// RasterizerState.cs
//
// Copyright (C) BEditor
//
// This software may be modified and distributed under the terms
// of the MIT license. See the LICENSE file for details.

using System;

namespace BEditor.Graphics
{
    /// <summary>
    /// A <see cref="GraphicsContext"/> component describing the properties of the rasterizer.
    /// </summary>
    public struct RasterizerState : IEquatable<RasterizerState>
    {
        /// <summary>
        /// Describes the default rasterizer state, with clockwise backface culling, solid polygon filling, and both depth
        /// clipping and scissor tests enabled.
        /// </summary>
        public static readonly RasterizerState Default = new()
        {
            CullMode = FaceCullMode.Back,
            FillMode = PolygonFillMode.Solid,
            FrontFace = FrontFace.Clockwise,
            DepthClipEnabled = true,
            ScissorTestEnabled = false,
        };

        /// <summary>
        /// Describes a rasterizer state with no culling, solid polygon filling, and both depth clipping and scissor tests enabled.
        /// </summary>
        public static readonly RasterizerState CullNone = new()
        {
            CullMode = FaceCullMode.None,
            FillMode = PolygonFillMode.Solid,
            FrontFace = FrontFace.Clockwise,
            DepthClipEnabled = true,
            ScissorTestEnabled = false,
        };

        /// <summary>
        /// Initializes a new instance of the <see cref="RasterizerState"/> struct.
        /// </summary>
        /// <param name="cullMode">Controls which face will be culled.</param>
        /// <param name="fillMode">Controls how the rasterizer fills polygons.</param>
        /// <param name="frontFace">Controls the winding order used to determine the front face of primitives.</param>
        /// <param name="depthClipEnabled">Controls whether depth clipping is enabled.</param>
        /// <param name="scissorTestEnabled">Controls whether the scissor test is enabled.</param>
        public RasterizerState(
            FaceCullMode cullMode,
            PolygonFillMode fillMode,
            FrontFace frontFace,
            bool depthClipEnabled,
            bool scissorTestEnabled)
        {
            CullMode = cullMode;
            FillMode = fillMode;
            FrontFace = frontFace;
            DepthClipEnabled = depthClipEnabled;
            ScissorTestEnabled = scissorTestEnabled;
        }

        /// <summary>
        /// Gets or sets which face will be culled.
        /// </summary>
        public FaceCullMode CullMode { get; set; }

        /// <summary>
        /// Gets or sets how the rasterizer fills polygons.
        /// </summary>
        public PolygonFillMode FillMode { get; set; }

        /// <summary>
        /// Gets or sets the winding order used to determine the front face of primitives.
        /// </summary>
        public FrontFace FrontFace { get; set; }

        /// <summary>
        /// Gets or sets whether depth clipping is enabled.
        /// </summary>
        public bool DepthClipEnabled { get; set; }

        /// <summary>
        /// Gets or sets whether the scissor test is enabled.
        /// </summary>
        public bool ScissorTestEnabled { get; set; }

        /// <summary>
        /// Compares two <see cref="RasterizerState"/>. The result specifies whether the values of the two <see cref="RasterizerState"/> are equal.
        /// </summary>
        /// <param name="left">A <see cref="RasterizerState"/> to compare.</param>
        /// <param name="right">A <see cref="RasterizerState"/> to compare.</param>
        /// <returns>true if the left and right <see cref="RasterizerState"/> structures are equal; otherwise, false.</returns>
        public static bool operator ==(RasterizerState left, RasterizerState right)
        {
            return left.Equals(right);
        }

        /// <summary>
        /// Determines whether or not the specified <see cref="RasterizerState"/> is not equal.
        /// </summary>
        /// <param name="left">A <see cref="RasterizerState"/> to compare.</param>
        /// <param name="right">A <see cref="RasterizerState"/> to compare.</param>
        /// <returns>True if the left and right <see cref="RasterizerState"/> are different, false otherwise.</returns>
        public static bool operator !=(RasterizerState left, RasterizerState right)
        {
            return !(left == right);
        }

        /// <inheritdoc/>
        public override bool Equals(object? obj)
        {
            return obj is RasterizerState state && Equals(state);
        }

        /// <inheritdoc/>
        public bool Equals(RasterizerState other)
        {
            return CullMode == other.CullMode &&
                   FillMode == other.FillMode &&
                   FrontFace == other.FrontFace &&
                   DepthClipEnabled == other.DepthClipEnabled &&
                   ScissorTestEnabled == other.ScissorTestEnabled;
        }

        /// <inheritdoc/>
        public override int GetHashCode()
        {
            return HashCode.Combine(CullMode, FillMode, FrontFace, DepthClipEnabled, ScissorTestEnabled);
        }
    }
}