using Beutl.Media;

namespace PackageSample;

public record WellKnownSize(string Name, PixelSize Size)
{
    public override string ToString()
    {
        return $"{Name} ({Size.Width} x {Size.Height})";
    }
}
