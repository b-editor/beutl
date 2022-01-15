using System.Runtime.InteropServices;

namespace BeUtl.Compute.Struct;

[StructLayout(LayoutKind.Sequential)]
public struct Double4
{
    public Double4(double x, double y, double z, double w)
    {
        X = x;
        Y = y;
        Z = z;
        W = w;
    }

    public Double4(Double2 vector, double z, double w)
    {
        X = vector.X;
        Y = vector.Y;
        Z = z;
        W = w;
    }

    public Double4(Double3 vector, double w)
    {
        X = vector.X;
        Y = vector.Y;
        Z = vector.Z;
        W = w;
    }

    public Double4(Double4 vector)
    {
        X = vector.X;
        Y = vector.Y;
        Z = vector.Z;
        W = vector.W;
    }

    public double X { get; set; }

    public double Y { get; set; }

    public double Z { get; set; }

    public double W { get; set; }

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
        get => new(X, Y, Z);
        set
        {
            X = value.X;
            Y = value.Y;
            Z = value.Z;
        }
    }

    public Double4 XYZW
    {
        get => this;
        set => this = value;
    }
}
