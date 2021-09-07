using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

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
