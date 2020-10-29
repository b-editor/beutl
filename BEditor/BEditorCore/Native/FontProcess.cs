using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;

using BEditorCore.SDL2;

namespace BEditorCore.Native {
    public unsafe static class FontProcess {
        private const string dll = "Runtimes\\BEditorExtern";
        private static extern void Open(byte* file, int size, out IntPtr font);
        private static extern void Open(byte* file, int size, long index, out IntPtr font);

        public static extern int Init();
        public static extern void Quit();

        public static unsafe void Open(string file, int size, out IntPtr font) {
            byte* utf8File = SDL.Utf8Encode(file);
            Open(utf8File, size, out font);

            Marshal.FreeHGlobal((IntPtr)utf8File);
        }
        public static unsafe void Open(string file, int size, long index, out IntPtr font) {
            byte* utf8File = SDL.Utf8Encode(file);
            Open(utf8File, size, index, out font);

            Marshal.FreeHGlobal((IntPtr)utf8File);
        }
        public static extern void Close(IntPtr font);

        public static extern int GetStyle(IntPtr font);
        public static extern void SetStyle(IntPtr font, int style);


    }
}
