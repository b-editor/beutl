using System;
using System.Runtime.InteropServices;

using BEditor.Core.Media;
using BEditor.Core.Native;

namespace BEditor.Core.Media
{
    /// <summary>
    /// FreeTypeのFaceの一部をカプセル化します
    /// </summary>
    public class Font : DisposableObject
    {
        private IntPtr ptr;

        #region Constructor

        /// <summary>
        /// <see cref="Font"/> クラスの新しいインスタンスを初期化します
        /// </summary>
        public Font(string file, int size, bool isFitHeight = true, uint index = 0)
        {
            ptr = FontProcess.Open(file, (uint)size, isFitHeight, index);
        }

        #endregion


        #region Properties

        /// <summary>
        /// フォントのスタイルを取得または設定します
        /// </summary>
        public FontStyle Style
        {
            get
            {
                ThrowIfDisposed();
                return (FontStyle)Enum.ToObject(typeof(FontStyle), FontProcess.GetStyle(ptr));
            }
            set
            {
                ThrowIfDisposed();
                FontProcess.SetStyle(ptr, (long)value);
            }
        }

        /// <summary>
        /// フォントの高さを取得します
        /// </summary>
        public int Height
        {
            get
            {
                return FontProcess.Height(ptr);
            }
        }
        /// <summary>
        /// https://www.freetype.org/freetype2/docs/tutorial/step2.html を参照してください
        /// </summary>
        public int Ascender
        {
            get
            {
                return FontProcess.Ascender(ptr);
            }
        }
        /// <summary>
        /// https://www.freetype.org/freetype2/docs/tutorial/step2.html を参照してください
        /// </summary>
        public int Descender
        {
            get
            {
                return FontProcess.Descender(ptr);
            }
        }
        /// <summary>
        /// フォントファミリー名を取得します
        /// </summary>
        public string FaceFamilyName
        {
            get
            {
                ThrowIfDisposed();
                return FontProcess.FamilyName(ptr);
            }
        }
        /// <summary>
        /// 現在のフォントスタイル名を取得します
        /// </summary>
        public string FaceStyleName
        {
            get
            {
                ThrowIfDisposed();
                return FontProcess.StyleName(ptr);
            }
        }

        #endregion

        #region Methods

        /// <summary>
        /// 指定した文字からサイズを測ります
        /// </summary>
        public void SizeText(string text, out int width, out int height)
        {
            ThrowIfDisposed();

            FontProcess.SizeText(ptr, out var left, out var top, out var right, out var bottom, text);

            var rect = Rectangle.FromLTRB(left, top, right, bottom);
            width = rect.Width;
            height = rect.Height;
        }

        /// <summary>
        /// 指定した文字から <see cref="Image"/> にレンダリングします
        /// </summary>
        public Image RenderText(string text, Color color)
        {
            SizeText(text, out var width, out var height);

            //width *= 2;
            height *= 2;

            var img = new Image(width, height, ImageType.ByteCh4);

            FontProcess.DrawText(ptr, 0, 0, text, color.R, color.G, color.B, img.Data, width * 4, width, height);

            return img;
        }

        #endregion

        #region StaticMethods

        /// <summary>
        /// ライブラリを終了します
        /// </summary>
        public static void Quit() => FontProcess.Quit();

        /// <summary>
        /// ライブラリを初期化します
        /// </summary>
        public static bool Initialize() => FontProcess.Init();

        #endregion

        protected override void OnDispose(bool disposing)
        {
            if (ptr != IntPtr.Zero && !IsDisposed)
            {
                FontProcess.Close(ptr);

                ptr = IntPtr.Zero;
            }
        }
    }

    /// <summary>
    /// フォントスタイルを表します
    /// </summary>
    public enum FontStyle
    {
        Normal = 0x00,
        Bold = 0x01,
        Italic = 0x02,
        UnderLine = 0x04,
        StrikeThrough = 0x08
    }
}
