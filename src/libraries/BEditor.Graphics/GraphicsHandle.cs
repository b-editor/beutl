// GraphicsHandle.cs
//
// Copyright (C) BEditor
//
// This software may be modified and distributed under the terms
// of the MIT license. See the LICENSE file for details.

using System;

namespace BEditor.Graphics
{
    /// <summary>
    /// Represents the handle of an OpenGL object.
    /// </summary>
    public readonly struct GraphicsHandle : IEquatable<GraphicsHandle>
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="GraphicsHandle"/> struct.
        /// </summary>
        /// <param name="handle">The value of the handle.</param>
        public GraphicsHandle(int handle)
        {
            Handle = handle;
        }

        /// <summary>
        /// Gets the value of this handle.
        /// </summary>
        public int Handle { get; }

        /// <summary>
        /// Converts the <see cref="GraphicsHandle"/> to a 32-bit signed integer.
        /// </summary>
        /// <param name="handle">A graphics handle.</param>
        public static implicit operator int(GraphicsHandle handle)
        {
            return handle.Handle;
        }

        /// <summary>
        /// Converts the 32-bit signed integer to a <see cref="GraphicsHandle"/>.
        /// </summary>
        /// <param name="handle">A 32-bit signed integer.</param>
        public static implicit operator GraphicsHandle(int handle)
        {
            return new(handle);
        }

        /// <summary>
        /// Indicates whether two <see cref="GraphicsHandle"/> instances are equal.
        /// </summary>
        /// <param name="left">The first time interval to compare.</param>
        /// <param name="right">The second time interval to compare.</param>
        /// <returns>true if the values of <paramref name="left"/> and <paramref name="right"/> are equal; otherwise, false.</returns>
        public static bool operator ==(GraphicsHandle left, GraphicsHandle right)
        {
            return left.Equals(right);
        }

        /// <summary>
        /// Indicates whether two <see cref="GraphicsHandle"/> instances are not equal.
        /// </summary>
        /// <param name="left">The first time interval to compare.</param>
        /// <param name="right">The second time interval to compare.</param>
        /// <returns>true if the values of <paramref name="left"/> and <paramref name="right"/> are not equal; otherwise, false.</returns>
        public static bool operator !=(GraphicsHandle left, GraphicsHandle right)
        {
            return !(left == right);
        }

        /// <inheritdoc/>
        public override bool Equals(object? obj)
        {
            return obj is GraphicsHandle handle && Equals(handle);
        }

        /// <inheritdoc/>
        public bool Equals(GraphicsHandle other)
        {
            return Handle == other.Handle;
        }

        /// <inheritdoc/>
        public override int GetHashCode()
        {
            return HashCode.Combine(Handle);
        }
    }
}