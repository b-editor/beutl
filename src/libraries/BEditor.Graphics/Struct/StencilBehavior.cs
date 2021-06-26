// StencilBehavior.cs
//
// Copyright (C) BEditor
//
// This software may be modified and distributed under the terms
// of the MIT license. See the LICENSE file for details.

using System;

namespace BEditor.Graphics
{
    /// <summary>
    /// Describes how stencil tests are performed in a depth-stencil state.
    /// </summary>
    public struct StencilBehavior : IEquatable<StencilBehavior>
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="StencilBehavior"/> struct.
        /// </summary>
        /// <param name="fail">The operation performed on samples that fail the stencil test.</param>
        /// <param name="pass">The operation performed on samples that pass the stencil test.</param>
        /// <param name="depthFail">The operation performed on samples that pass the stencil test but fail the depth test.</param>
        /// <param name="comparison">The comparison operator used in the stencil test.</param>
        public StencilBehavior(StencilOperation fail, StencilOperation pass, StencilOperation depthFail, ComparisonKind comparison)
        {
            (Fail, Pass, DepthFail, Comparison) = (fail, pass, depthFail, comparison);
        }

        /// <summary>
        /// Gets or sets the operation performed on samples that fail the stencil test.
        /// </summary>
        public StencilOperation Fail { get; set; }

        /// <summary>
        /// Gets or sets the operation performed on samples that pass the stencil test.
        /// </summary>
        public StencilOperation Pass { get; set; }

        /// <summary>
        /// Gets or sets the operation performed on samples that pass the stencil test but fail the depth test.
        /// </summary>
        public StencilOperation DepthFail { get; set; }

        /// <summary>
        /// Gets or sets the comparison operator used in the stencil test.
        /// </summary>
        public ComparisonKind Comparison { get; set; }

        /// <summary>
        /// Compares two <see cref="StencilBehavior"/>. The result specifies whether the values of the two <see cref="StencilBehavior"/> are equal.
        /// </summary>
        /// <param name="left">A <see cref="StencilBehavior"/> to compare.</param>
        /// <param name="right">A <see cref="StencilBehavior"/> to compare.</param>
        /// <returns>true if the left and right <see cref="StencilBehavior"/> structures are equal; otherwise, false.</returns>
        public static bool operator ==(StencilBehavior left, StencilBehavior right)
        {
            return left.Equals(right);
        }

        /// <summary>
        /// Determines whether or not the specified <see cref="StencilBehavior"/> is not equal.
        /// </summary>
        /// <param name="left">A <see cref="StencilBehavior"/> to compare.</param>
        /// <param name="right">A <see cref="StencilBehavior"/> to compare.</param>
        /// <returns>True if the left and right <see cref="StencilBehavior"/> are different, false otherwise.</returns>
        public static bool operator !=(StencilBehavior left, StencilBehavior right)
        {
            return !(left == right);
        }

        /// <inheritdoc/>
        public override bool Equals(object? obj)
        {
            return obj is StencilBehavior behavior && Equals(behavior);
        }

        /// <inheritdoc/>
        public bool Equals(StencilBehavior other)
        {
            return Fail == other.Fail &&
                   Pass == other.Pass &&
                   DepthFail == other.DepthFail &&
                   Comparison == other.Comparison;
        }

        /// <inheritdoc/>
        public override int GetHashCode()
        {
            return HashCode.Combine(Fail, Pass, DepthFail, Comparison);
        }
    }
}