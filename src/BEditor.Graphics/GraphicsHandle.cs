using System;

namespace BEditor.Graphics
{
    public readonly struct GraphicsHandle : IEquatable<GraphicsHandle>
    {
        public GraphicsHandle(int handle)
        {
            Handle = handle;
        }

        public int Handle { get; }

        public override bool Equals(object? obj)
        {
            return obj is GraphicsHandle handle && Equals(handle);
        }

        public bool Equals(GraphicsHandle other)
        {
            return Handle == other.Handle;
        }

        public override int GetHashCode()
        {
            return HashCode.Combine(Handle);
        }

        public static bool operator ==(GraphicsHandle left, GraphicsHandle right)
        {
            return left.Equals(right);
        }

        public static bool operator !=(GraphicsHandle left, GraphicsHandle right)
        {
            return !(left == right);
        }

        public static implicit operator int(GraphicsHandle handle)
        {
            return handle.Handle;
        }

        public static implicit operator GraphicsHandle(int handle)
        {
            return new(handle);
        }
    }
}
