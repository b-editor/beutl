using System.Numerics;
using System.Runtime.InteropServices;
using Beutl.Graphics;
using Beutl.Graphics.Shaders;
using Beutl.Language;

namespace Beutl.NodeGraph.Nodes.Shaders;

/// <summary>Chooses the port for a declared shader value and writes it where the declaration says.</summary>
internal static class ShaderParameterPacking
{
    public static Type PortType(ShaderValueLayout member)
    {
        if (member.ArrayLength != 0 || member.Members.Length > 0)
            return typeof(byte[]);
        bool isFloat = member.ScalarKind == ShaderScalarKind.FloatingPoint && member.BitWidth == 32;
        if (member.Columns != 1)
            return isFloat && member.Columns == member.Rows && member.Columns is 3 or 4 ? typeof(Matrix) : typeof(byte[]);
        if (isFloat)
            return member.Rows switch { 1 => typeof(float), 2 => typeof(Vector2), 3 => typeof(Vector3), 4 => typeof(Vector4), _ => typeof(byte[]) };
        return member.Rows != 1 ? typeof(byte[]) : ScalarType(member.ScalarKind, member.BitWidth) ?? typeof(byte[]);
    }

    public static Type? ScalarType(ShaderScalarKind kind, int bitWidth) => (kind, bitWidth) switch
    {
        (ShaderScalarKind.Boolean, _) => typeof(bool),
        (ShaderScalarKind.SignedInteger, 32) => typeof(int),
        (ShaderScalarKind.UnsignedInteger, 32) => typeof(uint),
        (ShaderScalarKind.SignedInteger, 64) => typeof(long),
        (ShaderScalarKind.UnsignedInteger, 64) => typeof(ulong),
        (ShaderScalarKind.FloatingPoint, 32) => typeof(float),
        (ShaderScalarKind.FloatingPoint, 64) => typeof(double),
        _ => null
    };

    public static object? DefaultValue(ShaderValueLayout member)
        => PortType(member) == typeof(Matrix) ? Matrix.Identity : null;

    public static byte[] Pack(IEnumerable<ShaderValueLayout> members, int size, Func<ShaderValueLayout, object?> value)
    {
        byte[] result = new byte[size];
        foreach (ShaderValueLayout member in members)
        {
            Span<byte> destination = result.AsSpan(member.Offset, member.Size);
            switch (value(member))
            {
                case null: break;
                case byte[] bytes:
                    if (bytes.Length != member.Size)
                        throw new ArgumentException(string.Format(NodeGraphStrings.ShaderError_DataSize, member.Name, member.Size, bytes.Length));
                    bytes.CopyTo(destination);
                    break;
                case bool b: int boolean = b ? 1 : 0; MemoryMarshal.Write(destination, in boolean); break;
                case int v: MemoryMarshal.Write(destination, in v); break;
                case uint v: MemoryMarshal.Write(destination, in v); break;
                case long v: MemoryMarshal.Write(destination, in v); break;
                case ulong v: MemoryMarshal.Write(destination, in v); break;
                case float v: MemoryMarshal.Write(destination, in v); break;
                case double v: MemoryMarshal.Write(destination, in v); break;
                case Vector2 v: MemoryMarshal.Write(destination, in v); break;
                case Vector3 v: MemoryMarshal.Write(destination, in v); break;
                case Vector4 v: MemoryMarshal.Write(destination, in v); break;
                case Matrix v: WriteMatrix(destination, member, v); break;
                default: throw new ArgumentException(string.Format(NodeGraphStrings.ShaderError_ValueType, member.Name));
            }
        }
        return result;
    }

    // A Matrix maps row vectors and GLSL maps column vectors, so its rows are the shader's columns.
    private static void WriteMatrix(Span<byte> destination, ShaderValueLayout member, Matrix matrix)
    {
        float[,] columns = member.Columns == 3
            ? new[,]
            {
                { matrix.M11, matrix.M12, matrix.M13 },
                { matrix.M21, matrix.M22, matrix.M23 },
                { matrix.M31, matrix.M32, matrix.M33 },
            }
            : new[,]
            {
                { matrix.M11, matrix.M12, 0, matrix.M13 },
                { matrix.M21, matrix.M22, 0, matrix.M23 },
                { 0, 0, 1, 0 },
                { matrix.M31, matrix.M32, 0, matrix.M33 },
            };
        for (int column = 0; column < member.Columns; column++)
        {
            for (int row = 0; row < member.Rows; row++)
            {
                (int outer, int inner) = member.RowMajor ? (row, column) : (column, row);
                MemoryMarshal.Write(destination[(outer * member.MatrixStride + inner * sizeof(float))..], in columns[column, row]);
            }
        }
    }

    /// <summary>The reflected value of a specialization constant that nothing has specialized.</summary>
    public static object? DefaultValue(ShaderSpecializationLayout constant) => (constant.ScalarKind, constant.BitWidth) switch
    {
        (ShaderScalarKind.Boolean, _) => constant.DefaultBits != 0,
        (ShaderScalarKind.SignedInteger, 32) => unchecked((int)constant.DefaultBits),
        (ShaderScalarKind.UnsignedInteger, 32) => unchecked((uint)constant.DefaultBits),
        (ShaderScalarKind.SignedInteger, 64) => unchecked((long)constant.DefaultBits),
        (ShaderScalarKind.UnsignedInteger, 64) => constant.DefaultBits,
        (ShaderScalarKind.FloatingPoint, 32) => BitConverter.UInt32BitsToSingle(unchecked((uint)constant.DefaultBits)),
        (ShaderScalarKind.FloatingPoint, 64) => BitConverter.UInt64BitsToDouble(constant.DefaultBits),
        _ => null
    };
}
