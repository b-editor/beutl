using System;
using System.Collections.Generic;
using System.Diagnostics.Contracts;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text;

using Microsoft.VisualBasic.CompilerServices;

namespace BEditor.Core.Native {
    public unsafe static class FontProcess {
        private const string dll = "BEditor.Extern.dll";
        //private const string ft_lib = "freetype.dll";

        [Pure, DllImport(dll, EntryPoint = "FontInit")]
        public static extern bool Init();
        [Pure, DllImport(dll, EntryPoint = "FontQuit")]
        public static extern void Quit();

        [Pure, DllImport(dll, EntryPoint = "ImageFontCreate1")]
        public static extern IntPtr Open([In()][MarshalAs(UnmanagedType.LPStr)] string filename, uint height, bool isFitHeight, long index);
        [Pure, DllImport(dll, EntryPoint = "ImageFontCreate2")]
        public static extern IntPtr Open([In()][MarshalAs(UnmanagedType.LPStr)] string filename, uint height, bool isFitHeight);
        [Pure, DllImport(dll, EntryPoint = "ImageFontCreate3")]
        public static extern IntPtr Open([In()][MarshalAs(UnmanagedType.LPStr)] string filename, uint height);


        [Pure, DllImport(dll, EntryPoint = "ImageFontClose")]
        public static extern void Close(IntPtr font);

        [Pure, DllImport(dll, EntryPoint = "ImageFontSizeText")]
        public static extern void SizeText(
            IntPtr font,
            out int left,
            out int top,
            out int right,
            out int bottom,
            [In()][MarshalAs(UnmanagedType.LPStr)]
            string str);

        [Pure, DllImport(dll, EntryPoint = "ImageFontDrawTextBGRA")]
        public static extern void DrawText(
            IntPtr font,
            int x, int y,
            [In()][MarshalAs(UnmanagedType.LPStr)]
            string str,
            int r, int g, int b,
            IntPtr image,
            int stride,
            int cx, int cy);


        [Pure, DllImport(dll, EntryPoint = "ImageFontGetStyle")]
        public static extern long GetStyle(IntPtr font);
        [Pure, DllImport(dll, EntryPoint = "ImageFontSetStyle")]
        public static extern void SetStyle(IntPtr font, long value);

        [Pure, DllImport(dll, EntryPoint = "ImageFontHeight")]
        public static extern int Height(IntPtr font);
        [Pure, DllImport(dll, EntryPoint = "ImageFontAscender")]
        public static extern int Ascender(IntPtr font);
        [Pure, DllImport(dll, EntryPoint = "ImageFontDescender")]
        public static extern int Descender(IntPtr font);
        [Pure, DllImport(dll, EntryPoint = "ImageFontFamilyName")]
        private static extern IntPtr FamilyName_(IntPtr font);
        [Pure, DllImport(dll, EntryPoint ="ImageFontStyleName")]
        private static extern IntPtr StyleName_(IntPtr font);
        public static string FamilyName(IntPtr font) {
            return TextConvert.UTF8_ToManaged(FamilyName_(font));
        }
        public static string StyleName(IntPtr font) {
            return TextConvert.UTF8_ToManaged(StyleName_(font));
        }





    }
}
