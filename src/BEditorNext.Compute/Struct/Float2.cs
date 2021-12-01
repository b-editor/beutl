using System.Runtime.InteropServices;

namespace BEditorNext.Compute.Struct;

[StructLayout(LayoutKind.Sequential)]
public struct Float2
{
    public Float2(float x, float y)
    {
        X = x;
        Y = y;
    }

    public Float2(Float2 vector)
    {
        X = vector.X;
        Y = vector.Y;
    }

    public Float2(Float3 vector)
    {
        X = vector.X;
        Y = vector.Y;
    }

    public Float2(Float4 vector)
    {
        X = vector.X;
        Y = vector.Y;
    }

    public float X { get; set; }

    public float Y { get; set; }

    public Float2 XY
    {
        get => this;
        set => this = value;
    }
}