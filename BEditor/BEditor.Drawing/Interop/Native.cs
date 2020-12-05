using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace BEditor.Drawing.Interop
{
    internal class Native
    {
        const string Library = "BEditor.Drawing.Interop";


        [DllImport(Library)]
        public static extern void Image_BoxBlur(ImageStruct image, float size, ImageStruct @out);
        [DllImport(Library)]
        public static extern void Image_GaussBlur(ImageStruct image, float size, ImageStruct @out);
        [DllImport(Library)]
        public static extern void Image_MedianBlur(ImageStruct image, int size, ImageStruct @out);

        [DllImport(Library)]
        public static extern void Image_Dilate(ImageStruct image, int f, ImageStruct @out);
        [DllImport(Library)]
        public static extern void Image_Erode(ImageStruct image, int f, ImageStruct @out);

        [DllImport(Library)]
        public static extern bool Image_Save(ImageStruct image, string filename);
    }
}
