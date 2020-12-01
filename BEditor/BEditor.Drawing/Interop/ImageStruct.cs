using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace BEditor.Drawing.Interop
{
    [StructLayout(LayoutKind.Sequential)]
    internal unsafe struct ImageStruct
    {
        public int Width;
        public int Height;
        public int CvType;
        public void* Data;
    }
}
