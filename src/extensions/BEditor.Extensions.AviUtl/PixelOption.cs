namespace BEditor.Extensions.AviUtl
{
    public enum PixelReadWriteOption
    {
        Object,
        Framebuffer,
    }

    public enum PixelType
    {
        Color,
        Rgb,
        YCbCr,
    }

    public record PixelOption(
        PixelType Type = PixelType.Color,
        PixelReadWriteOption PixelSource = PixelReadWriteOption.Object,
        PixelReadWriteOption PixelDestination = PixelReadWriteOption.Object)
    {
    }
}