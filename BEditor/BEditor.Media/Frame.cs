using System;
using System.Diagnostics.CodeAnalysis;

namespace BEditor.Media
{
    public readonly struct Frame : IEquatable<Frame>
    {
        public Frame(int value)
        {
            Value = value;
        }

        public int Value { get; }

        public override readonly bool Equals(object? obj)
            => obj is Frame frame && Equals(frame);
        public readonly bool Equals(Frame other)
            => Value == other.Value;
        public override readonly int GetHashCode()
            => HashCode.Combine(Value);

        public static bool operator ==(Frame left, Frame right) => left.Equals(right);
        public static bool operator !=(Frame left, Frame right) => !(left == right);
        public static Frame operator +(Frame left, Frame right) => new(left.Value + right.Value);
        public static Frame operator -(Frame left, Frame right) => new(left.Value - right.Value);

        public static implicit operator int(Frame frame) => frame.Value;
        public static implicit operator Frame(int value) => new(value);
    }
}
