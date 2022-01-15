using System.Runtime.InteropServices;

namespace BeUtl.Compute.Struct;

[StructLayout(LayoutKind.Sequential)]
public struct Float4
{
    public Float4(float x, float y, float z, float w)
    {
        X = x;
        Y = y;
        Z = z;
        W = w;
    }

    public Float4(Float2 vector, float z, float w)
    {
        X = vector.X;
        Y = vector.Y;
        Z = z;
        W = w;
    }

    public Float4(Float3 vector, float w)
    {
        X = vector.X;
        Y = vector.Y;
        Z = vector.Z;
        W = w;
    }

    public Float4(Float4 vector)
    {
        X = vector.X;
        Y = vector.Y;
        Z = vector.Z;
        W = vector.W;
    }

    public float X { get; set; }

    public float Y { get; set; }

    public float Z { get; set; }

    public float W { get; set; }

    public Float2 XY
    {
        get => new(X, Y);
        set
        {
            X = value.X;
            Y = value.Y;
        }
    }

    public Float3 XYZ
    {
        get => new(X, Y, Z);
        set
        {
            X = value.X;
            Y = value.Y;
            Z = value.Z;
        }
    }

    public Float4 XYZW
    {
        get => this;
        set => this = value;
    }
}
