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
                left.Scale * right.Scale);
        }
    }
}