using System;
using System.Runtime.InteropServices;

using BEditorCore.Media;

namespace BEditorCore.SDL2.TTF {
    public class Font : DisposableObject {
        private IntPtr ptr;

        #region Constructor

        public Font(string file, int ptsize) {
            ptr = SDL.TTF.OpenFont(file, ptsize);
            Size = ptsize;
        }

        public Font(string file, int ptsize, long index) {
            ptr = SDL.TTF.OpenFontIndex(file, ptsize, index);
            Size = ptsize;
        }

        #endregion


        #region Properties

        public int Size { get; }

        public FontStyle Style {
            get {
                ThrowIfDisposed();
                return (FontStyle)Enum.ToObject(typeof(FontStyle), SDL.TTF.GetFontStyle(ptr));
            }
            set {
                ThrowIfDisposed();
                SDL.TTF.SetFontStyle(ptr, (int)value);
            }
        }

        public int Outline {
            get {
                ThrowIfDisposed();
                return SDL.TTF.GetFontOutline(ptr);
            }
            set {
                ThrowIfDisposed();
                SDL.TTF.SetFontOutline(ptr, value);
            }
        }

        public Hinting Hinting {
            get {
                ThrowIfDisposed();
                return (Hinting)Enum.ToObject(typeof(Hinting), SDL.TTF.GetFontHinting(ptr));
            }
            set {
                ThrowIfDisposed();
                SDL.TTF.SetFontHinting(ptr, (int)value);
            }
        }

        public int Height {
            get {
                ThrowIfDisposed();
                return SDL.TTF.FontHeight(ptr);
            }
        }

        public int Ascent {
            get {
                ThrowIfDisposed();
                return SDL.TTF.FontAscent(ptr);
            }
        }

        public int Descent {
            get {
                ThrowIfDisposed();
                return SDL.TTF.FontDescent(ptr);
            }
        }

        public int LineSkip {
            get {
                ThrowIfDisposed();
                return SDL.TTF.FontLineSkip(ptr);
            }
        }

        public bool Kerning {
            get {
                ThrowIfDisposed();
                var val = SDL.TTF.TTF_GetFontKerning(ptr);
                return val != 0;
            }
            set {
                ThrowIfDisposed();
                if (value) {
                    SDL.TTF.TTF_SetFontKerning(ptr, 1);
                }
                else {
                    SDL.TTF.TTF_SetFontKerning(ptr, 0);
                }
            }
        }

        public long Faces {
            get {
                ThrowIfDisposed();
                return SDL.TTF.TTF_FontFaces(ptr);
            }
        }

        public bool IsFixedWidth {
            get {
                ThrowIfDisposed();
                return SDL.TTF.TTF_FontFaceIsFixedWidth(ptr) != 0;
            }
        }

        public string FaceFamilyName {
            get {
                ThrowIfDisposed();
                return SDL.TTF.TTF_FontFaceFamilyName(ptr);
            }
        }

        public string FaceStyleName {
            get {
                ThrowIfDisposed();
                return SDL.TTF.TTF_FontFaceStyleName(ptr);
            }
        }

        #endregion

        #region Methods

        public int GlyphIsProvided(ushort ch) {
            ThrowIfDisposed();
            var result = SDL.TTF.TTF_GlyphIsProvided(ptr, ch);
            return result;
        }
        public int GlyphIsProvided32(uint ch) {
            ThrowIfDisposed();
            var result = SDL.TTF.TTF_GlyphIsProvided32(ptr, ch);
            return result;
        }

        public bool GlyphMetrics(ushort ch, out int minx, out int maxx, out int miny, out int maxy, out int advance) {
            ThrowIfDisposed();
            return SDL.TTF.TTF_GlyphMetrics(ptr, ch, out minx, out maxx, out miny, out maxy, out advance) == 0;
        }
        public bool GlyphMetrics32(uint ch, out int minx, out int maxx, out int miny, out int maxy, out int advance) {
            ThrowIfDisposed();
            return SDL.TTF.TTF_GlyphMetrics32(ptr, ch, out minx, out maxx, out miny, out maxy, out advance) == 0;
        }

        public bool SizeText(string text, out int width, out int height) {
            ThrowIfDisposed();
            return SDL.TTF.TTF_SizeText(ptr, text, out width, out height) == 0;
        }
        public bool SizeUnicode(string text, out int width, out int height) {
            ThrowIfDisposed();
            return SDL.TTF.TTF_SizeUNICODE(ptr, text, out width, out height) == 0;
        }
        public bool SizeUTF8(string text, out int width, out int height) {
            ThrowIfDisposed();
            return SDL.TTF.TTF_SizeUTF8(ptr, text, out width, out height) == 0;
        }


        public Image RenderGlyph32(uint ch, Color3 color) {
            var surface = SDL.TTF.TTF_RenderGlyph32_Blended(ptr, ch, color.ToSDL());
            SDL.SDL_LockSurface(surface);

            SDL.SDL_Surface sur = (SDL.SDL_Surface)Marshal.PtrToStructure(surface, typeof(SDL.SDL_Surface));

            var img = new Image(sur.w, sur.h, sur.pixels, ImageType.ByteChannel4);
            SDL.SDL_UnlockSurface(surface);


            img.Disposable.Add(Toolkit.CreateDisposable(() => SDL.SDL_FreeSurface(surface)));

            return img;
        }
        public Image RenderGlyph(ushort ch, Color3 color) {
            var surface = SDL.TTF.TTF_RenderGlyph_Blended(ptr, ch, color.ToSDL());
            SDL.SDL_LockSurface(surface);

            SDL.SDL_Surface sur = (SDL.SDL_Surface)Marshal.PtrToStructure(surface, typeof(SDL.SDL_Surface));

            var img = new Image(sur.w, sur.h, sur.pixels, ImageType.ByteChannel4);
            SDL.SDL_UnlockSurface(surface);


            img.Disposable.Add(Toolkit.CreateDisposable(() => SDL.SDL_FreeSurface(surface)));

            return img;
        }
        public Image RenderUnicode(string text, Color3 color) {
            var surface = SDL.TTF.TTF_RenderUNICODE_Blended(ptr, text, color.ToSDL());
            SDL.SDL_LockSurface(surface);

            SDL.SDL_Surface sur = (SDL.SDL_Surface)Marshal.PtrToStructure(surface, typeof(SDL.SDL_Surface));

            var img = new Image(sur.w, sur.h, sur.pixels, ImageType.ByteChannel4);
            SDL.SDL_UnlockSurface(surface);


            img.Disposable.Add(Toolkit.CreateDisposable(() => SDL.SDL_FreeSurface(surface)));

            return img;
        }
        public Image RenderUTF8(string text, Color3 color) {
            var surface = SDL.TTF.TTF_RenderUTF8_Blended(ptr, text, color.ToSDL());
            SDL.SDL_LockSurface(surface);

            SDL.SDL_Surface sur = (SDL.SDL_Surface)Marshal.PtrToStructure(surface, typeof(SDL.SDL_Surface));

            var img = new Image(sur.w, sur.h, sur.pixels, ImageType.ByteChannel4);
            SDL.SDL_UnlockSurface(surface);

            img.Disposable.Add(Toolkit.CreateDisposable(() => SDL.SDL_FreeSurface(surface)));

            return img;
        }
        public Image RenderText(string text, Color3 color) {
            var surface = SDL.TTF.TTF_RenderText_Blended(ptr, text, color.ToSDL());
            SDL.SDL_LockSurface(surface);

            SDL.SDL_Surface sur = (SDL.SDL_Surface)Marshal.PtrToStructure(surface, typeof(SDL.SDL_Surface));

            var img = new Image(sur.w, sur.h, sur.pixels, ImageType.ByteChannel4);
            SDL.SDL_UnlockSurface(surface);


            img.Disposable.Add(Toolkit.CreateDisposable(() => SDL.SDL_FreeSurface(surface)));

            return img;
        }

        #endregion

        #region StaticMethods

        public static void Quit() => SDL.TTF.TTF_Quit();

        public static bool Initialize() => SDL.TTF.Initialize() == 0;

        public static string GetError() => SDL.SDL_GetError();

        #endregion

        protected override void OnDispose(bool disposing) {
            if (ptr != IntPtr.Zero && !IsDisposed) {
                SDL.TTF.TTF_CloseFont(ptr);

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
