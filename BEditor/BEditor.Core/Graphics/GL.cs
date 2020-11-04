using System;
using System.Collections.Generic;
using System.Diagnostics.Contracts;
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

        [Pure, DllImport(Library, EntryPoint = "glBegin", ExactSpelling = true)]
        public static extern void Begin(PrimitiveType mode);


        [Pure, DllImport(Library, EntryPoint = "glBindTexture", ExactSpelling = true)]
        public static extern void BindTexture(TextureTarget target, int texture);
        [Pure, DllImport(Library, EntryPoint = "glBindTexture", ExactSpelling = true)]
        public static extern void BindTexture(TextureTarget target, uint texture);

        #endregion

        #region C

        [Pure, DllImport(Library, EntryPoint = "glColor3b", ExactSpelling = true)]
        public static extern void Color3(sbyte red, sbyte green, sbyte blue);

        [Pure, DllImport(Library, EntryPoint = "glColor3s", ExactSpelling = true)]
        public static extern void Color3(short red, short green, short blue);

        [Pure, DllImport(Library, EntryPoint = "glColor3i", ExactSpelling = true)]
        public static extern void Color3(int red, int green, int blue);

        [Pure, DllImport(Library, EntryPoint = "glColor3f", ExactSpelling = true)]
        public static extern void Color3(float red, float green, float blue);

        [Pure, DllImport(Library, EntryPoint = "glColor3d", ExactSpelling = true)]
        public static extern void Color3(double red, double green, double blue);

        [Pure, DllImport(Library, EntryPoint = "glColor3ub", ExactSpelling = true)]
        public static extern void Color3(byte red, byte green, byte blue);

        [Pure, DllImport(Library, EntryPoint = "glColor3us", ExactSpelling = true)]
        public static extern void Color3(ushort red, ushort green, ushort blue);

        [Pure, DllImport(Library, EntryPoint = "glColor3ui", ExactSpelling = true)]
        public static extern void Color3(uint red, uint green, uint blue);


        [Pure, DllImport(Library, EntryPoint = "glColor3bv", ExactSpelling = true)]
        public static extern void Color3(sbyte* v);

        [Pure, DllImport(Library, EntryPoint = "glColor3sv", ExactSpelling = true)]
        public static extern void Color3(short* v);

        [Pure, DllImport(Library, EntryPoint = "glColor3iv", ExactSpelling = true)]
        public static extern void Color3(int* v);

        [Pure, DllImport(Library, EntryPoint = "glColor3fv", ExactSpelling = true)]
        public static extern void Color3(float* v);

        [Pure, DllImport(Library, EntryPoint = "glColor3dv", ExactSpelling = true)]
        public static extern void Color3(double* v);

        [Pure, DllImport(Library, EntryPoint = "glColor3ubv", ExactSpelling = true)]
        public static extern void Color3(byte* v);

        [Pure, DllImport(Library, EntryPoint = "glColor3usv", ExactSpelling = true)]
        public static extern void Color3(ushort* v);

        [Pure, DllImport(Library, EntryPoint = "glColor3uiv", ExactSpelling = true)]
        public static extern void Color3(uint* v);


        [Pure, DllImport(Library, EntryPoint = "glColor4b", ExactSpelling = true)]
        public static extern void Color4(sbyte red, sbyte green, sbyte blue, sbyte alpha);

        [Pure, DllImport(Library, EntryPoint = "glColor4s", ExactSpelling = true)]
        public static extern void Color4(short red, short green, short blue, short alpha);

        [Pure, DllImport(Library, EntryPoint = "glColor4i", ExactSpelling = true)]
        public static extern void Color4(int red, int green, int blue, int alpha);

        [Pure, DllImport(Library, EntryPoint = "glColor4f", ExactSpelling = true)]
        public static extern void Color4(float red, float green, float blue, float alpha);

        [Pure, DllImport(Library, EntryPoint = "glColor4d", ExactSpelling = true)]
        public static extern void Color4(double red, double green, double blue, double alpha);

        [Pure, DllImport(Library, EntryPoint = "glColor4ub", ExactSpelling = true)]
        public static extern void Color4(byte red, byte green, byte blue, byte alpha);

        [Pure, DllImport(Library, EntryPoint = "glColor4us", ExactSpelling = true)]
        public static extern void Color4(ushort red, ushort green, ushort blue, ushort alpha);

        [Pure, DllImport(Library, EntryPoint = "glColor4ui", ExactSpelling = true)]
        public static extern void Color4(uint red, uint green, uint blue, uint alpha);


        [Pure, DllImport(Library, EntryPoint = "glColor4bv", ExactSpelling = true)]
        public static extern void Color4(sbyte* v);

        [Pure, DllImport(Library, EntryPoint = "glColor4sv", ExactSpelling = true)]
        public static extern void Color4(short* v);

        [Pure, DllImport(Library, EntryPoint = "glColor4iv", ExactSpelling = true)]
        public static extern void Color4(int* v);

        [Pure, DllImport(Library, EntryPoint = "glColor4fv", ExactSpelling = true)]
        public static extern void Color4(float* v);

        [Pure, DllImport(Library, EntryPoint = "glColor4dv", ExactSpelling = true)]
        public static extern void Color4(double* v);

        [Pure, DllImport(Library, EntryPoint = "glColor4ubv", ExactSpelling = true)]
        public static extern void Color4(byte* v);

        [Pure, DllImport(Library, EntryPoint = "glColor4usv", ExactSpelling = true)]
        public static extern void Color4(ushort* v);

        [Pure, DllImport(Library, EntryPoint = "glColor4uiv", ExactSpelling = true)]
        public static extern void Color4(uint* v);

        #endregion

        #region D

        [Pure, DllImport(Library, EntryPoint = "glDisable", ExactSpelling = true)]
        public static extern void Disable(EnableCap cap);

        [Pure, DllImport(Library, EntryPoint = "glDeleteTextures", ExactSpelling = true)]
        public static extern void DeleteTextures(int n, int* textures);
        [Pure, DllImport(Library, EntryPoint = "glDeleteTextures", ExactSpelling = true)]
        public static extern void DeleteTextures(int n, uint* textures);

        #endregion

        #region E

        [Pure, DllImport(Library, EntryPoint = "glEnd", ExactSpelling = true)]
        public static extern void End();

        [Pure, DllImport(Library, EntryPoint = "glEnable", ExactSpelling = true)]
        public static extern void Enable(EnableCap cap);

        #endregion

        #region G

        [Pure, DllImport(Library, EntryPoint = "glGenTextures", ExactSpelling = true)]
        public static extern void GenTextures(int n, [Out] int* textures);
        [Pure, DllImport(Library, EntryPoint = "glGenTextures", ExactSpelling = true)]
        public static extern void GenTextures(int n, [Out] uint* textures);

        [Pure, DllImport(Library, EntryPoint = "glGenerateMipmap", ExactSpelling = true)]
        public static extern void GenerateMipmap(GenerateMipmapTarget target);

        #endregion

        #region L

        [Pure, DllImport(Library, EntryPoint = "glLoadIdentity", ExactSpelling = true)]
        public static extern void LoadIdentity();
        [Pure, DllImport(Library, EntryPoint = "glLoadMatrixf", ExactSpelling = true)]
        public static extern void LoadMatrix(float* m);
        [Pure, DllImport(Library, EntryPoint = "glLoadMatrixd", ExactSpelling = true)]
        public static extern void LoadMatrix(double* m);

        #endregion

        #region M

        [Pure, DllImport(Library, EntryPoint = "glMatrixMode", ExactSpelling = true)]
        public static extern void MatrixMode(MatrixMode mode);

        [Pure, DllImport(Library, EntryPoint = "glMaterialf", ExactSpelling = true)]
        public static extern void Material(MaterialFace face, MaterialParameter pname, float param);

        [Pure, DllImport(Library, EntryPoint = "glMaterialfv", ExactSpelling = true)]
        public static extern void Material(MaterialFace face, MaterialParameter pname, float* @params);

        [Pure, DllImport(Library, EntryPoint = "glMateriali", ExactSpelling = true)]
        public static extern void Material(MaterialFace face, MaterialParameter pname, int param);

        [Pure, DllImport(Library, EntryPoint = "glMaterialiv", ExactSpelling = true)]
        public static extern void Material(MaterialFace face, MaterialParameter pname, int* @params);

        #endregion

        #region N

        [Pure, DllImport(Library, EntryPoint = "glNormal3b", ExactSpelling = true)]
        public static extern void Normal3(byte nx, byte ny, byte nz);
        [Pure, DllImport(Library, EntryPoint = "glNormal3b", ExactSpelling = true)]
        public static extern void Normal3(sbyte nx, sbyte ny, sbyte nz);
        [Pure, DllImport(Library, EntryPoint = "glNormal3d", ExactSpelling = true)]
        public static extern void Normal3(double nx, double ny, double nz);
        [Pure, DllImport(Library, EntryPoint = "glNormal3f", ExactSpelling = true)]
        public static extern void Normal3(float nx, float ny, float nz);
        [Pure, DllImport(Library, EntryPoint = "glNormal3i", ExactSpelling = true)]
        public static extern void Normal3(int nx, int ny, double nz);
        [Pure, DllImport(Library, EntryPoint = "glNormal3s", ExactSpelling = true)]
        public static extern void Normal3(short nx, short ny, short nz);

        [Pure, DllImport(Library, EntryPoint = "glNormal3bv", ExactSpelling = true)]
        public static extern void Normal3(byte* v);
        [Pure, DllImport(Library, EntryPoint = "glNormal3bv", ExactSpelling = true)]
        public static extern void Normal3(sbyte* v);
        [Pure, DllImport(Library, EntryPoint = "glNormal3dv", ExactSpelling = true)]
        public static extern void Normal3(double* v);
        [Pure, DllImport(Library, EntryPoint = "glNormal3fv", ExactSpelling = true)]
        public static extern void Normal3(float* v);
        [Pure, DllImport(Library, EntryPoint = "glNormal3iv", ExactSpelling = true)]
        public static extern void Normal3(int* v);
        [Pure, DllImport(Library, EntryPoint = "glNormal3sv", ExactSpelling = true)]
        public static extern void Normal3(short* v);

        #endregion

        #region P

        [Pure, DllImport(Library, EntryPoint = "glPushMatrix", ExactSpelling = true)]
        public static extern void PushMatrix();

        [Pure, DllImport(Library, EntryPoint = "glPopMatrix", ExactSpelling = true)]
        public static extern void PopMatrix();

        #endregion

        #region R

        [Pure, DllImport(Library, EntryPoint = "glReadBuffer", ExactSpelling = true)]
        public static extern void ReadBuffer(ReadBufferMode src);

        [Pure, DllImport(Library, EntryPoint = "glReadPixels", ExactSpelling = true)]
        public static extern void ReadPixels(int x, int y, int width, int height, PixelFormat format, PixelType type, [Out] IntPtr pixels);


        [Pure, DllImport(Library, EntryPoint = "glRotated", ExactSpelling = true)]
        public static extern void Rotate(double angle, double x, double y, double z);

        [Pure, DllImport(Library, EntryPoint = "glRotatef", ExactSpelling = true)]
        public static extern void Rotate(float angle, float x, float y, float z);

        #endregion

        #region S

        [Pure, DllImport(Library, EntryPoint = "glScaled", ExactSpelling = true)]
        public static extern void Scale(double x, double y, double z);

        [Pure, DllImport(Library, EntryPoint = "glScalef", ExactSpelling = true)]
        public static extern void Scale(float x, float y, float z);

        #endregion

        #region T

        [Pure, DllImport(Library, EntryPoint = "glTexImage1D", ExactSpelling = true)]
        public static extern void TexImage1D(TextureTarget target, int level, PixelInternalFormat internalformat, int width, int border, PixelFormat format, PixelType type, IntPtr pixels);

        [Pure, DllImport(Library, EntryPoint = "glTexImage2D", ExactSpelling = true)]
        public static extern void TexImage2D(TextureTarget target, int level, PixelInternalFormat internalformat, int width, int height, int border, PixelFormat format, PixelType type, IntPtr pixels);

        [Pure, DllImport(Library, EntryPoint = "glTexImage2DMultisample", ExactSpelling = true)]
        public static extern void TexImage2DMultisample(TextureTargetMultisample target, int samples, PixelInternalFormat internalformat, int width, int height, bool fixedsamplelocations);

        [Pure, DllImport(Library, EntryPoint = "glTexImage3D", ExactSpelling = true)]
        public static extern void TexImage3D(TextureTarget target, int level, PixelInternalFormat internalformat, int width, int height, int depth, int border, PixelFormat format, PixelType type, IntPtr pixels);

        [Pure, DllImport(Library, EntryPoint = "glTexImage3DMultisample", ExactSpelling = true)]
        public static extern void TexImage3DMultisample(TextureTargetMultisample target, int samples, PixelInternalFormat internalformat, int width, int height, int depth, bool fixedsamplelocations);


        [Pure, DllImport(Library, EntryPoint = "glTexParameterf", ExactSpelling = true)]
        public static extern void TexParameter(TextureTarget target, TextureParameterName pname, float param);

        [Pure, DllImport(Library, EntryPoint = "glTexParameteri", ExactSpelling = true)]
        public static extern void TexParameter(TextureTarget target, TextureParameterName pname, int param);

        [Pure, DllImport(Library, EntryPoint = "glTexParameterfv", ExactSpelling = true)]
        public static extern void TexParameter(TextureTarget target, TextureParameterName pname, float* @params);

        [Pure, DllImport(Library, EntryPoint = "glTexParameteriv", ExactSpelling = true)]
        public static extern void TexParameter(TextureTarget target, TextureParameterName pname, int* @params);

        [Pure, DllImport(Library, EntryPoint = "glTranslated", ExactSpelling = true)]
        public static extern void Translate(double x, double y, double z);

        [Pure, DllImport(Library, EntryPoint = "glTranslatef", ExactSpelling = true)]
        public static extern void Translate(float x, float y, float z);


        [Pure, DllImport(Library, EntryPoint = "glTexCoord1s", ExactSpelling = true)]
        public static extern void TexCoord1(short s);
        [Pure, DllImport(Library, EntryPoint = "glTexCoord1i", ExactSpelling = true)]
        public static extern void TexCoord1(int s);
        [Pure, DllImport(Library, EntryPoint = "glTexCoord1f", ExactSpelling = true)]
        public static extern void TexCoord1(float s);
        [Pure, DllImport(Library, EntryPoint = "glTexCoord1d", ExactSpelling = true)]
        public static extern void TexCoord1(double s);

        [Pure, DllImport(Library, EntryPoint = "glTexCoord2s", ExactSpelling = true)]
        public static extern void TexCoord2(short s, short t);
        [Pure, DllImport(Library, EntryPoint = "glTexCoord2i", ExactSpelling = true)]
        public static extern void TexCoord2(int s, int t);
        [Pure, DllImport(Library, EntryPoint = "glTexCoord2f", ExactSpelling = true)]
        public static extern void TexCoord2(float s, float t);
        [Pure, DllImport(Library, EntryPoint = "glTexCoord2d", ExactSpelling = true)]
        public static extern void TexCoord2(double s, double t);

        [Pure, DllImport(Library, EntryPoint = "glTexCoord3s", ExactSpelling = true)]
        public static extern void TexCoord3(short s, short t, short r);
        [Pure, DllImport(Library, EntryPoint = "glTexCoord3i", ExactSpelling = true)]
        public static extern void TexCoord3(int s, int t, int r);
        [Pure, DllImport(Library, EntryPoint = "glTexCoord3f", ExactSpelling = true)]
        public static extern void TexCoord3(float s, float t, float r);
        [Pure, DllImport(Library, EntryPoint = "glTexCoord3d", ExactSpelling = true)]
        public static extern void TexCoord3(double s, double t, double r);

        [Pure, DllImport(Library, EntryPoint = "glTexCoord4s", ExactSpelling = true)]
        public static extern void TexCoord4(short s, short t, short r, short q);
        [Pure, DllImport(Library, EntryPoint = "glTexCoord4i", ExactSpelling = true)]
        public static extern void TexCoord4(int s, int t, int r, int q);
        [Pure, DllImport(Library, EntryPoint = "glTexCoord4f", ExactSpelling = true)]
        public static extern void TexCoord4(float s, float t, float r, float q);
        [Pure, DllImport(Library, EntryPoint = "glTexCoord4d", ExactSpelling = true)]
        public static extern void TexCoord4(double s, double t, double r, double q);

        #endregion

        #region V

        [Pure, DllImport(Library, EntryPoint = "glVertex2s", ExactSpelling = true)]
        public static extern void Vertex2(short x, short y);
        [Pure, DllImport(Library, EntryPoint = "glVertex2i", ExactSpelling = true)]
        public static extern void Vertex2(int x, int y);
        [Pure, DllImport(Library, EntryPoint = "glVertex2f", ExactSpelling = true)]
        public static extern void Vertex2(float x, float y);
        [Pure, DllImport(Library, EntryPoint = "glVertex2d", ExactSpelling = true)]
        public static extern void Vertex2(double x, double y);

        [Pure, DllImport(Library, EntryPoint = "glVertex2sv", ExactSpelling = true)]
        public static extern void Vertex2(short* v);
        [Pure, DllImport(Library, EntryPoint = "glVertex2iv", ExactSpelling = true)]
        public static extern void Vertex2(int* v);
        [Pure, DllImport(Library, EntryPoint = "glVertex2fv", ExactSpelling = true)]
        public static extern void Vertex2(float* v);
        [Pure, DllImport(Library, EntryPoint = "glVertex2dv", ExactSpelling = true)]
        public static extern void Vertex2(double* v);


        [Pure, DllImport(Library, EntryPoint = "glVertex3s", ExactSpelling = true)]
        public static extern void Vertex3(short x, short y, short z);
        [Pure, DllImport(Library, EntryPoint = "glVertex3i", ExactSpelling = true)]
        public static extern void Vertex3(int x, int y, int z);
        [Pure, DllImport(Library, EntryPoint = "glVertex3f", ExactSpelling = true)]
        public static extern void Vertex3(float x, float y, float z);
        [Pure, DllImport(Library, EntryPoint = "glVertex3d", ExactSpelling = true)]
        public static extern void Vertex3(double x, double y, double z);

        [Pure, DllImport(Library, EntryPoint = "glVertex3sv", ExactSpelling = true)]
        public static extern void Vertex3(short* v);
        [Pure, DllImport(Library, EntryPoint = "glVertex3iv", ExactSpelling = true)]
        public static extern void Vertex3(int* v);
        [Pure, DllImport(Library, EntryPoint = "glVertex3fv", ExactSpelling = true)]
        public static extern void Vertex3(float* v);
        [Pure, DllImport(Library, EntryPoint = "glVertex3dv", ExactSpelling = true)]
        public static extern void Vertex3(double* v);


        [Pure, DllImport(Library, EntryPoint = "glVertex3s", ExactSpelling = true)]
        public static extern void Vertex4(short x, short y, short z, short w);
        [Pure, DllImport(Library, EntryPoint = "glVertex3i", ExactSpelling = true)]
        public static extern void Vertex4(int x, int y, int z, int w);
        [Pure, DllImport(Library, EntryPoint = "glVertex3f", ExactSpelling = true)]
        public static extern void Vertex4(float x, float y, float z, float w);
        [Pure, DllImport(Library, EntryPoint = "glVertex3d", ExactSpelling = true)]
        public static extern void Vertex4(double x, double y, double z, double w);

        [Pure, DllImport(Library, EntryPoint = "glVertex3sv", ExactSpelling = true)]
        public static extern void Vertex4(short* v);
        [Pure, DllImport(Library, EntryPoint = "glVertex3iv", ExactSpelling = true)]
        public static extern void Vertex4(int* v);
        [Pure, DllImport(Library, EntryPoint = "glVertex3fv", ExactSpelling = true)]
        public static extern void Vertex4(float* v);
        [Pure, DllImport(Library, EntryPoint = "glVertex3dv", ExactSpelling = true)]
        public static extern void Vertex4(double* v);

        #endregion
    }
}
