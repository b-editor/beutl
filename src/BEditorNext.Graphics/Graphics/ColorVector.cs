using System.Runtime.InteropServices;

namespace BEditorNext.Graphics;

[StructLayout(LayoutKind.Sequential)]
public struct ColorVector
{
    public ColorVector(float r, float g, float b, float a)
    {
        R = r;
        G = g;
        B = b;
        A = a;
        W = 1;
    }

    public float R { readonly get; set; }

    public float G { readonly get; set; }

    public float B { readonly get; set; }

    public float A { readonly get; set; }

    public float W { readonly get; set; }
}
