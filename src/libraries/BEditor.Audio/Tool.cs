using System;

using BEditor.Audio.Resources;

using OpenTK.Audio.OpenAL;
using OpenTK.Mathematics;

using Color = BEditor.Drawing.Color;
using Matrix4x4 = System.Numerics.Matrix4x4;

namespace BEditor.Audio
{
    static class Tool
    {
        internal static Vector3 ToOpenTK(this in System.Numerics.Vector3 vector3)
        {
            return new Vector3(vector3.X, vector3.Y, vector3.Z);
        }

        internal static Vector4 ToOpenTK(this in System.Numerics.Vector4 vector4)
        {
            return new Vector4(vector4.X, vector4.Y, vector4.Z, vector4.W);
        }

        internal static Matrix4 ToOpenTK(this in Matrix4x4 mat)
        {
            return new Matrix4(
                mat.M11, mat.M12, mat.M13, mat.M14,
                mat.M21, mat.M22, mat.M23, mat.M24,
                mat.M31, mat.M32, mat.M33, mat.M34,
                mat.M41, mat.M42, mat.M43, mat.M44);
        }

        internal static Vector2 ToOpenTK(this in System.Numerics.Vector2 vector3)
        {
            return new Vector2(vector3.X, vector3.Y);
        }

        internal static System.Numerics.Vector3 ToVector3(this in Color color)
        {
            return new(
                color.R / 255f,
                color.G / 255f,
                color.B / 255f);
        }

        internal static System.Numerics.Vector4 ToVector4(this in Color color)
        {
            return new(
                color.R / 255f,
                color.G / 255f,
                color.B / 255f,
                color.A / 255f);
        }

        internal static System.Numerics.Vector3 ToNumerics(this in Vector3 vector3)
        {
            return new(vector3.X, vector3.Y, vector3.Z);
        }

        internal static Matrix4x4 ToNumerics(this in Matrix4 mat)
        {
            return new(
                mat.M11, mat.M12, mat.M13, mat.M14,
                mat.M21, mat.M22, mat.M23, mat.M24,
                mat.M31, mat.M32, mat.M33, mat.M34,
                mat.M41, mat.M42, mat.M43, mat.M44);
        }

        internal static System.Numerics.Vector2 ToNumerics(this in Vector2 vector3)
        {
            return new(vector3.X, vector3.Y);
        }

        public static int GenBuffer()
        {
            var buf = AL.GenBuffer();
            var error = AL.GetError();
            if (error is ALError.InvalidValue)
            {
                throw new AudioException(Strings.GenBufferInvalidValue);
            }
            else if (error is ALError.OutOfMemory)
            {
                throw new AudioException(Strings.GenBufferOutOfMemory);
            }

            return buf;
        }
        public static int GenSource()
        {
            var src = AL.GenSource();
            var error = AL.GetError();

            if (error is ALError.OutOfMemory)
            {
                throw new AudioException(Strings.GenSourceOutOfMemory);
            }
            else if (error is ALError.InvalidValue)
            {
                throw new AudioException(Strings.GenSourceInvalidValue);
            }
            else if (error is ALError.InvalidOperation)
            {
                throw new AudioException(Strings.GenSourceInvalidOperation);
            }

            return src;
        }
        public static void DeleteBuffer(int handle)
        {
            AL.DeleteBuffer(handle);
        }
        public static void DeleteSource(int handle)
        {
            AL.DeleteSource(handle);

            var error = AL.GetError();
            if (error is ALError.InvalidName)
            {
                throw new AudioException(Strings.DeleteSourceInvalidName);
            }
            else if (error is ALError.InvalidOperation)
            {
                throw new AudioException(Strings.DeleteSourceInvalidOperation);
            }
        }
        public static void BufferData<TBuffer>(int bid, ALFormat format, Span<TBuffer> buffer, int freq) where TBuffer : unmanaged
        {
            AL.BufferData(bid, format, buffer, freq);

            var error = AL.GetError();
            if (error is ALError.OutOfMemory)
            {
                throw new AudioException(Strings.BufferDataOutOfMemory);
            }
            else if (error is ALError.InvalidValue)
            {
                throw new AudioException(Strings.BufferDataInvalidValue);
            }
            else if (error is ALError.InvalidEnum)
            {
                throw new AudioException(Strings.BufferDataInvalidEnum);
            }
        }
        public static void GetBuffer(int bid, ALGetBufferi param, out int value)
        {
            AL.GetBuffer(bid, param, out value);

            var error = AL.GetError();
            if (error is ALError.InvalidEnum)
            {
                throw new AudioException(Strings.GetBufferInvalidEnum);
            }
            else if (error is ALError.InvalidName)
            {
                throw new AudioException(Strings.GetBufferInvalidName);
            }
            else if (error is ALError.InvalidValue)
            {
                throw new AudioException(Strings.GetBufferInvalidValue);
            }
        }
        public static void SourcePlay(int handle)
        {
            AL.SourcePlay(handle);
            var error = AL.GetError();

            if (error is ALError.InvalidName)
            {
                throw new AudioException(Strings.SourcePlayInvalidName);
            }
            else if (error is ALError.InvalidOperation)
            {
                throw new AudioException(Strings.SourcePlayInvalidOperation);
            }
        }
        public static void SourceStop(int handle)
        {
            AL.SourceStop(handle);
            var error = AL.GetError();

            if (error is ALError.InvalidName)
            {
                throw new AudioException(Strings.SourceStopInvalidName);
            }
            else if (error is ALError.InvalidOperation)
            {
                throw new AudioException(Strings.SourceStopInvalidOperation);
            }
        }
        public static void SourcePause(int handle)
        {
            AL.SourcePause(handle);
            var error = AL.GetError();

            if (error is ALError.InvalidName)
            {
                throw new AudioException(Strings.SourcePauseInvalidName);
            }
            else if (error is ALError.InvalidOperation)
            {
                throw new AudioException(Strings.SourcePauseInvalidOperation);
            }
        }
    }
}