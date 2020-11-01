using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text;

using BEditor.Core.SDL2;

using Microsoft.VisualBasic.CompilerServices;

namespace BEditor.Core.Native {
    public unsafe static class FontProcess {
        private const string dll = "Runtimes\\BEditor.Extern";

        [DllImport(dll, EntryPoint = "FontOpen1")]
        private static extern void Open(byte* file, int size, out IntPtr font);
        [DllImport(dll, EntryPoint = "FontOpen2")]
        private static extern void Open(byte* file, int size, long index, out IntPtr font);

        [DllImport(dll, EntryPoint = "FontInit")]
        public static extern int Init();
        [DllImport(dll, EntryPoint = "FontQuit")]
        public static extern void Quit();

        public static void Open(string file, int size, out IntPtr font) {
            byte* utf8File = SDL.Utf8Encode(file);
            Open(utf8File, size, out font);

            Marshal.FreeHGlobal((IntPtr)utf8File);
        }
        public static void Open(string file, int size, long index, out IntPtr font) {
            byte* utf8File = SDL.Utf8Encode(file);
            Open(utf8File, size, index, out font);

            Marshal.FreeHGlobal((IntPtr)utf8File);
        }
        [DllImport(dll, EntryPoint = "FontClose")]
        public static extern void Close(IntPtr font);

        [DllImport(dll, EntryPoint = "FontHeight")]
        public static extern int Height(IntPtr font);
        [DllImport(dll, EntryPoint = "FontAscent")]
        public static extern int Ascent(IntPtr font);
        [DllImport(dll, EntryPoint = "FontDescent")]
        public static extern int Descent(IntPtr font);
        [DllImport(dll, EntryPoint = "FontLineSkip")]
        public static extern int LineSkip(IntPtr font);
        [DllImport(dll, EntryPoint = "FontFaces")]
        public static extern long Faces(IntPtr font);
        [DllImport(dll, EntryPoint = "FontIsFixedWidth")]
        public static extern bool IsFixedWidth(IntPtr font);
        [DllImport(dll, EntryPoint = "FontStyleName")]
        private static extern IntPtr StyleName_(IntPtr font);
        [DllImport(dll, EntryPoint = "FontFamilyName")]
        private static extern IntPtr FamilyName_(IntPtr font);
        [DllImport(dll, EntryPoint = "FontGlyphIsProvided")]
        public static extern int GlyphIsProvided(IntPtr font, ushort ch);

        public static string FamilyName(IntPtr font) {
            return TextConvert.UTF8_ToManaged(FamilyName_(font));
        }
        public static string StyleName(IntPtr font) {
            return TextConvert.UTF8_ToManaged(StyleName_(font));
        }

        #region Properties

        [DllImport(dll, EntryPoint = "FontGetStyle")]
        public static extern int GetStyle(IntPtr font);
        [DllImport(dll, EntryPoint = "FontSetStyle")]
        public static extern void SetStyle(IntPtr font, int style);

        [DllImport(dll, EntryPoint = "FontGetOutline")]
        public static extern int GetOutline(IntPtr font);
        [DllImport(dll, EntryPoint = "FontSetOutline")]
        public static extern void SetOutline(IntPtr font, int outline);

        [DllImport(dll, EntryPoint = "FontGetHinting")]
        public static extern int GetHinting(IntPtr font);
        [DllImport(dll, EntryPoint = "FontSetHinting")]
        public static extern void SetHinting(IntPtr font, int hinting);

        [DllImport(dll, EntryPoint = "FontGetKerning")]
        public static extern bool GetKerning(IntPtr font);
        [DllImport(dll, EntryPoint = "FontSetKerning")]
        public static extern void SetKerning(IntPtr font, bool allowed);

        #endregion

        #region Render

        [DllImport(dll, EntryPoint = "FontGlyphMetrics")]
        public static extern int GlyphMetrics(IntPtr font, ushort ch, out int minx, out int maxx, out int miny, out int maxy, out int advance);
        [DllImport(dll, EntryPoint = "FontSizeText")]
        public static extern bool SizeText(IntPtr font, [In()][MarshalAs(UnmanagedType.LPStr)] string text, out int width, out int height);
        [DllImport(dll, EntryPoint = "FontSizeUTF8")]
        public static extern bool SizeUTF8(IntPtr font, byte* text, out int width, out int height);
        public static bool SizeUTF8(IntPtr font, string text, out int width, out int height) {
            byte* utf8Text = TextConvert.Utf8Encode(text);
            bool result = SizeUTF8(font, utf8Text, out width, out height);

            Marshal.FreeHGlobal((IntPtr)utf8Text);
            return result;
        }
        [DllImport(dll, EntryPoint = "FontSizeUNICODE")]
        public static extern bool SizeUnicode(IntPtr font, [In()][MarshalAs(UnmanagedType.LPStr)] string text, out int width, out int height);


        [DllImport(dll, EntryPoint = "FontRenderGlyph")]
        public static extern void RenderGlyph(IntPtr font, ushort ch, byte r, byte g, byte b, byte a, out IntPtr returnmat);
        [DllImport(dll, EntryPoint = "FontRenderText")]
        public static extern void RenderText(IntPtr font, [In()][MarshalAs(UnmanagedType.LPStr)] string text, byte r, byte g, byte b, byte a, out IntPtr returnmat);
        [DllImport(dll, EntryPoint = "FontRenderUTF8")]
        public static extern void RenderUTF8(IntPtr font, byte* text, byte r, byte g, byte b, byte a, out IntPtr returnmat);
        [DllImport(dll, EntryPoint = "FontRenderUNICODE")]
        public static extern void RenderUnicode(IntPtr font, [In()][MarshalAs(UnmanagedType.LPStr)] string text, byte r, byte g, byte b, byte a, out IntPtr returnmat);

        public static void RenderUTF8(IntPtr font, string text, byte r, byte g, byte b, byte a, out IntPtr returnmat) {
            byte* utf8Text = SDL.Utf8Encode(text);
            RenderUTF8(font, utf8Text, r, g, b, a, out returnmat);

            Marshal.FreeHGlobal((IntPtr)utf8Text);
        }

        #endregion
    }
}
