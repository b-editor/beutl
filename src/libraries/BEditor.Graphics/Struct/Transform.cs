// Transform.cs
//
// Copyright (C) BEditor
//
// This software may be modified and distributed under the terms
// of the MIT license. See the LICENSE file for details.

using System.Numerics;

namespace BEditor.Graphics
{
    /// <summary>
    /// Represents the transformation matrix.
    /// </summary>
    public struct Transform
    {
        /// <summary>
        /// Represents the zero-filled value of <see cref="Transform"/>.
        /// </summary>
        public static readonly Transform Zero;

        /// <summary>
        /// Represents the default value of <see cref="Transform"/>.
        /// </summary>
        public static readonly Transform Default = new(new(0, 0, 0), new(0, 0, 0), new(0, 0, 0), new(1, 1, 1));

        /// <summary>
        /// Initializes a new instance of the <see cref="Transform"/> struct.
        /// </summary>
        /// <param name="coord">The coordinates.</param>
        /// <param name="center">The center coordinates.</param>
        /// <param name="rotate">The rotation.</param>
        /// <param name="scale">The scale.</param>
        public Transform(Vector3 coord, Vector3 center, Vector3 rotate, Vector3 scale)
        {
            Coordinate = coord;
            Center = center;
            Rotate = rotate;
            Scale = scale;
        }

        /// <summary>
        /// Gets or sets the coordinates.
        /// </summary>
        public Vector3 Coordinate { get; set; }

        /// <summary>
        /// Gets or sets the center coordinates.
        /// </summary>
        public Vector3 Center { get; set; }

        /// <summary>
        /// Gets or sets the rotation.
        /// </summary>
        public Vector3 Rotate { get; set; }

        /// <summary>
        /// Gets or sets the scale.
        /// </summary>
        public Vector3 Scale { get; set; }

        /// <summary>
        /// Gets the matrix.
        /// </summary>
        public Matrix4x4 Matrix
        {
            get
            {
                return Matrix4x4.Identity
                    * Matrix4x4.CreateTranslation(Center)
                        * Matrix4x4.CreateRotationX(Camera.ToRadians(Rotate.X))
                        * Matrix4x4.CreateRotationY(Camera.ToRadians(Rotate.Y))
                        * Matrix4x4.CreateRotationZ(Camera.ToRadians(Rotate.Z))
                            * Matrix4x4.CreateScale(Scale)
                                * Matrix4x4.CreateTranslation(Coordinate);
            }
        }

        /// <summary>
        /// Adds two specified <see cref="Transform"/> instances.
        /// </summary>
        /// <param name="left">The first time interval to add.</param>
        /// <param name="right">The second time interval to add.</param>
        /// <returns>An object whose value is the sum of the values of <paramref name="left"/> and <paramref name="right"/>.</returns>
        public static Transform operator +(Transform left, Transform right)
        {
            return new(
                left.Coordinate + right.Coordinate,
                left.Center + right.Center,
                left.Rotate + right.Rotate,
                left.Scale + right.Scale);
        }

        /// <summary>
        /// Subtracts a specified <see cref="Transform"/> from another specified <see cref="Transform"/>.
        /// </summary>
        /// <param name="left">The minuend.</param>
        /// <param name="right">The subtrahend.</param>
        /// <returns>An object whose value is the result of the value of <paramref name="left"/> minus the value of <paramref name="right"/>.</returns>
        public static Transform operator -(Transform left, Transform right)
        {
            return new(
                left.Coordinate - right.Coordinate,
                left.Center - right.Center,
                left.Rotate - right.Rotate,
                left.Scale - right.Scale);
        }

        /// <summary>
        /// Returns a new <see cref="Transform"/> object whose value is the result of multiplying the specified <see cref="Transform"/> instance and the specified factor.
        /// </summary>
        /// <param name="left">The value to be multiplied.</param>
        /// <param name="right">The value to be multiplied by.</param>
        /// <returns>A new object that represents the value of the specified <see cref="Transform"/> instance multiplied by the value of the specified factor.</returns>
        public static Transform operator *(Transform left, Transform right)
        {
            return new(
                left.Coordinate * right.Coordinate,
                left.Center * right.Center,
                left.Rotate * right.Rotate,
                left.Scale * right.Scale);
        }

        /// <summary>
        /// Returns a new <see cref="Transform"/> value which is the result of division of <paramref name="left"/> instance and the specified <paramref name="right"/>.
        /// </summary>
        /// <param name="left">Divident or the value to be divided.</param>
        /// <param name="right">The value to be divided by.</param>
        /// <returns>A new value that represents result of division of <paramref name="left"/> instance by the value of the <paramref name="right"/>.</returns>
        public static Transform operator /(Transform left, Transform right)
        {
            return new(
                left.Coordinate / right.Coordinate,
                left.Center / right.Center,
                left.Rotate / right.Rotate,
                left.Scale / right.Scale);
        }
    }
}