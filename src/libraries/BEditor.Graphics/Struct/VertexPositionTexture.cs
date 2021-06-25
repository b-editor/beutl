// VertexPositionTexture.cs
//
// Copyright (C) BEditor
//
// This software may be modified and distributed under the terms
// of the MIT license. See the LICENSE file for details.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;

namespace BEditor.Graphics
{
    /// <summary>
    /// Represents the vertex position of the texture.
    /// </summary>
    public struct VertexPositionTexture
    {
        /// <summary>
        /// The x coordinate position.
        /// </summary>
        public float PosX;

        /// <summary>
        /// The y coordinate position.
        /// </summary>
        public float PosY;

        /// <summary>
        /// The z coordinate position.
        /// </summary>
        public float PosZ;

        /// <summary>
        /// The x coordinate position of the texture.
        /// </summary>
        public float TexU;

        /// <summary>
        /// The y coordinate position of the texture.
        /// </summary>
        public float TexV;

        /// <summary>
        /// Initializes a new instance of the <see cref="VertexPositionTexture"/> struct.
        /// </summary>
        /// <param name="pos">The coordinate.</param>
        /// <param name="uv">The uv coordinate.</param>
        public VertexPositionTexture(Vector3 pos, Vector2 uv)
        {
            PosX = pos.X;
            PosY = pos.Y;
            PosZ = pos.Z;
            TexU = uv.X;
            TexV = uv.Y;
        }

        /// <summary>
        /// Enumerates the fields of this structure.
        /// </summary>
        /// <returns>Returns the fields.</returns>
        public IEnumerable<float> Enumerate()
        {
            yield return PosX;
            yield return PosY;
            yield return PosZ;
            yield return TexU;
            yield return TexV;
        }
    }
}