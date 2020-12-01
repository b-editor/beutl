using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace BEditor.Drawing.Interop
{
    internal class Native
    {
        const string Library = "BEditor.Drawing.Interop";

        [DllImport(Library)]
        public static extern void Image_Flip(ImageStruct image, int mode);

        [DllImport(Library)]
        public static extern void Image_AreaExpansion(ImageStruct image, int top, int bottom, int left, int right);

        [DllImport(Library)]
        public static extern void Image_BoxFilter(ImageStruct image, float size);
        [DllImport(Library)]
        public static extern void Image_GaussBlur(ImageStruct image, float size);
        [DllImport(Library)]
        public static extern void Image_MedianBlur(ImageStruct image, int size);

        [DllImport(Library)]
        public static extern void Image_Dilate(ImageStruct image, int f);
        [DllImport(Library)]
        public static extern void Image_Erode(ImageStruct image, int f);

        [DllImport(Library)]
        public static extern void Image_Decode(byte[] buffer, IntPtr Length, int flags, out ImageStruct image);
        [DllImport(Library)]
        public static extern bool Image_Save(ImageStruct image, string filename);
    }
}
