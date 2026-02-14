using System.Globalization;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Text.Json.Serialization;
using System.Text.Unicode;
using Beutl.Converters;
using Beutl.Graphics.Backend;
using Beutl.Utilities;

namespace Beutl.Graphics3D.Meshes;

/// <summary>
/// Standard 3D vertex with position, normal, texture coordinates, and tangent.
/// This is a backend-agnostic representation.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
[JsonConverter(typeof(Vertex3DJsonConverter))]
public struct Vertex3D : ISpanParsable<Vertex3D>, IUtf8SpanParsable<Vertex3D>, IUtf8SpanFormattable, ISpanFormattable
{
    public Vector3 Position;
    public Vector3 Normal;

    public Vector2 TexCoord;

    // Tangent vector for normal mapping. xyz = tangent direction, w = handedness (+1 or -1).
    public Vector4 Tangent;

    public Vertex3D(Vector3 position, Vector3 normal, Vector2 texCoord)
    {
        Position = position;
        Normal = normal;
        TexCoord = texCoord;
        Tangent = new Vector4(1, 0, 0, 1); // Default tangent along X-axis
    }

    public Vertex3D(Vector3 position, Vector3 normal, Vector2 texCoord, Vector4 tangent)
    {
        Position = position;
        Normal = normal;
        TexCoord = texCoord;
        Tangent = tangent;
    }

    public static Vertex3D Parse(string s, IFormatProvider? provider = null)
    {
        return TryParse(s.AsSpan(), provider, out Vertex3D result)
            ? result
            : throw new FormatException("Invalid Vertex3D format.");
    }

    public static Vertex3D Parse(ReadOnlySpan<char> s, IFormatProvider? provider = null)
    {
        return TryParse(s, provider, out Vertex3D result)
            ? result
            : throw new FormatException("Invalid Vertex3D format.");
    }

    public static Vertex3D Parse(ReadOnlySpan<byte> s, IFormatProvider? provider = null)
    {
        return TryParse(s, provider, out Vertex3D result)
            ? result
            : throw new FormatException("Invalid Vertex3D format.");
    }

    public static bool TryParse(string? s, IFormatProvider? provider, out Vertex3D result)
    {
        if (s is null)
        {
            result = default;
            return false;
        }

        return TryParse(s.AsSpan(), provider, out result);
    }

    public static bool TryParse(ReadOnlySpan<byte> s, IFormatProvider? provider, out Vertex3D result)
    {
        // Expected format: "px,py,pz; nx,ny,nz; u,v; tx,ty,tz,w"
        var tokenizer = new RefUtf8StringTokenizer(s, ';', "Invalid Vertex3D.");
        if (!tokenizer.TryReadString(out var posStr) ||
            !tokenizer.TryReadString(out var normStr) ||
            !tokenizer.TryReadString(out var texStr))
        {
            result = default;
            return false;
        }

        bool hasTangent = tokenizer.TryReadString(out var tangentStr);
        if (TryParseVector3(posStr, out Vector3 position) &&
            TryParseVector3(normStr, out Vector3 normal) &&
            TryParseVector2(texStr, out Vector2 texCoord))
        {
            if (hasTangent && TryParseVector4(tangentStr, out Vector4 tangent))
            {
                result = new Vertex3D(position, normal, texCoord, tangent);
            }
            else
            {
                result = new Vertex3D(position, normal, texCoord);
            }

            return true;
        }

        result = default;
        return false;

        static bool TryParseVector4(ReadOnlySpan<byte> str, out Vector4 vec4)
        {
            var tokenizer = new RefUtf8StringTokenizer(str, ',', "Invalid Vector4.");
            if (tokenizer.TryReadSingle(out float x) &&
                tokenizer.TryReadSingle(out float y) &&
                tokenizer.TryReadSingle(out float z) &&
                tokenizer.TryReadSingle(out float w))
            {
                vec4 = new Vector4(x, y, z, w);
                return true;
            }

            vec4 = default;
            return false;
        }

        static bool TryParseVector3(ReadOnlySpan<byte> str, out Vector3 vec3)
        {
            var tokenizer = new RefUtf8StringTokenizer(str, ',', "Invalid Vector3.");
            if (tokenizer.TryReadSingle(out float x) &&
                tokenizer.TryReadSingle(out float y) &&
                tokenizer.TryReadSingle(out float z))
            {
                vec3 = new Vector3(x, y, z);
                return true;
            }

            vec3 = default;
            return false;
        }

        static bool TryParseVector2(ReadOnlySpan<byte> str, out Vector2 vec2)
        {
            var tokenizer = new RefUtf8StringTokenizer(str, ',', "Invalid Vector2.");
            if (tokenizer.TryReadSingle(out float x) &&
                tokenizer.TryReadSingle(out float y))
            {
                vec2 = new Vector2(x, y);
                return true;
            }

            vec2 = default;
            return false;
        }
    }

    public static bool TryParse(ReadOnlySpan<char> s, IFormatProvider? provider, out Vertex3D result)
    {
        // Expected format: "px,py,pz; nx,ny,nz; u,v; tx,ty,tz,w"
        var tokenizer = new RefStringTokenizer(s, ';', "Invalid Vertex3D.");
        if (!tokenizer.TryReadString(out var posStr) ||
            !tokenizer.TryReadString(out var normStr) ||
            !tokenizer.TryReadString(out var texStr))
        {
            result = default;
            return false;
        }

        bool hasTangent = tokenizer.TryReadString(out var tangentStr);
        if (TryParseVector3(posStr, out Vector3 position) &&
            TryParseVector3(normStr, out Vector3 normal) &&
            TryParseVector2(texStr, out Vector2 texCoord))
        {
            if (hasTangent && TryParseVector4(tangentStr, out Vector4 tangent))
            {
                result = new Vertex3D(position, normal, texCoord, tangent);
            }
            else
            {
                result = new Vertex3D(position, normal, texCoord);
            }

            return true;
        }

        result = default;
        return false;

        static bool TryParseVector4(ReadOnlySpan<char> str, out Vector4 vec4)
        {
            var tokenizer = new RefStringTokenizer(str, ',', "Invalid Vector4.");
            if (tokenizer.TryReadSingle(out float x) &&
                tokenizer.TryReadSingle(out float y) &&
                tokenizer.TryReadSingle(out float z) &&
                tokenizer.TryReadSingle(out float w))
            {
                vec4 = new Vector4(x, y, z, w);
                return true;
            }

            vec4 = default;
            return false;
        }

        static bool TryParseVector3(ReadOnlySpan<char> str, out Vector3 vec3)
        {
            var tokenizer = new RefStringTokenizer(str, ',', "Invalid Vector3.");
            if (tokenizer.TryReadSingle(out float x) &&
                tokenizer.TryReadSingle(out float y) &&
                tokenizer.TryReadSingle(out float z))
            {
                vec3 = new Vector3(x, y, z);
                return true;
            }

            vec3 = default;
            return false;
        }

        static bool TryParseVector2(ReadOnlySpan<char> str, out Vector2 vec2)
        {
            var tokenizer = new RefStringTokenizer(str, ',', "Invalid Vector2.");
            if (tokenizer.TryReadSingle(out float x) &&
                tokenizer.TryReadSingle(out float y))
            {
                vec2 = new Vector2(x, y);
                return true;
            }

            vec2 = default;
            return false;
        }
    }

    public override string ToString()
    {
        // Expected format: "px,py,pz; nx,ny,nz; u,v; tx,ty,tz,w"
        if (Tangent == new Vector4(1, 0, 0, 1))
        {
            return FormattableString.Invariant(
                $"{Position.X},{Position.Y},{Position.Z}; {Normal.X},{Normal.Y},{Normal.Z}; {TexCoord.X},{TexCoord.Y}");
        }

        return FormattableString.Invariant(
            $"{Position.X},{Position.Y},{Position.Z}; {Normal.X},{Normal.Y},{Normal.Z}; {TexCoord.X},{TexCoord.Y}; {Tangent.X},{Tangent.Y},{Tangent.Z},{Tangent.W}");
    }

    public string ToString(string? format, IFormatProvider? formatProvider)
    {
        return ToString();
    }

    public bool TryFormat(Span<byte> utf8Destination, out int bytesWritten, ReadOnlySpan<char> format,
        IFormatProvider? provider)
    {
        if (Tangent == new Vector4(1, 0, 0, 1))
        {
            return Utf8.TryWrite(
                utf8Destination,
                CultureInfo.InvariantCulture,
                $"{Position.X},{Position.Y},{Position.Z}; {Normal.X},{Normal.Y},{Normal.Z}; {TexCoord.X},{TexCoord.Y}",
                out bytesWritten);
        }

        return Utf8.TryWrite(
            utf8Destination,
            CultureInfo.InvariantCulture,
            $"{Position.X},{Position.Y},{Position.Z}; {Normal.X},{Normal.Y},{Normal.Z}; {TexCoord.X},{TexCoord.Y}; {Tangent.X},{Tangent.Y},{Tangent.Z},{Tangent.W}",
            out bytesWritten);
    }

    public bool TryFormat(Span<char> destination, out int charsWritten, ReadOnlySpan<char> format,
        IFormatProvider? provider)
    {
        if (Tangent == new Vector4(1, 0, 0, 1))
        {
            return destination.TryWrite(
                CultureInfo.InvariantCulture,
                $"{Position.X},{Position.Y},{Position.Z}; {Normal.X},{Normal.Y},{Normal.Z}; {TexCoord.X},{TexCoord.Y}",
                out charsWritten);
        }

        return destination.TryWrite(
            CultureInfo.InvariantCulture,
            $"{Position.X},{Position.Y},{Position.Z}; {Normal.X},{Normal.Y},{Normal.Z}; {TexCoord.X},{TexCoord.Y}; {Tangent.X},{Tangent.Y},{Tangent.Z},{Tangent.W}",
            out charsWritten);
    }

    /// <summary>
    /// Gets the vertex input description for <see cref="Vertex3D"/>.
    /// </summary>
    public static VertexInputDescription GetVertexInputDescription()
    {
        return new VertexInputDescription
        {
            Bindings =
            [
                new VertexBindingDescription
                {
                    Binding = 0, Stride = (uint)Marshal.SizeOf<Vertex3D>(), InputRate = VertexInputRate.Vertex
                }
            ],
            Attributes =
            [
                new VertexAttributeDescription { Binding = 0, Location = 0, Format = VertexFormat.Float3, Offset = 0 },
                new VertexAttributeDescription
                {
                    Binding = 0,
                    Location = 1,
                    Format = VertexFormat.Float3,
                    Offset = (uint)Marshal.OffsetOf<Vertex3D>(nameof(Normal))
                },
                new VertexAttributeDescription
                {
                    Binding = 0,
                    Location = 2,
                    Format = VertexFormat.Float2,
                    Offset = (uint)Marshal.OffsetOf<Vertex3D>(nameof(TexCoord))
                },
                new VertexAttributeDescription
                {
                    Binding = 0,
                    Location = 3,
                    Format = VertexFormat.Float4,
                    Offset = (uint)Marshal.OffsetOf<Vertex3D>(nameof(Tangent))
                }
            ]
        };
    }
}
