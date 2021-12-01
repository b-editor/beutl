using System.Runtime.InteropServices;

namespace BEditorNext.Compute.Struct;

[StructLayout(LayoutKind.Sequential)]
public struct Double3
{
    public Double3(double x, double y, double z)
    {
        X = x;
        Y = y;
        Z = z;
    }

    public Double3(Double2 vector, double z)
    {
        X = vector.X;
        Y = vector.Y;
        Z = z;
    }

    public Double3(Double3 vector)
    {
        X = vector.X;
        Y = vector.Y;
        Z = vector.Z;
    }

    public Double3(Double4 vector)
    {
        X = vector.X;
        Y = vector.Y;
        Z = vector.Z;
    }

    public double X { get; set; }

    public double Y { get; set; }

    public double Z { get; set; }

    public Double2 XY
    {
        get => new(X, Y);
        set
        {
            X = value.X;
            Y = value.Y;
        }
    }

    public Double3 XYZ
    {
        get => this;
        set => this = value;
    }
}
