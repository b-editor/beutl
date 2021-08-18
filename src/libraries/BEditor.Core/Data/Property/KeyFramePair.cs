// ColorAnimationProperty.cs
//
// Copyright (C) BEditor
//
// This software may be modified and distributed under the terms
// of the MIT license. See the LICENSE file for details.

using System;
using System.Collections.Generic;
using System.Reflection;

namespace BEditor.Data.Property
{
    /// <summary>
    /// Represents a position and value pair.
    /// </summary>
    /// <typeparam name="T">The type of the value.</typeparam>
    public readonly struct KeyFramePair<T> : IEquatable<KeyFramePair<T>>
        where T : notnull
    {
        public KeyFramePair(PositionInfo position, T value)
        {
            Position = position;
            Value = value;
        }

        public KeyFramePair(float position, T value, PositionType type)
        {
            Position = new(position, type);
            Value = value;
        }

        /// <summary>
        /// Gets or sets the value.
        /// </summary>
        public T Value { get; }

        /// <summary>
        /// Gets or sets the position.
        /// </summary>
        public PositionInfo Position { get; }

        public static bool operator ==(KeyFramePair<T> left, KeyFramePair<T> right)
        {
            return left.Equals(right);
        }

        public static bool operator !=(KeyFramePair<T> left, KeyFramePair<T> right)
        {
            return !(left == right);
        }

        public static bool TryParse(string s, out KeyFramePair<T> result)
        {
            var strs = s.Split(',');
            if (strs.Length < 2)
            {
                result = default;
                return false;
            }

            if (!PositionInfo.TryParse(strs[0], out var pos))
            {
                result = default;
                return false;
            }

            var meth = typeof(T).GetMethod(
                "Parse",
                BindingFlags.Public | BindingFlags.Static | BindingFlags.Public | BindingFlags.InvokeMethod,
                null,
                new Type[] { typeof(string) },
                null);
            var value = meth?.Invoke(null, new object[] { strs[1] });
            if (value is not T)
            {
                result = default;
                return false;
            }

            result = new KeyFramePair<T>(pos, (T)value);
            return true;
        }

        public static KeyFramePair<T> Parse(string s)
        {
            if (TryParse(s, out var result))
            {
                return result;
            }
            else
            {
                throw new Exception($"\"{s}\" could not be parsed.");
            }
        }

        public KeyFramePair<T> WithPosition(float position)
        {
            return new(position, Value, Position.Type);
        }

        public KeyFramePair<T> WithValue(T value)
        {
            return new(Position, value);
        }

        public KeyFramePair<T> WithType(PositionType type)
        {
            return new(Position.Value, Value, type);
        }

        public KeyFramePair<T> WithType(PositionType type, float length)
        {
            if (type == Position.Type) return this;

            if (Position.Type == PositionType.Absolute)
            {
                // 割合に変換
                return new(Position.Value / length, Value, type);
            }
            else
            {
                return new(Position.Value * length, Value, type);
            }
        }

        /// <inheritdoc/>
        public override string ToString()
        {
            var pos = Position.ToString();
            var value = Value.ToString();
            return string.Join(",", pos, value);
        }

        public override bool Equals(object? obj)
        {
            return obj is KeyFramePair<T> pair && Equals(pair);
        }

        public bool Equals(KeyFramePair<T> other)
        {
            return EqualityComparer<T>.Default.Equals(Value, other.Value) &&
                   Position.Equals(other.Position);
        }

        public override int GetHashCode()
        {
            return HashCode.Combine(Value, Position);
        }
    }
}