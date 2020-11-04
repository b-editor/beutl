using System;
using System.Collections.Generic;
using System.Diagnostics.Contracts;
using System.Runtime.InteropServices;
using System.Text;
using System.Transactions;

using BEditor.Core.Media;
using BEditor.Mathematics;

namespace BEditor.Graphics {
    public unsafe static partial class GL {
        #region C

        public static void Color3(Color color) {
            Color3(color.ScR, color.ScG, color.ScB);
        }
        public static void Color3(sbyte[] v) {
            if (v is null) throw new ArgumentNullException(nameof(v));

            fixed (sbyte* ptr = v) {
                Color3(ptr);
            }
        }
        public static void Color3(short[] v) {
            if (v is null) throw new ArgumentNullException(nameof(v));

            fixed (short* ptr = v) {
                Color3(ptr);
            }
        }
        public static void Color3(int[] v) {
            if (v is null) throw new ArgumentNullException(nameof(v));

            fixed (int* ptr = v) {
                Color3(ptr);
            }
        }
        public static void Color3(float[] v) {
            if (v is null) throw new ArgumentNullException(nameof(v));

            fixed (float* ptr = v) {
                Color3(ptr);
            }
        }
        public static void Color3(double[] v) {
            if (v is null) throw new ArgumentNullException(nameof(v));

            fixed (double* ptr = v) {
                Color3(ptr);
            }
        }
        public static void Color3(byte[] v) {
            if (v is null) throw new ArgumentNullException(nameof(v));

            fixed (byte* ptr = v) {
                Color3(ptr);
            }
        }
        public static void Color3(ushort[] v) {
            if (v is null) throw new ArgumentNullException(nameof(v));

            fixed (ushort* ptr = v) {
                Color3(ptr);
            }
        }
        public static void Color3(uint[] v) {
            if (v is null) throw new ArgumentNullException(nameof(v));

            fixed (uint* ptr = v) {
                Color3(ptr);
            }
        }

        public static void Color4(Color color) {
            Color4(color.ScR, color.ScG, color.ScB, color.ScA);
        }
        public static void Color4(sbyte[] v) {
            if (v is null) throw new ArgumentNullException(nameof(v));

            fixed (sbyte* ptr = v) {
                Color4(ptr);
            }
        }
        public static void Color4(short[] v) {
            if (v is null) throw new ArgumentNullException(nameof(v));

            fixed (short* ptr = v) {
                Color4(ptr);
            }
        }
        public static void Color4(int[] v) {
            if (v is null) throw new ArgumentNullException(nameof(v));

            fixed (int* ptr = v) {
                Color4(ptr);
            }
        }
        public static void Color4(float[] v) {
            if (v is null) throw new ArgumentNullException(nameof(v));

            fixed (float* ptr = v) {
                Color4(ptr);
            }
        }
        public static void Color4(double[] v) {
            if (v is null) throw new ArgumentNullException(nameof(v));

            fixed (double* ptr = v) {
                Color4(ptr);
            }
        }
        public static void Color4(byte[] v) {
            if (v is null) throw new ArgumentNullException(nameof(v));

            fixed (byte* ptr = v) {
                Color4(ptr);
            }
        }
        public static void Color4(ushort[] v) {
            if (v is null) throw new ArgumentNullException(nameof(v));

            fixed (ushort* ptr = v) {
                Color4(ptr);
            }
        }
        public static void Color4(uint[] v) {
            if (v is null) throw new ArgumentNullException(nameof(v));

            fixed (uint* ptr = v) {
                Color4(ptr);
            }
        }

        #endregion

        #region D

        public static void DeleteTexture(int texture) {
            fixed (int* ptr = new int[1] { texture }) {
                DeleteTextures(1, ptr);
            }
        }
        public static void DeleteTexture(uint texture) {
            fixed (uint* ptr = new uint[1] { texture }) {
                DeleteTextures(1, ptr);
            }
        }

        public static void DeleteTextures(int n, int[] textures) {
            fixed (int* ptr = textures) {
                DeleteTextures(n, ptr);
            }
        }
        public static void DeleteTextures(int n, uint[] textures) {
            fixed (uint* ptr = textures) {
                DeleteTextures(n, ptr);
            }
        }

        #endregion

        #region G

        public static void GenTextures(int n, [Out] int[] textures) {
            fixed (int* texs = textures) {
                GenTextures(n, texs);
            }
        }
        public static void GenTextures(int n, out int textures) {
            fixed (int* texs = &textures) {
                GenTextures(n, texs);
            }
        }
        public static void GenTextures(int n, [Out] uint[] textures) {
            fixed (uint* txs = textures) {
                GenTextures(n, txs);
            }
        }
        public static void GenTextures(int n, out uint textures) {
            fixed (uint* texs = &textures) {
                GenTextures(n, texs);
            }
        }

        #endregion

        #region L

        public static void LoadMatrix(float[] m) {
            if (m is null) throw new ArgumentNullException(nameof(m));

            fixed (float* ptr = m) {
                LoadMatrix(ptr);
            }
        }
        public static void LoadMatrix(double[] m) {
            if (m is null) throw new ArgumentNullException(nameof(m));

            fixed (double* ptr = m) {
                LoadMatrix(ptr);
            }
        }
        public static void LoadMatrix(ref Matrix4 m) {
            fixed (float* ptr = &m.Row0.X) {
                LoadMatrix(ptr);
            }
        }


        #endregion

        #region M

        public static void Material(MaterialFace face, MaterialParameter pname, float[] @params) {
            if (@params is null) throw new ArgumentNullException(nameof(@params));

            fixed (float* ptr = @params) {
                Material(face, pname, ptr);
            }
        }
        public static void Material(MaterialFace face, MaterialParameter pname, int[] @params) {
            if (@params is null) throw new ArgumentNullException(nameof(@params));

            fixed (int* ptr = @params) {
                Material(face, pname, ptr);
            }
        }
        public static void Material(MaterialFace face, MaterialParameter pname, Vector4 @params) {
            Material(face, pname, new float[4] { @params.X, @params.Y, @params.Z, @params.W });
        }
        public static void Material(MaterialFace face, MaterialParameter pname, Color @params) {
            Material(face, pname, new float[4] { @params.ScR, @params.ScG, @params.ScB, @params.ScA });
        }

        #endregion

        #region N

        public static void Normal3(Vector3 normal) {
            Normal3(normal.X, normal.Y, normal.Z);
        }

        public static void Normal3(byte[] v) {
            if (v is null) throw new ArgumentNullException(nameof(v));

            fixed(byte* ptr = v) {
                Normal3(ptr);
            }
        }
        public static void Normal3(sbyte[] v) {
            if (v is null) throw new ArgumentNullException(nameof(v));

            fixed(sbyte* ptr = v) {
                Normal3(ptr);
            }
        }
        public static void Normal3(double[] v) {
            if (v is null) throw new ArgumentNullException(nameof(v));

            fixed(double* ptr = v) {
                Normal3(ptr);
            }
        }
        public static void Normal3(float[] v) {
            if (v is null) throw new ArgumentNullException(nameof(v));

            fixed(float* ptr = v) {
                Normal3(ptr);
            }
        }
        public static void Normal3(int[] v) {
            if (v is null) throw new ArgumentNullException(nameof(v));

            fixed(int* ptr = v) {
                Normal3(ptr);
            }
        }
        public static void Normal3(short[] v) {
            if (v is null) throw new ArgumentNullException(nameof(v));

            fixed(short* ptr = v) {
                Normal3(ptr);
            }
        }

        #endregion

        #region R

        public static void Rotate(double angle, Vector3 axis) {
            Rotate(angle, axis.X, axis.Y, axis.Z);
        }
        public static void Rotate(float angle, Vector3 axis) {
            Rotate(angle, axis.X, axis.Y, axis.Z);
        }

        #endregion

        #region S

        public static void Scale(Vector3 scale) {
            Scale(scale.X, scale.Y, scale.Z);
        }

        #endregion

        #region T

        public static void TexParameter(TextureTarget target, TextureParameterName pname, float[] @params) {
            if (@params is null) throw new ArgumentNullException(nameof(@params));

            fixed (float* ptr = @params) {
                TexParameter(target, pname, ptr);
            }
        }
        public static void TexParameter(TextureTarget target, TextureParameterName pname, int[] @params) {
            if (@params is null) throw new ArgumentNullException(nameof(@params));

            fixed (int* ptr = @params) {
                TexParameter(target, pname, ptr);
            }
        }

        public static void Translate(Vector3 trans) {
            Translate(trans.X, trans.Y, trans.Z);
        }

        public static void TexCoord2(Vector2 coord) {
            TexCoord2(coord.X, coord.Y);
        }
        public static void TexCoord3(Vector3 coord) {
            TexCoord3(coord.X, coord.Y, coord.Z);
        }
        public static void TexCoord4(Vector4 coord) {
            TexCoord4(coord.X, coord.Y, coord.Z, coord.Z);
        }

        #endregion

        #region V

        public static void Vertex2(Vector2 v) {
            Vertex2(v.X, v.Y);
        }
        public static void Vertex3(Vector3 v) {
            Vertex3(v.X, v.Y, v.Z);
        }
        public static void Vertex4(Vector4 v) {
            Vertex4(v.X, v.Y, v.Z, v.W);
        }

        #endregion
    }
}
