using System.Runtime.InteropServices;

namespace BEditorNext.Compute.Struct;

[StructLayout(LayoutKind.Sequential)]
public struct Float3
{
    public Float3(float x, float y, float z)
    {
        X = x;
        Y = y;
        Z = z;
    }

    public Float3(Float2 vector, float z)
    {
        X = vector.X;
        Y = vector.Y;
        Z = z;
    }

    public Float3(Float3 vector)
    {
        X = vector.X;
        Y = vector.Y;
        Z = vector.Z;
    }

    public Float3(Float4 vector)
    {
        X = vector.X;
        Y = vector.Y;
        Z = vector.Z;
    }

    public float X { get; set; }

    public float Y { get; set; }

    public float Z { get; set; }

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
        get => this;
        set => this = value;
    }
}
