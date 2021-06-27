// DepthStencilState.cs
//
// Copyright (C) BEditor
//
// This software may be modified and distributed under the terms
// of the MIT license. See the LICENSE file for details.

using System;

namespace BEditor.Graphics
{
    /// <summary>
    /// A <see cref="GraphicsContext"/> component describing the properties of the depth stencil state.
    /// </summary>
    public struct DepthStencilState : IEquatable<DepthStencilState>
    {
        /// <summary>
        /// Describes a depth-only depth stencil state which uses a <see cref="ComparisonKind.LessEqual"/> comparison.
        /// The stencil test is disabled.
        /// </summary>
        public static readonly DepthStencilState DepthOnlyLessEqual = new()
        {
            DepthTestEnabled = true,
            DepthWriteEnabled = true,
            DepthComparison = ComparisonKind.LessEqual,
        };

        /// <summary>
        /// Describes a depth-only depth stencil state which uses a <see cref="ComparisonKind.LessEqual"/> comparison, and disables writing to the depth buffer.
        /// The stencil test is disabled.
        /// </summary>
        public static readonly DepthStencilState DepthOnlyLessEqualRead = new()
        {
            DepthTestEnabled = true,
            DepthWriteEnabled = false,
            DepthComparison = ComparisonKind.LessEqual,
        };

        /// <summary>
        /// Describes a depth-only depth stencil state which uses a <see cref="ComparisonKind.GreaterEqual"/> comparison.
        /// The stencil test is disabled.
        /// </summary>
        public static readonly DepthStencilState DepthOnlyGreaterEqual = new()
        {
            DepthTestEnabled = true,
            DepthWriteEnabled = true,
            DepthComparison = ComparisonKind.GreaterEqual,
        };

        /// <summary>
        /// Describes a depth-only depth stencil state which uses a <see cref="ComparisonKind.GreaterEqual"/> comparison, and
        /// disables writing to the depth buffer. The stencil test is disabled.
        /// </summary>
        public static readonly DepthStencilState DepthOnlyGreaterEqualRead = new()
        {
            DepthTestEnabled = true,
            DepthWriteEnabled = false,
            DepthComparison = ComparisonKind.GreaterEqual,
        };

        /// <summary>
        /// Describes a depth-only depth stencil state in which depth testing and writing is disabled.
        /// The stencil test is disabled.
        /// </summary>
        public static readonly DepthStencilState Disabled = new()
        {
            DepthTestEnabled = false,
            DepthWriteEnabled = false,
            DepthComparison = ComparisonKind.LessEqual,
        };

        /// <summary>
        /// Initializes a new instance of the <see cref="DepthStencilState"/> struct.
        /// </summary>
        /// <param name="depthTestEnabled">Whether depth testing is enabled.</param>
        /// <param name="depthWriteEnabled">Whether new depth values are written to the depth buffer.</param>
        /// <param name="comparisonKind">The <see cref="ComparisonKind"/> used when considering new depth values.</param>
        public DepthStencilState(bool depthTestEnabled, bool depthWriteEnabled, ComparisonKind comparisonKind)
        {
            DepthTestEnabled = depthTestEnabled;
            DepthWriteEnabled = depthWriteEnabled;
            DepthComparison = comparisonKind;

            StencilTestEnabled = false;
            StencilFront = default;
            StencilBack = default;
            StencilReadMask = 0;
            StencilWriteMask = 0;
            StencilReference = 0;
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="DepthStencilState"/> struct.
        /// </summary>
        /// <param name="depthTestEnabled">Whether depth testing is enabled.</param>
        /// <param name="depthWriteEnabled">Whether new depth values are written to the depth buffer.</param>
        /// <param name="comparisonKind">The <see cref="ComparisonKind"/> used when considering new depth values.</param>
        /// <param name="stencilTestEnabled">Whether the stencil test is enabled.</param>
        /// <param name="stencilFront">How stencil tests are handled for pixels whose surface faces towards the camera.</param>
        /// <param name="stencilBack">How stencil tests are handled for pixels whose surface faces away from the camera.</param>
        /// <param name="stencilReadMask">The portion of the stencil buffer used for reading.</param>
        /// <param name="stencilWriteMask">The portion of the stencil buffer used for writing.</param>
        /// <param name="stencilReference">The reference value to use when doing a stencil test.</param>
        public DepthStencilState(
            bool depthTestEnabled, bool depthWriteEnabled, ComparisonKind comparisonKind,
            bool stencilTestEnabled, StencilBehavior stencilFront, StencilBehavior stencilBack, byte stencilReadMask, byte stencilWriteMask, uint stencilReference)
        {
            DepthTestEnabled = depthTestEnabled;
            DepthWriteEnabled = depthWriteEnabled;
            DepthComparison = comparisonKind;

            StencilTestEnabled = stencilTestEnabled;
            StencilFront = stencilFront;
            StencilBack = stencilBack;
            StencilReadMask = stencilReadMask;
            StencilWriteMask = stencilWriteMask;
            StencilReference = stencilReference;
        }

        /// <summary>
        /// Gets or sets whether depth testing is enabled.
        /// </summary>
        public bool DepthTestEnabled { get; set; }

        /// <summary>
        /// Gets or sets whether new depth values are written to the depth buffer.
        /// </summary>
        public bool DepthWriteEnabled { get; set; }

        /// <summary>
        /// Gets or sets the <see cref="ComparisonKind"/> used when considering new depth values.
        /// </summary>
        public ComparisonKind DepthComparison { get; set; }

        /// <summary>
        /// Gets or sets the portion of the stencil buffer used for writing.
        /// </summary>
        public byte StencilWriteMask { get; set; }

        /// <summary>
        /// Gets or sets the reference value to use when doing a stencil test.
        /// </summary>
        public uint StencilReference { get; set; }

        /// <summary>
        /// Gets or sets how stencil tests are handled for pixels whose surface faces away from the camera.
        /// </summary>
        public StencilBehavior StencilBack { get; set; }

        /// <summary>
        /// Gets or sets how stencil tests are handled for pixels whose surface faces towards the camera.
        /// </summary>
        public StencilBehavior StencilFront { get; set; }

        /// <summary>
        /// Gets or sets whether the stencil test is enabled.
        /// </summary>
        public bool StencilTestEnabled { get; set; }

        /// <summary>
        /// Gets or sets the portion of the stencil buffer used for reading.
        /// </summary>
        public byte StencilReadMask { get; set; }

        /// <summary>
        /// Compares two <see cref="DepthStencilState"/>. The result specifies whether the values of the two <see cref="DepthStencilState"/> are equal.
        /// </summary>
        /// <param name="left">A <see cref="DepthStencilState"/> to compare.</param>
        /// <param name="right">A <see cref="DepthStencilState"/> to compare.</param>
        /// <returns>true if the left and right <see cref="DepthStencilState"/> structures are equal; otherwise, false.</returns>
        public static bool operator ==(DepthStencilState left, DepthStencilState right)
        {
            return left.Equals(right);
        }

        /// <summary>
        /// Determines whether or not the specified <see cref="DepthStencilState"/> is not equal.
        /// </summary>
        /// <param name="left">A <see cref="DepthStencilState"/> to compare.</param>
        /// <param name="right">A <see cref="DepthStencilState"/> to compare.</param>
        /// <returns>True if the left and right <see cref="DepthStencilState"/> are different, false otherwise.</returns>
        public static bool operator !=(DepthStencilState left, DepthStencilState right)
        {
            return !(left == right);
        }

        /// <inheritdoc/>
        public override bool Equals(object? obj)
        {
            return obj is DepthStencilState state && Equals(state);
        }

        /// <inheritdoc/>
        public bool Equals(DepthStencilState other)
        {
            return DepthTestEnabled == other.DepthTestEnabled &&
                   DepthWriteEnabled == other.DepthWriteEnabled &&
                   DepthComparison == other.DepthComparison &&
                   StencilWriteMask == other.StencilWriteMask &&
                   StencilReference == other.StencilReference &&
                   StencilBack.Equals(other.StencilBack) &&
                   StencilFront.Equals(other.StencilFront) &&
                   StencilTestEnabled == other.StencilTestEnabled &&
                   StencilReadMask == other.StencilReadMask;
        }

        /// <inheritdoc/>
        public override int GetHashCode()
        {
            var hash = default(HashCode);
            hash.Add(DepthTestEnabled);
            hash.Add(DepthWriteEnabled);
            hash.Add(DepthComparison);
            hash.Add(StencilWriteMask);
            hash.Add(StencilReference);
            hash.Add(StencilBack);
            hash.Add(StencilFront);
            hash.Add(StencilTestEnabled);
            hash.Add(StencilReadMask);
            return hash.ToHashCode();
        }

        /// <summary>
        /// Sets the depth test.
        /// </summary>
        /// <param name="depthTestEnabled">Whether depth testing is enabled.</param>
        /// <param name="depthWriteEnabled">Whether new depth values are written to the depth buffer.</param>
        /// <param name="comparisonKind">The <see cref="ComparisonKind"/> used when considering new depth values.</param>
        /// <returns>A new instance created by this method.</returns>
        public DepthStencilState WithDepth(bool depthTestEnabled, bool depthWriteEnabled, ComparisonKind comparisonKind)
        {
            return new(
                depthTestEnabled, depthWriteEnabled, comparisonKind,
                StencilTestEnabled, StencilFront, StencilBack, StencilReadMask, StencilWriteMask, StencilReference);
        }

        /// <summary>
        /// Sets the stencil test.
        /// </summary>
        /// <param name="stencilTestEnabled">Whether the stencil test is enabled.</param>
        /// <param name="stencilFront">How stencil tests are handled for pixels whose surface faces towards the camera.</param>
        /// <param name="stencilBack">How stencil tests are handled for pixels whose surface faces away from the camera.</param>
        /// <param name="stencilReadMask">The portion of the stencil buffer used for reading.</param>
        /// <param name="stencilWriteMask">The portion of the stencil buffer used for writing.</param>
        /// <param name="stencilReference">The reference value to use when doing a stencil test.</param>
        /// <returns>A new instance created by this method.</returns>
        public DepthStencilState WithStencil(bool stencilTestEnabled, StencilBehavior stencilFront, StencilBehavior stencilBack, byte stencilReadMask, byte stencilWriteMask, uint stencilReference)
        {
            return new(
                DepthTestEnabled, DepthWriteEnabled, DepthComparison,
                stencilTestEnabled, stencilFront, stencilBack, stencilReadMask, stencilWriteMask, stencilReference);
        }
    }
}