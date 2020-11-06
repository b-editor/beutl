using System;
using System.Collections.Generic;
using System.Diagnostics.Contracts;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text;

using BEditor.Core.SDL2;

using Microsoft.VisualBasic.CompilerServices;

namespace BEditor.Core.Native {
    public unsafe static class FontProcess {
        private const string dll = "BEditor.Extern";

        [Pure, DllImport(dll, EntryPoint = "FontInit")]
        public static extern bool Init();
        [Pure, DllImport(dll, EntryPoint = "FontQuit")]
        public static extern void Quit();

        [Pure, DllImport(dll, EntryPoint = "ImageFontCreate1")]
        private static extern IntPtr Open(byte* filename, uint height, bool isFitHeight, long index);
        [Pure, DllImport(dll, EntryPoint = "ImageFontCreate2")]
        private static extern IntPtr Open(byte* filename, uint height, bool isFitHeight);
        [Pure, DllImport(dll, EntryPoint = "ImageFontCreate3")]
        private static extern IntPtr Open(byte* filename, uint height);

        public static IntPtr Open(string filename, uint height, bool isFitHeight, long index) {
            byte* utf8File = TextConvert.UTF8Encode(filename);
            var ptr = Open(utf8File, height, isFitHeight, index);

            Marshal.FreeHGlobal((IntPtr)utf8File);

            return ptr;
        }
        public static IntPtr Open(string filename, uint height, bool isFitHeight) {
            byte* utf8File = TextConvert.UTF8Encode(filename);
            var ptr = Open(utf8File, height, isFitHeight);

            Marshal.FreeHGlobal((IntPtr)utf8File);

            return ptr;
        }
        public static IntPtr Open(string filename, uint height) {
            byte* utf8File = TextConvert.UTF8Encode(filename);
            var ptr = Open(utf8File, height);

            Marshal.FreeHGlobal((IntPtr)utf8File);

            return ptr;
        }

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

        //[DllImport(dll, EntryPoint = "FontHeight")]
        //public static extern int Height(IntPtr font);
        //[DllImport(dll, EntryPoint = "FontAscent")]
        //public static extern int Ascent(IntPtr font);
        //[DllImport(dll, EntryPoint = "FontDescent")]
        //public static extern int Descent(IntPtr font);
        //[DllImport(dll, EntryPoint = "FontLineSkip")]
        //public static extern int LineSkip(IntPtr font);
        //[DllImport(dll, EntryPoint = "FontFaces")]
        //public static extern long Faces(IntPtr font);
        //[DllImport(dll, EntryPoint = "FontIsFixedWidth")]
        //public static extern bool IsFixedWidth(IntPtr font);
        //[DllImport(dll, EntryPoint = "FontStyleName")]
        //private static extern IntPtr StyleName_(IntPtr font);
        //[DllImport(dll, EntryPoint = "FontFamilyName")]
        //private static extern IntPtr FamilyName_(IntPtr font);
        //[DllImport(dll, EntryPoint = "FontGlyphIsProvided")]
        //public static extern int GlyphIsProvided(IntPtr font, ushort ch);

        //public static string FamilyName(IntPtr font) {
        //    return TextConvert.UTF8_ToManaged(FamilyName_(font));
        //}
        //public static string StyleName(IntPtr font) {
        //    return TextConvert.UTF8_ToManaged(StyleName_(font));
        //}

        //#region Properties

        //[DllImport(dll, EntryPoint = "FontGetStyle")]
        //public static extern int GetStyle(IntPtr font);
        //[DllImport(dll, EntryPoint = "FontSetStyle")]
        //public static extern void SetStyle(IntPtr font, int style);

        //[DllImport(dll, EntryPoint = "FontGetOutline")]
        //public static extern int GetOutline(IntPtr font);
        //[DllImport(dll, EntryPoint = "FontSetOutline")]
        //public static extern void SetOutline(IntPtr font, int outline);

        //[DllImport(dll, EntryPoint = "FontGetHinting")]
        //public static extern int GetHinting(IntPtr font);
        //[DllImport(dll, EntryPoint = "FontSetHinting")]
        //public static extern void SetHinting(IntPtr font, int hinting);

        //[DllImport(dll, EntryPoint = "FontGetKerning")]
        //public static extern bool GetKerning(IntPtr font);
        //[DllImport(dll, EntryPoint = "FontSetKerning")]
        //public static extern void SetKerning(IntPtr font, bool allowed);

        //#endregion

        //#region Render

        //[DllImport(dll, EntryPoint = "FontGlyphMetrics")]
        //public static extern int GlyphMetrics(IntPtr font, ushort ch, out int minx, out int maxx, out int miny, out int maxy, out int advance);
        //[DllImport(dll, EntryPoint = "FontSizeText")]
        //public static extern bool SizeText(IntPtr font, [In()][MarshalAs(UnmanagedType.LPStr)] string text, out int width, out int height);
        //[DllImport(dll, EntryPoint = "FontSizeUTF8")]
        //public static extern bool SizeUTF8(IntPtr font, byte* text, out int width, out int height);
        //public static bool SizeUTF8(IntPtr font, string text, out int width, out int height) {
        //    byte* utf8Text = TextConvert.UTF8Encode(text);
        //    bool result = SizeUTF8(font, utf8Text, out width, out height);

        //    Marshal.FreeHGlobal((IntPtr)utf8Text);
        //    return result;
        //}
        //[DllImport(dll, EntryPoint = "FontSizeUNICODE")]
        //public static extern bool SizeUnicode(IntPtr font, [In()][MarshalAs(UnmanagedType.LPStr)] string text, out int width, out int height);


        //[DllImport(dll, EntryPoint = "FontRenderGlyph")]
        //public static extern void RenderGlyph(IntPtr font, ushort ch, byte r, byte g, byte b, byte a, out IntPtr returnmat);
        //[DllImport(dll, EntryPoint = "FontRenderText")]
        //public static extern void RenderText(IntPtr font, [In()][MarshalAs(UnmanagedType.LPStr)] string text, byte r, byte g, byte b, byte a, out IntPtr returnmat);
        //[DllImport(dll, EntryPoint = "FontRenderUTF8")]
        //public static extern void RenderUTF8(IntPtr font, byte* text, byte r, byte g, byte b, byte a, out IntPtr returnmat);
        //[DllImport(dll, EntryPoint = "FontRenderUNICODE")]
        //public static extern void RenderUnicode(IntPtr font, [In()][MarshalAs(UnmanagedType.LPStr)] string text, byte r, byte g, byte b, byte a, out IntPtr returnmat);

        //public static void RenderUTF8(IntPtr font, string text, byte r, byte g, byte b, byte a, out IntPtr returnmat) {
        //    byte* utf8Text = TextConvert.UTF8Encode(text);
        //    RenderUTF8(font, utf8Text, r, g, b, a, out returnmat);

        //    Marshal.FreeHGlobal((IntPtr)utf8Text);
        //}

        //#endregion
    }
}
