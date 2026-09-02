using System.Numerics;
using SkiaSharp;

namespace Beutl.Graphics.Effects;

internal readonly record struct ShaderCanonicalValue(
    float[]? Values,
    int[]? Integers,
    bool IsInteger)
{
    public static ShaderCanonicalValue Create<T>(T value)
        where T : unmanaged
    {
        object boxed = value;
        return boxed switch
        {
            float current => Float([current]),
            double current => Float([(float)current]),
            int current => Integer([current]),
            uint current when current <= int.MaxValue => Integer([(int)current]),
            uint current => throw new ArgumentOutOfRangeException(
                nameof(value),
                current,
                "A UInt32 shader uniform value cannot exceed Int32.MaxValue."),
            short current => Integer([current]),
            ushort current => Integer([current]),
            byte current => Integer([current]),
            sbyte current => Integer([current]),
            bool current => Integer([current ? 1 : 0]),
            Vector2 current => Float([current.X, current.Y]),
            Vector3 current => Float([current.X, current.Y, current.Z]),
            Vector4 current => Float([current.X, current.Y, current.Z, current.W]),
            // SkSL reads matrix uniforms column-major. System.Numerics stores rows contiguously and transforms
            // row vectors, so its storage order already is the column-major encoding of the equivalent
            // column-vector SkSL matrix. Matrix3x2 has no SkSL matrix type; it binds to float2[3].
            Matrix3x2 current => Float([
                current.M11, current.M12,
                current.M21, current.M22,
                current.M31, current.M32]),
            Matrix4x4 current => Float([
                current.M11, current.M12, current.M13, current.M14,
                current.M21, current.M22, current.M23, current.M24,
                current.M31, current.M32, current.M33, current.M34,
                current.M41, current.M42, current.M43, current.M44]),
            SKPoint current => Float([current.X, current.Y]),
            SKPoint3 current => Float([current.X, current.Y, current.Z]),
            SKSize current => Float([current.Width, current.Height]),
            // SKMatrix also stores rows contiguously but transforms column vectors, so unlike the cases above its
            // storage order must be transposed to become column-major.
            SKMatrix current => Float([
                current.ScaleX, current.SkewY, current.Persp0,
                current.SkewX, current.ScaleY, current.Persp1,
                current.TransX, current.TransY, current.Persp2]),
            _ => throw new ArgumentException(
                $"'{typeof(T).FullName}' is not a canonical shader uniform value type.",
                nameof(value)),
        };
    }

    public void ThrowIfIncompatible(SkslUniformDeclaration declaration)
    {
        if (declaration.IsShader)
            throw new InvalidOperationException("A shader resource declaration requires a resource binding.");
        int required = GetComponentCount(declaration);
        int actual = IsInteger ? Integers!.Length : Values!.Length;
        bool declaredInteger = declaration.Type is "int" or "int2" or "int3" or "int4" or "bool";
        if (declaredInteger != IsInteger || required != actual)
        {
            throw new InvalidOperationException(
                $"The supplied value is incompatible with SkSL uniform type '{declaration.Type}'.");
        }
    }

    public static void ThrowIfFloatSequenceIncompatible(float[] values, SkslUniformDeclaration declaration)
    {
        if (declaration.IsShader || declaration.Type.StartsWith("int", StringComparison.Ordinal) || declaration.Type == "bool")
            throw new InvalidOperationException($"SkSL uniform type '{declaration.Type}' does not accept float values.");
        int required = GetComponentCount(declaration);
        if (values.Length != required)
            throw new InvalidOperationException($"SkSL uniform type '{declaration.Type}' requires {required} values.");
    }

    private static int GetComponentCount(SkslUniformDeclaration declaration)
    {
        int count = declaration.Type switch
        {
            "float" or "half" or "int" or "bool" => 1,
            "float2" or "half2" or "int2" => 2,
            "float3" or "half3" or "int3" => 3,
            "float4" or "half4" or "int4" => 4,
            "float2x2" or "half2x2" or "mat2" => 4,
            "float3x3" or "half3x3" or "mat3" => 9,
            "float4x4" or "half4x4" or "mat4" => 16,
            _ => throw new InvalidOperationException($"Unsupported SkSL uniform type '{declaration.Type}'."),
        };
        return count * (declaration.ArrayExtent ?? 1);
    }

    private static ShaderCanonicalValue Float(float[] values) => new(values, null, false);

    private static ShaderCanonicalValue Integer(int[] values) => new(null, values, true);
}
