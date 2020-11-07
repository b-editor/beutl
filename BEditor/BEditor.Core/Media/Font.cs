using System;
using System.Runtime.InteropServices;

using BEditor.Core.Media;
using BEditor.Core.Native;

namespace BEditor.Core.Media {
    public class Font : DisposableObject {
        private IntPtr ptr;

        #region Constructor

        public Font(string file, int size, bool isFitHeight = true, uint index = 0) {
            ptr = FontProcess.Open(file, (uint)size, isFitHeight, index);
            Size = size;
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
                FontProcess.SetStyle(ptr, (long)value);
            }
        }


        public int Height {
            get {
                return FontProcess.Height(ptr);
            }
        }
        public int Ascender {
            get {
                return FontProcess.Ascender(ptr);
            }
        }
        public int Descender {
            get {
                return FontProcess.Descender(ptr);
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

        public void SizeText(string text, out int width, out int height) {
            ThrowIfDisposed();

            FontProcess.SizeText(ptr, out var left, out var top, out var right, out var bottom, text);
            
            var rect = Rectangle.FromLTRB(left, top, right, bottom);
            width = rect.Width;
            height = rect.Height;
        }

        public Image RenderText(string text, Color color) {
            SizeText(text, out var width, out var height);

            //width *= 2;
            //height *= 2;

            var img = new Image(width, height, ImageType.ByteCh4);

            FontProcess.DrawText(ptr, 0, 0, text, color.R, color.G, color.B, img.Data, width * 4, width, height);

            return img;
        }

        #endregion

        #region StaticMethods

        public static void Quit() => FontProcess.Quit();

        public static bool Initialize() => FontProcess.Init();

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
}
