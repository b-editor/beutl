// Tool.cs
//
// Copyright (C) BEditor
//
// This software may be modified and distributed under the terms
// of the MIT license. See the LICENSE file for details.

using BEditor.Graphics.OpenGL.Resources;

using OpenTK.Graphics.OpenGL4;
using OpenTK.Mathematics;
using OpenTK.Windowing.GraphicsLibraryFramework;

using Color = BEditor.Drawing.Color;
using GLColor = OpenTK.Mathematics.Color4;
using Matrix4x4 = System.Numerics.Matrix4x4;

namespace BEditor.Graphics.OpenGL
{
    internal static class Tool
    {
        public static Vector3 ToOpenTK(this in System.Numerics.Vector3 vector3)
        {
            return new Vector3(vector3.X, vector3.Y, vector3.Z);
        }

        public static Vector4 ToOpenTK(this in System.Numerics.Vector4 vector4)
        {
            return new Vector4(vector4.X, vector4.Y, vector4.Z, vector4.W);
        }

        public static Matrix4 ToOpenTK(this in Matrix4x4 mat)
        {
            return new Matrix4(
                mat.M11, mat.M12, mat.M13, mat.M14,
                mat.M21, mat.M22, mat.M23, mat.M24,
                mat.M31, mat.M32, mat.M33, mat.M34,
                mat.M41, mat.M42, mat.M43, mat.M44);
        }

        public static Vector2 ToOpenTK(this in System.Numerics.Vector2 vector3)
        {
            return new Vector2(vector3.X, vector3.Y);
        }

        public static System.Numerics.Vector3 ToVector3(this in Color color)
        {
            return new(
                color.R / 255f,
                color.G / 255f,
                color.B / 255f);
        }

        public static System.Numerics.Vector4 ToVector4(this in Color color)
        {
            return new(
                color.R / 255f,
                color.G / 255f,
                color.B / 255f,
                color.A / 255f);
        }

        public static System.Numerics.Vector3 ToNumerics(this in Vector3 vector3)
        {
            return new(vector3.X, vector3.Y, vector3.Z);
        }

        public static Matrix4x4 ToNumerics(this in Matrix4 mat)
        {
            return new(
                mat.M11, mat.M12, mat.M13, mat.M14,
                mat.M21, mat.M22, mat.M23, mat.M24,
                mat.M31, mat.M32, mat.M33, mat.M34,
                mat.M41, mat.M42, mat.M43, mat.M44);
        }

        public static System.Numerics.Vector2 ToNumerics(this in Vector2 vector3)
        {
            return new(vector3.X, vector3.Y);
        }

        public static GLColor ToOpenTK(this in Color color)
        {
            return new(color.R, color.G, color.B, color.A);
        }

        public static void ThrowGLFWError()
        {
            var result = GLFW.GetError(out var description);

            if (result is not OpenTK.Windowing.GraphicsLibraryFramework.ErrorCode.NoError)
            {
                throw new GraphicsException(description);
            }
        }

        public static void ThrowGLError()
        {
            var result = GL.GetError();

            if (result is not OpenTK.Graphics.OpenGL4.ErrorCode.NoError)
            {
                var description = result switch
                {
                    OpenTK.Graphics.OpenGL4.ErrorCode.NoError => string.Empty,
                    OpenTK.Graphics.OpenGL4.ErrorCode.InvalidEnum => Strings.InvalidEnum,
                    OpenTK.Graphics.OpenGL4.ErrorCode.InvalidValue => Strings.InvalidValue,
                    OpenTK.Graphics.OpenGL4.ErrorCode.InvalidOperation => Strings.InvalidOperation,
                    OpenTK.Graphics.OpenGL4.ErrorCode.OutOfMemory => Strings.OutOfMemory,
                    OpenTK.Graphics.OpenGL4.ErrorCode.InvalidFramebufferOperation => Strings.InvalildFramebufferOperation,
                    OpenTK.Graphics.OpenGL4.ErrorCode.ContextLost => result.ToString("g"),
                    OpenTK.Graphics.OpenGL4.ErrorCode.TableTooLarge => Strings.TableTooLarge,
                    OpenTK.Graphics.OpenGL4.ErrorCode.TextureTooLargeExt => result.ToString("g"),
                    _ => string.Empty,
                };

                throw new GraphicsException(description);
            }
        }

        public static BallImpl ToImpl(this Ball ball)
        {
            return new(ball.RadiusX, ball.RadiusY, ball.RadiusZ);
        }

        public static CubeImpl ToImpl(this Cube cube)
        {
            return new(cube.Width, cube.Height, cube.Depth);
        }

        public static LineImpl ToImpl(this Line line)
        {
            return new(line.Start, line.End, line.Width);
        }

        public static TextureImpl ToImpl(this Texture texture)
        {
            using var img = texture.ToImage();
            return TextureImpl.FromImage(img, texture.Vertices);
        }
    }
}