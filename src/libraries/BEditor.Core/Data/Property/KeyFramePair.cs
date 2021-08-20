// KeyFramePair.cs
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
        /// <summary>
        /// Initializes a new instance of the <see cref="KeyFramePair{T}"/> struct.
        /// </summary>
        /// <param name="position">The position.</param>
        /// <param name="value">The value.</param>
        public KeyFramePair(PositionInfo position, T value)
        {
            Position = position;
            Value = value;
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="KeyFramePair{T}"/> struct.
        /// </summary>
        /// <param name="position">The position.</param>
        /// <param name="value">The value.</param>
        /// <param name="type">The type of the position.</param>
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

        /// <summary>
        /// Indicates whether two <see cref="KeyFramePair{T}"/> instances are equal.
        /// </summary>
        /// <param name="left">The first color to compare.</param>
        /// <param name="right">The second color to compare.</param>
        /// <returns>true if the values of <paramref name="left"/> and <paramref name="right"/> are equal; otherwise, false.</returns>
        public static bool operator ==(KeyFramePair<T> left, KeyFramePair<T> right)
        {
            return left.Equals(right);
        }

        /// <summary>
        /// Indicates whether two <see cref="KeyFramePair{T}"/> instances are not equal.
        /// </summary>
        /// <param name="left">The first color to compare.</param>
        /// <param name="right">The second color to compare.</param>
        /// <returns>true if the values of <paramref name="left"/> and <paramref name="right"/> are not equal; otherwise, false.</returns>
        public static bool operator !=(KeyFramePair<T> left, KeyFramePair<T> right)
        {
            return !(left == right);
        }

        /// <summary>
        /// Parses a <see cref="KeyFramePair{T}"/> string.
        /// A return value indicates whether the conversion succeeded or failed.
        /// </summary>
        /// <param name="s">The string.</param>
        /// <param name="result">The parsed <see cref="KeyFramePair{T}"/>.</param>
        /// <returns>true if s was parsed successfully; otherwise, false.</returns>
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

        /// <summary>
        /// Parses a <see cref="KeyFramePair{T}"/> string.
        /// </summary>
        /// <param name="s">The string.</param>
        /// <returns>The parsed <see cref="KeyFramePair{T}"/>.</returns>
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

        /// <summary>
        /// Returns a new <see cref="KeyFramePair{T}"/> with the specified position.
        /// </summary>
        /// <param name="position">The position.</param>
        /// <returns>The new <see cref="KeyFramePair{T}"/>.</returns>
        public KeyFramePair<T> WithPosition(float position)
        {
            return new(position, Value, Position.Type);
        }

        /// <summary>
        /// Returns a new <see cref="KeyFramePair{T}"/> with the specified value.
        /// </summary>
        /// <param name="value">The value.</param>
        /// <returns>The new <see cref="KeyFramePair{T}"/>.</returns>
        public KeyFramePair<T> WithValue(T value)
        {
            return new(Position, value);
        }

        /// <summary>
        /// Returns a new <see cref="KeyFramePair{T}"/> with the specified position type.
        /// </summary>
        /// <param name="type">The position type.</param>
        /// <returns>The new <see cref="KeyFramePair{T}"/>.</returns>
        public KeyFramePair<T> WithType(PositionType type)
        {
            return new(Position.Value, Value, type);
        }

        /// <summary>
        /// Returns a new <see cref="KeyFramePair{T}"/> with the specified position type.
        /// </summary>
        /// <param name="type">The position type.</param>
        /// <param name="length">The length.</param>
        /// <returns>The new <see cref="KeyFramePair{T}"/>.</returns>
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

        /// <inheritdoc/>
        public override bool Equals(object? obj)
        {
            return obj is KeyFramePair<T> pair && Equals(pair);
        }

        /// <inheritdoc/>
        public bool Equals(KeyFramePair<T> other)
        {
            return EqualityComparer<T>.Default.Equals(Value, other.Value) &&
                   Position.Equals(other.Position);
        }

        /// <inheritdoc/>
        public override int GetHashCode()
        {
            return HashCode.Combine(Value, Position);
        }
    }
}