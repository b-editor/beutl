using System.Runtime.InteropServices;

namespace BEditorNext.Compute.Struct;

[StructLayout(LayoutKind.Sequential)]
public struct Double2
{
    public Double2(double x, double y)
    {
        X = x;
        Y = y;
    }

    public Double2(Double2 vector)
    {
        X = vector.X;
        Y = vector.Y;
    }

    public Double2(Double3 vector)
    {
        X = vector.X;
        Y = vector.Y;
    }

    public Double2(Double4 vector)
    {
        X = vector.X;
        Y = vector.Y;
    }

    public double X { get; set; }

    public double Y { get; set; }

    public Double2 XY
    {
        get => this;
        set => this = value;
    }
}
