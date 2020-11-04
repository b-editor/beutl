using System;
using System.Runtime.InteropServices;

using BEditor.Core.Media;
using BEditor.Core.Native;

namespace BEditor.Core.SDL2.TTF {
    public class Font : DisposableObject {
        private IntPtr ptr;

        #region Constructor

        public Font(string file, int ptsize) {
            FontProcess.Open(file, ptsize, out ptr);
            Size = ptsize;
        }

        public Font(string file, int ptsize, long index) {
            FontProcess.Open(file, ptsize, index, out ptr);
            Size = ptsize;
        }

        #endregion


        #region Properties

        public int Size { get; }

        public FontStyle Style {
            get {
                ThrowIfDisposed();
                return (FontStyle)Enum.ToObject(typeof(FontStyle), FontProcess.GetStyle(ptr));
            }
            set {
                ThrowIfDisposed();
                FontProcess.SetStyle(ptr, (int)value);
            }
        }

        public int Outline {
            get {
                ThrowIfDisposed();
                return FontProcess.GetOutline(ptr);
            }
            set {
                ThrowIfDisposed();
                FontProcess.SetOutline(ptr, value);
            }
        }

        public Hinting Hinting {
            get {
                ThrowIfDisposed();
                return (Hinting)Enum.ToObject(typeof(Hinting), FontProcess.GetHinting(ptr));
            }
            set {
                ThrowIfDisposed();
                FontProcess.SetHinting(ptr, (int)value);
            }
        }

        public int Height {
            get {
                ThrowIfDisposed();
                return FontProcess.Height(ptr);
            }
        }

        public int Ascent {
            get {
                ThrowIfDisposed();
                return FontProcess.Ascent(ptr);
            }
        }

        public int Descent {
            get {
                ThrowIfDisposed();
                return FontProcess.Descent(ptr);
            }
        }

        public int LineSkip {
            get {
                ThrowIfDisposed();
                return FontProcess.LineSkip(ptr);
            }
        }

        public bool Kerning {
            get {
                ThrowIfDisposed();
                return FontProcess.GetKerning(ptr);
            }
            set {
                ThrowIfDisposed();
                FontProcess.SetKerning(ptr, value);
            }
        }

        public long Faces {
            get {
                ThrowIfDisposed();
                return FontProcess.Faces(ptr);
            }
        }

        public bool IsFixedWidth {
            get {
                ThrowIfDisposed();
                return FontProcess.IsFixedWidth(ptr);
            }
        }

        public string FaceFamilyName {
            get {
                ThrowIfDisposed();
                return FontProcess.FamilyName(ptr);
            }
        }

        public string FaceStyleName {
            get {
                ThrowIfDisposed();
                return FontProcess.StyleName(ptr);
            }
        }

        #endregion

        #region Methods

        public int GlyphIsProvided(ushort ch) {
            ThrowIfDisposed();
            var result = FontProcess.GlyphIsProvided(ptr, ch);
            return result;
        }
        
        public bool GlyphMetrics(ushort ch, out int minx, out int maxx, out int miny, out int maxy, out int advance) {
            ThrowIfDisposed();
            return FontProcess.GlyphMetrics(ptr, ch, out minx, out maxx, out miny, out maxy, out advance) == 0;
        }

        public bool SizeText(string text, out int width, out int height) {
            ThrowIfDisposed();
            return FontProcess.SizeText(ptr, text, out width, out height);
        }
        public bool SizeUnicode(string text, out int width, out int height) {
            ThrowIfDisposed();
            return FontProcess.SizeUnicode(ptr, text, out width, out height);
        }
        public bool SizeUTF8(string text, out int width, out int height) {
            ThrowIfDisposed();
            return FontProcess.SizeUTF8(ptr, text, out width, out height);
        }

        public Image RenderGlyph(ushort ch,Color color) {
            FontProcess.RenderGlyph(ptr, ch, color.R, color.G, color.B, color.A, out var mat);
            return new Image(mat);
        }
        public Image RenderUnicode(string text,Color color) {
            FontProcess.RenderUnicode(ptr, text, color.R, color.G, color.B, color.A, out var mat);
            return new Image(mat);
        }
        public Image RenderUTF8(string text, Color color) {
            FontProcess.RenderUTF8(ptr, text, color.R, color.G, color.B, color.A, out var mat);
            return new Image(mat);
        }
        public Image RenderText(string text, Color color) {
            FontProcess.RenderText(ptr, text, color.R, color.G, color.B, color.A, out var mat);
            return new Image(mat);
        }

        #endregion

        #region StaticMethods

        public static void Quit() => FontProcess.Quit();

        public static bool Initialize() => FontProcess.Init() == 0;

        #endregion

        protected override void OnDispose(bool disposing) {
            if (ptr != IntPtr.Zero && !IsDisposed) {
                FontProcess.Close(ptr);

                ptr = IntPtr.Zero;
            }
        }
    }

    public enum FontStyle {
        Normal = 0x00,
        Bold = 0x01,
        Italic = 0x02,
        UnderLine = 0x04,
        StrikeThrough = 0x08
    }
    public enum Hinting {
        Normal = 0,
        Light = 1,
        Mono = 2,
        None = 3,
        LightSubPixel = 4
    }
}
