using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;

namespace BEditor.Graphics {
    public unsafe static partial class GL {
        const string Library = "opengl32.dll";

        //public static extern void BeginConditionalRender(uint id, uint mode);
        //public static extern void BeginQuery(uint target, uint id);
        //public static extern void BeginQueryIndexed(uint target, uint index, uint id);
        //public static extern void BeginTransformFeedback(uint primitiveMode);
        //public static extern void BindAttribLocation(uint program, uint index, [In()][MarshalAs(UnmanagedType.LPStr)] string name);
        //public static extern void BindBuffer(uint target, uint buffer);
        #region B

        [DllImport(Library, EntryPoint = "glBindTexture")]
        public static extern void BindTexture(TextureTarget target, int texture);
        [DllImport(Library, EntryPoint = "glBindTexture")]
        public static extern void BindTexture(TextureTarget target, uint texture);

        #endregion

        #region D

        [DllImport(Library, EntryPoint = "glDisable")]
        public static extern void Disable(EnableCap cap);

        #endregion

        #region E

        [DllImport(Library, EntryPoint = "glEnable")]
        public static extern void Enable(EnableCap cap);

        #endregion

        #region G

        [DllImport(Library, EntryPoint = "glGenTextures")]
        public static extern void GenTextures(int n, [Out] int* textures);
        [DllImport(Library, EntryPoint = "glGenTextures")]
        public static extern void GenTextures(int n, [Out] uint* textures);

        [DllImport(Library, EntryPoint = "glGenerateMipmap")]
        public static extern void GenerateMipmap(GenerateMipmapTarget target);

        #endregion

        #region P

        [DllImport(Library, EntryPoint = "glPushMatrix")]
        public static extern void PushMatrix();

        #endregion

        #region R

        [DllImport(Library)]
        public static extern void ReadBuffer(ReadBufferMode src);

        [DllImport(Library)]
        public static extern void ReadPixels(int x, int y, int width, int height, PixelFormat format, PixelType type, [Out] IntPtr pixels);

        #endregion

        #region T

        [DllImport(Library, EntryPoint = "glTexImage1D")]
        public static extern void TexImage1D(TextureTarget target, int level, PixelInternalFormat internalformat, int width, int border, PixelFormat format, PixelType type, IntPtr pixels);

        [DllImport(Library, EntryPoint = "glTexImage2D")]
        public static extern void TexImage2D(TextureTarget target, int level, PixelInternalFormat internalformat, int width, int height, int border, PixelFormat format, PixelType type, IntPtr pixels);

        [DllImport(Library, EntryPoint = "glTexImage2DMultisample")]
        public static extern void TexImage2DMultisample(TextureTargetMultisample target, int samples, PixelInternalFormat internalformat, int width, int height, bool fixedsamplelocations);

        [DllImport(Library, EntryPoint = "glTexImage3D")]
        public static extern void TexImage3D(TextureTarget target, int level, PixelInternalFormat internalformat, int width, int height, int depth, int border, PixelFormat format, PixelType type, IntPtr pixels);

        [DllImport(Library, EntryPoint = "glTexImage3DMultisample")]
        public static extern void TexImage3DMultisample(TextureTargetMultisample target, int samples, PixelInternalFormat internalformat, int width, int height, int depth, bool fixedsamplelocations);


        [DllImport(Library, EntryPoint = "glTexParameterf")]
        public static extern void TexParameter(TextureTarget target, TextureParameterName pname, float param);

        [DllImport(Library, EntryPoint = "glTexParameteri")]
        public static extern void TexParameter(TextureTarget target, TextureParameterName pname, int param);

        [DllImport(Library, EntryPoint = "glTexParameterfv")]
        public static extern void TexParameter(TextureTarget target, TextureParameterName pname, float* @params);

        [DllImport(Library, EntryPoint = "glTexParameteriv")]
        public static extern void TexParameter(TextureTarget target, TextureParameterName pname, int* @params);

        #endregion
    }
}
