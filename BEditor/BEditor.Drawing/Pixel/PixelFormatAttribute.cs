using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BEditor.Drawing.Pixel
{
    [AttributeUsage(AttributeTargets.Struct)]
    public class PixelFormatAttribute : Attribute
    {
        public PixelFormatAttribute(int channels, int mattype)
        {
            Channels=channels;
            MatType = mattype;
            MatIsSupported = true;
        }
        public PixelFormatAttribute(int channels)
        {
            Channels = channels;
        }

        public int Channels { get; }
        public int MatType { get; }
        public bool MatIsSupported { get; }
    }
}
