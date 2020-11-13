using System;
using System.Collections.Generic;
using System.Diagnostics.Contracts;
using System.Runtime.InteropServices;
using System.Runtime.Serialization;
using System.Text;

namespace BEditor.Core.Media
{
#nullable enable
    /// <summary>
    /// 
    /// </summary>
    [Serializable]
    [StructLayout(LayoutKind.Sequential)]
    public struct Range : IEquatable<Range>
    {
        /// <summary>
        /// 
        /// </summary>
        public int Start { get; set; }

        /// <summary>
        /// 
        /// </summary>
        public int End { get; set; }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="start"></param>
        /// <param name="end"></param>
        public Range(int start, int end)
        {
            Start = start;
            End = end;
        }

        /// <summary>
        /// 
        /// </summary>
        public static Range All => new Range(int.MinValue, int.MaxValue);

        /// <inheritdoc />
        [Pure]
        public bool Equals(Range other)
        {
            return Start == other.Start && End == other.End;
        }

        /// <inheritdoc />
        [Pure]
        public override bool Equals(object? obj)
        {
            return obj is Range other && Equals(other);
        }

        /// <inheritdoc />
        [Pure]
        public override int GetHashCode()
        {
            unchecked
            {
                return (Start * 397) ^ End;
            }
        }

        /// <inheritdoc/>
        [Pure]
        public override string ToString() => $"(Start:{Start} End:{End})";


        /// <summary>
        /// 
        /// </summary>
        /// <param name="left"></param>
        /// <param name="right"></param>
        /// <returns></returns>
        [Pure]
        public static bool operator ==(Range left, Range right)
        {
            return left.Equals(right);
        }
        /// <summary>
        /// 
        /// </summary>
        /// <param name="left"></param>
        /// <param name="right"></param>
        /// <returns></returns>
        [Pure]
        public static bool operator !=(Range left, Range right)
        {
            return !(left == right);
        }
    }
}
