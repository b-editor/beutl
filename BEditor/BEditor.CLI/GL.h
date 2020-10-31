#pragma once

#include <glm/glm.hpp>
#include <glm/gtc/matrix_transform.hpp>

using namespace System;
using namespace System::Runtime::InteropServices;
using namespace BEditor::CLI::Media;
using namespace BEditor::CLI::Extensions;

namespace BEditor {
	namespace CLI {
		namespace Graphics {
			public ref class GL abstract sealed {
			public:
				static void ReadBuffer(ReadBufferMode mode) {
					glReadBuffer((uint)mode);
				}
				static void ReadPixels(int x, int y, int width, int height, PixelFormat format, PixelType type, IntPtr pixels) {
					glReadPixels(x, y, width, height, (uint)format, (uint)type, (void*)pixels);
				}

				static void Enable(uint cap) {
					glEnable(cap);
				}
				static void Enable(EnableCap cap) {
					glEnable((uint)cap);
				}
				static void Disable(uint cap) {
					glDisable(cap);
				}
				static void Disable(EnableCap cap) {
					glDisable((uint)cap);
				}
				static bool IsEnabled(uint cap) {
					return glIsEnabled(cap) == GL_TRUE;
				}
				static bool IsEnabled(EnableCap cap) {
					return glIsEnabled((uint)cap) == GL_TRUE;
				}
				
				#pragma region Getter

				static bool GetBoolean(GetPName pname) {
					GLboolean result;
					glGetBooleanv((uint)pname, &result);

					return result == GL_TRUE;
				}
				static bool GetBoolean(uint pname) {
					GLboolean result;
					glGetBooleanv(pname, &result);

					return result == GL_TRUE;
				}

				static double GetDouble(GetPName pname) {
					double result;
					glGetDoublev((uint)pname, &result);

					return result;
				}
				static double GetDouble(uint pname) {
					double result;
					glGetDoublev(pname, &result);

					return result;
				}

				static float GetFloat(GetPName pname) {
					float result;
					glGetFloatv((uint)pname, &result);

					return result;
				}
				static float GetFloat(uint pname) {
					float result;
					glGetFloatv(pname, &result);

					return result;
				}

				static void GetLight(LightName light, LightParameter pname, [Out] float% params) {
					float result;
					glGetLightfv((uint)light, (uint)pname, &result);

					params = result;
				}
				static void GetLight(LightName light, LightParameter pname, array<float>^ params) {
					pin_ptr<float> pin = &params[0];
					glGetLightfv((uint)light, (uint)pname, pin);

					pin = nullptr;
				}
				static void GetLight(LightName light, LightParameter pname, float* params) {
					glGetLightfv((uint)light, (uint)pname, params);
				}
				static void GetLight(LightName light, LightParameter pname, [Out] int% params) {
					int result;
					glGetLightiv((uint)light, (uint)pname, &result);

					params = result;
				}
				static void GetLight(LightName light, LightParameter pname, array<int>^ params) {
					pin_ptr<int> pin = &params[0];
					glGetLightiv((uint)light, (uint)pname, pin);

					pin = nullptr;
				}
				static void GetLight(LightName light, LightParameter pname, int* params) {
					glGetLightiv((uint)light, (uint)pname, params);
				}
				static void GetLight(uint light, uint pname, [Out] float% params) {
					float result;
					glGetLightfv(light, pname, &result);

					params = result;
				}
				static void GetLight(uint light, uint pname, array<float>^ params) {
					pin_ptr<float> pin = &params[0];
					glGetLightfv(light, pname, pin);

					pin = nullptr;
				}
				static void GetLight(uint light, uint pname, float* params) {
					glGetLightfv(light, pname, params);
				}
				static void GetLight(uint light, uint pname, [Out] int% params) {
					int result;
					glGetLightiv(light, pname, &result);

					params = result;
				}
				static void GetLight(uint light, uint pname, array<int>^ params) {
					pin_ptr<int> pin = &params[0];
					glGetLightiv(light, pname, pin);

					pin = nullptr;
				}
				static void GetLight(uint light, uint pname, int* params) {
					glGetLightiv(light, pname, params);
				}

				static void GetMaterial(MaterialFace face, MaterialParameter pname, [Out] int% params) {
					int result;
					glGetMaterialiv((uint)face, (uint)pname, &result);

					params = result;
				}
				static void GetMaterial(MaterialFace face, MaterialParameter pname, array<int>^ params) {
					pin_ptr<int> pin = &params[0];
					glGetMaterialiv((uint)face, (uint)pname, pin);

					pin = nullptr;
				}
				static void GetMaterial(MaterialFace face, MaterialParameter pname, int* params) {
					glGetMaterialiv((uint)face, (uint)pname, params);
				}
				static void GetMaterial(MaterialFace face, MaterialParameter pname, [Out] float% params) {
					float result;
					glGetMaterialfv((uint)face, (uint)pname, &result);

					params = result;
				}
				static void GetMaterial(MaterialFace face, MaterialParameter pname, array<float>^ params) {
					pin_ptr<float> pin = &params[0];
					glGetMaterialfv((uint)face, (uint)pname, pin);

					pin = nullptr;
				}
				static void GetMaterial(MaterialFace face, MaterialParameter pname, float* params) {
					glGetMaterialfv((uint)face, (uint)pname, params);
				}
				static void GetMaterial(uint face, uint pname, [Out] int% params) {
					int result;
					glGetMaterialiv(face, pname, &result);

					params = result;
				}
				static void GetMaterial(uint face, uint pname, array<int>^ params) {
					pin_ptr<int> pin = &params[0];
					glGetMaterialiv(face, pname, pin);

					pin = nullptr;
				}
				static void GetMaterial(uint face, uint pname, int* params) {
					glGetMaterialiv(face, pname, params);
				}
				static void GetMaterial(uint face, uint pname, [Out] float% params) {
					float result;
					glGetMaterialfv(face, pname, &result);

					params = result;
				}
				static void GetMaterial(uint face, uint pname, array<float>^ params) {
					pin_ptr<float> pin = &params[0];
					glGetMaterialfv(face, pname, pin);

					pin = nullptr;
				}
				static void GetMaterial(uint face, uint pname, float* params) {
					glGetMaterialfv(face, pname, params);
				}

				static void GetTexImage(TextureTarget target, int level, PixelFormat format, PixelType type, IntPtr pixels) {
					glGetTexImage((uint)target, level, (uint)format, (uint)type, pixels.ToPointer());
				}
				static void GetTexImage(uint target, int level, uint format, uint type, IntPtr pixels) {
					glGetTexImage(target, level, format, type, pixels.ToPointer());
				}

				#pragma endregion

				#pragma region Texture
				static void GenTexture(int n, [Out] int% textures) {
					uint tex;
					glGenTextures(n, &tex);

					textures = tex;
				}
				static void GenTextures(int n, int* textures) {
					glGenTextures(n, (uint*)textures);
				}
				static void GenTextures(int n, array<int>^ textures) {
					pin_ptr<int> pined = &textures[0];
					glGenTextures(n, (uint*)pined);

					pined = nullptr;
				}
				static void GenTexture(int n, [Out] uint% textures) {
					uint tex;
					glGenTextures(n, &tex);

					textures = tex;
				}
				static void GenTextures(int n, uint* textures) {
					glGenTextures(n, textures);
				}
				static void GenTextures(int n, array<uint>^ textures) {
					pin_ptr<uint> pined = &textures[0];
					glGenTextures(n, pined);

					pined = nullptr;
				}

				static void TexParameters(TextureTarget target, TextureParameterName pname, float* params) {
					glTexParameterfv((uint)target, (uint)pname, params);
				}
				static void TexParameters(TextureTarget target, TextureParameterName pname, array<float>^ params) {
					pin_ptr<float> pined = &params[0];

					glTexParameterfv((uint)target, (uint)pname, pined);
					pined = nullptr;
				}
				static void TexParameter(TextureTarget target, TextureParameterName pname, float param) {
					glTexParameterf((uint)target, (uint)pname, param);
				}
				static void TexParameters(TextureTarget target, TextureParameterName pname, int* params) {
					glTexParameteriv((uint)target, (uint)pname, params);
				}
				static void TexParameters(TextureTarget target, TextureParameterName pname, array<int>^ params) {
					pin_ptr<int> pined = &params[0];

					glTexParameteriv((uint)target, (uint)pname, pined);
					pined = nullptr;
				}
				static void TexParameter(TextureTarget target, TextureParameterName pname, int param) {
					glTexParameteri((uint)target, (uint)pname, param);
				}

				static void BindTexture(TextureTarget target, int texture) {
					glBindTexture((uint)target, texture);
				}
				static void BindTexture(TextureTarget target, uint texture) {
					glBindTexture((uint)target, texture);
				}
				#if 0

				static void BindTextures(uint first, int count, int* textures) {
					glBindTextures(first, count, (uint*)textures);
				}
				static void BindTextures(uint first, int count, uint* textures) {
					glBindTextures(first, count, textures);
				}
				static void BindTextures(uint first, int count, array<int>^ textures) {
					pin_ptr<int> pin = &textures[0];
					glBindTextures(first, count, (uint*)pin);

					pin = nullptr;
				}
				static void BindTextures(uint first, int count, array<uint>^ textures) {
					pin_ptr<uint> pin = &textures[0];
					glBindTextures(first, count, pin);

					pin = nullptr;
				}

				#endif // 0

				static void TexImage1D(TextureTarget target, int level, PixelInternalFormat internalformat, int width, int border, PixelFormat format, PixelType type, IntPtr pixels) {
					glTexImage1D(
						(uint)target,
						level,
						(uint)internalformat,
						width,
						border,
						(uint)format,
						(uint)type,
						pixels.ToPointer());
				}
				static void TexSubImage1D(TextureTarget target, int level, int xoffset, int width, PixelFormat format, PixelType type, IntPtr pixels) {
					glTexSubImage1D((uint)target,
						level,
						xoffset,
						width,
						(uint)format,
						(uint)type,
						pixels.ToPointer());
				}
				static void TexImage2D(TextureTarget target, int level, PixelInternalFormat internalformat, int width, int height, int border, PixelFormat format, PixelType type, IntPtr pixels) {
					glTexImage2D(
						(uint)target,
						level,
						(uint)internalformat,
						width,
						height,
						border,
						(uint)format,
						(uint)type,
						pixels.ToPointer());
				}
				static void TexSubImage2D(TextureTarget target, int level, int xoffset, int yoffset, int width, int height, PixelFormat format, PixelType type, IntPtr pixels) {
					glTexSubImage2D((uint)target,
						level,
						xoffset,
						yoffset,
						width,
						height,
						(uint)format,
						(uint)type,
						pixels.ToPointer());
				}
				
				#if 0

				static void TexImage3D(TextureTarget target, int level, PixelInternalFormat internalformat, int width, int height, int depth, int border, PixelFormat format, PixelType type, IntPtr pixels) {
					glTexImage3D(
						(uint)target,
						level,
						(uint)internalformat,
						width,
						height,
						depth,
						border,
						(uint)format,
						(uint)type,
						pixels.ToPointer());
				}
				static void TexSubImage3D(TextureTarget target, int level, int xoffset, int yoffset, int zoffset, int width, int height, int depth, PixelFormat format, PixelType type, IntPtr pixels) {
					glTexSubImage3D((uint)target,
						level,
						xoffset,
						yoffset,
						zoffset,
						width,
						height,
						depth,
						(uint)format,
						(uint)type,
						pixels.ToPointer());
				}

				#endif // 0

				static void DeleteTexture(uint texture) {
					uint* tex = new uint[] { texture };

					glDeleteTextures(1, tex);

					delete tex;
				}
				static void DeleteTexture(int texture) {
					uint* tex = new uint[] { (uint)texture };

					glDeleteTextures(1, tex);

					delete tex;
				}
				static void DeleteTextures(int n, uint* textures) {
					glDeleteTextures(n, textures);
				}
				static void DeleteTextures(int n, int* textures) {
					glDeleteTextures(n, (uint*)textures);
				}
				static void DeleteTextures(int n, array<uint>^ textures) {
					pin_ptr<uint> tex = &textures[0];
					glDeleteTextures(n, tex);

					tex = nullptr;
				}
				static void DeleteTextures(int n, array<int>^ textures) {
					pin_ptr<int> tex = &textures[0];
					glDeleteTextures(n, (uint*)tex);

					tex = nullptr;
				}
				#pragma endregion

				#pragma region Mipmap

				static void GenerateMipmap(GenerateMipmapTarget target) {
					glGenerateMipmap((uint)target);
				}

				#pragma endregion

				#pragma region Blend

				static void BlendEquation(BlendEquationMode mode) {
					glBlendEquation((uint)mode);
				}
				static void BlendEquation(uint buf, BlendEquationMode mode) {
					glBlendEquationi(buf, (uint)mode);
				}
				static void BlendEquation(int buf, BlendEquationMode mode) {
					glBlendEquationi(buf, (uint)mode);
				}

				static void BlendEquationSeparate(BlendEquationMode modeRGB, BlendEquationMode modeAlpha) {
					glBlendEquationSeparate((uint)modeRGB, (uint)modeAlpha);
				}
				static void BlendEquationSeparate(uint buf, BlendEquationMode modeRGB, BlendEquationMode modeAlpha) {
					glBlendEquationSeparatei(buf, (uint)modeRGB, (uint)modeAlpha);
				}
				static void BlendEquationSeparate(int buf, BlendEquationMode modeRGB, BlendEquationMode modeAlpha) {
					glBlendEquationSeparatei(buf, (uint)modeRGB, (uint)modeAlpha);
				}

				static void BlendFunc(int buf, BlendingFactorSrc src, BlendingFactorDest dst) {
					glBlendFunci(buf, (uint)src, (uint)dst);
				}
				static void BlendFunc(uint buf, BlendingFactorSrc src, BlendingFactorDest dst) {
					glBlendFunci(buf, (uint)src, (uint)dst);
				}
				static void BlendFunc(BlendingFactor sfactor, BlendingFactor dfactor) {
					glBlendFunc((uint)sfactor, (uint)dfactor);
				}

				static void BlendFuncSeparate(BlendingFactorSrc sfactorRGB, BlendingFactorDest dfactorRGB, BlendingFactorSrc sfactorAlpha, BlendingFactorDest dfactorAlpha) {
					glBlendFuncSeparate((uint)sfactorRGB, (uint)dfactorRGB, (uint)sfactorAlpha, (uint)dfactorAlpha);
				}
				static void BlendFuncSeparate(uint buf, BlendingFactorSrc srcRGB, BlendingFactorDest dstRGB, BlendingFactorSrc srcAlpha, BlendingFactorDest dstAlpha) {
					glBlendFuncSeparatei(buf, (uint)srcRGB, (uint)dstRGB, (uint)srcAlpha, (uint)dstAlpha);
				}
				static void BlendFuncSeparate(int buf, BlendingFactorSrc srcRGB, BlendingFactorDest dstRGB, BlendingFactorSrc srcAlpha, BlendingFactorDest dstAlpha) {
					glBlendFuncSeparatei(buf, (uint)srcRGB, (uint)dstRGB, (uint)srcAlpha, (uint)dstAlpha);
				}

				#pragma endregion

				static void PushMatrix() {
					glPushMatrix();
				}
				static void PopMatrix() {
					glPopMatrix();
				}

				static void Translate(Point3 trans) {
					glTranslatef(trans.X, trans.Y, trans.Z);
				}
				static void Translate(double x, double y, double z) {
					glTranslated(x, y, z);
				}
				static void Translate(float x, float y, float z) {
					glTranslatef(x, y, z);
				}

				#pragma region Rotate

				static void Rotate(double angle, double x, double y, double z) {
					glRotated(angle, x, y, z);
				}
				static void Rotate(float angle, float x, float y, float z) {
					glRotatef(angle, x, y, z);
				}
				static void RotateX(double angle) {
					glRotatef(angle, 1, 0, 0);
				}
				static void RotateY(double angle) {
					glRotatef(angle, 0, 1, 0);
				}
				static void RotateZ(double angle) {
					glRotatef(angle, 0, 0, 1);
				}
				static void RotateX(float angle) {
					glRotatef(angle, 1, 0, 0);
				}
				static void RotateY(float angle) {
					glRotatef(angle, 0, 1, 0);
				}
				static void RotateZ(float angle) {
					glRotatef(angle, 0, 0, 1);
				}

				#pragma endregion

				static void Scale(float x, float y, float z) {
					glScalef(x, y, z);
				}
				static void Scale(double x, double y, double z) {
					glScaled(x, y, z);
				}

				#pragma region Color4

				static void Color4(Color color) {
					glColor4b(color.R, color.G, color.B, color.A);
				}
				static void Color4(Byte r, Byte g, Byte b, Byte a) {
					glColor4b(r, g, b, a);
				}
				static void Color4(float r, float g, float b, float a) {
					glColor4f(r, g, b, a);
				}
				static void Color4(array<float>^ v) {
					pin_ptr<float> pin = &v[0];
					glColor4fv(pin);
					pin = nullptr;
				}
				static void Color4(array<SByte>^ v) {
					pin_ptr<SByte> pin = &v[0];
					glColor4bv(pin);
					pin = nullptr;
				}
				static void Color4(SByte* v) {
					glColor4bv(v);
				}
				static void Color4(float* v) {
					glColor4fv(v);
				}
				static void Color3(Color color) {
					glColor3b(color.R, color.G, color.B);
				}
				static void Color3(Byte r, Byte g, Byte b) {
					glColor3b(r, g, b);
				}
				static void Color3(float r, float g, float b) {
					glColor3f(r, g, b);
				}
				static void Color3(array<float>^ v) {
					pin_ptr<float> pin = &v[0];
					glColor3fv(pin);
					pin = nullptr;
				}
				static void Color3(array<SByte>^ v) {
					pin_ptr<SByte> pin = &v[0];
					glColor3bv(pin);
					pin = nullptr;
				}
				static void Color3(SByte* v) {
					glColor3bv(v);
				}
				static void Color3(float* v) {
					glColor3fv(v);
				}

				#pragma endregion

				#pragma region Material

				static void Material(MaterialFace face, MaterialParameter pname, Color params) {
					float* params_ = new float[4] { params.ScR, params.ScG, params.ScB, params.ScA };

					glMaterialfv((uint)face, (uint)pname, params_);

					delete[] params_;
				}
				static void Material(MaterialFace face, MaterialParameter pname, array<float>^ params) {
					pin_ptr<float> pined = &params[0];

					glMaterialfv((uint)face, (uint)pname, pined);
					pined = nullptr;
				}
				static void Material(MaterialFace face, MaterialParameter pname, array<int>^ params) {
					pin_ptr<int> pined = &params[0];

					glMaterialiv((uint)face, (uint)pname, pined);

					pined = nullptr;
				}
				static void Material(MaterialFace face, MaterialParameter pname, float* params) {
					glMaterialfv((uint)face, (uint)pname, params);
				}
				static void Material(MaterialFace face, MaterialParameter pname, int* params) {
					glMaterialiv((uint)face, (uint)pname, params);
				}
				static void Material(MaterialFace face, MaterialParameter pname, float param) {
					glMaterialf((uint)face, (uint)pname, param);
				}
				static void Material(MaterialFace face, MaterialParameter pname, int param) {
					glMateriali((uint)face, (uint)pname, param);
				}
				static void Material(uint face, uint pname, Color params) {
					float* params_ = new float[4] { params.ScR, params.ScG, params.ScB, params.ScA };

					glMaterialfv((uint)face, (uint)pname, params_);

					delete[] params_;
				}
				static void Material(uint face, uint pname, array<float>^ params) {
					pin_ptr<float> pined = &params[0];

					glMaterialfv(face, pname, pined);
					pined = nullptr;
				}
				static void Material(uint face, uint pname, array<int>^ params) {
					pin_ptr<int> pined = &params[0];

					glMaterialiv(face, pname, pined);

					pined = nullptr;
				}
				static void Material(uint face, uint pname, float* params) {
					glMaterialfv(face, pname, params);
				}
				static void Material(uint face, uint pname, int* params) {
					glMaterialiv(face, pname, params);
				}
				static void Material(uint face, uint pname, float param) {
					glMaterialf(face, pname, param);
				}
				static void Material(uint face, uint pname, int param) {
					glMateriali(face, pname, param);
				}

				#pragma endregion

				static void Begin(PrimitiveType mode) {
					glBegin((uint)mode);
				}
				static void Begin(uint mode) {
					glBegin(mode);
				}
				static void End() {
					glEnd();
				}

				#pragma region TexCoord

				static void TexCoord1(int s) {
					glTexCoord1i(s);
				}
				static void TexCoord1(float s) {
					glTexCoord1f(s);
				}
				static void TexCoord1(double s) {
					glTexCoord1d(s);
				}
				static void TexCoord1(short s) {
					glTexCoord1s(s);
				}

				static void TexCoord2(int s, int t) {
					glTexCoord2i(s, t);
				}
				static void TexCoord2(float s, float t) {
					glTexCoord2f(s, t);
				}
				static void TexCoord2(double s, double t) {
					glTexCoord2d(s, t);
				}
				static void TexCoord2(short s, short t) {
					glTexCoord2s(s, t);
				}

				static void TexCoord3(int s, int t, int r) {
					glTexCoord3i(s, t, r);
				}
				static void TexCoord3(float s, float t, float r) {
					glTexCoord3f(s, t, r);
				}
				static void TexCoord3(double s, double t, double r) {
					glTexCoord3d(s, t, r);
				}
				static void TexCoord3(short s, short t, short r) {
					glTexCoord3s(s, t, r);
				}

				#pragma endregion

				#pragma region Vertex

				static void Vertex2(int x, int y) {
					glVertex2i(x, y);
				}
				static void Vertex2(float x, float y) {
					glVertex2f(x, y);
				}
				static void Vertex2(double x, double y) {
					glVertex2d(x, y);
				}
				static void Vertex2(short x, short y) {
					glVertex2s(x, y);
				}
				static void Vertex2(Point2 point) {
					glVertex2f(point.X, point.Y);
				}

				static void Vertex3(int x, int y, int z) {
					glVertex3i(x, y, z);
				}
				static void Vertex3(float x, float y, float z) {
					glVertex3f(x, y, z);
				}
				static void Vertex3(double x, double y, double z) {
					glVertex3d(x, y, z);
				}
				static void Vertex3(short x, short y, short z) {
					glVertex3s(x, y, z);
				}
				static void Vertex3(Point3 point) {
					glVertex3f(point.X, point.Y, point.Z);
				}

				static void Vertex4(int x, int y, int z, int w) {
					glVertex4i(x, y, z, w);
				}
				static void Vertex4(float x, float y, float z, float w) {
					glVertex4f(x, y, z, w);
				}
				static void Vertex4(double x, double y, double z, double w) {
					glVertex4d(x, y, z, w);
				}
				static void Vertex4(short x, short y, short z, short w) {
					glVertex4s(x, y, z, w);
				}

				#pragma endregion

				#pragma region Normal

				static void Normal3(float nx, float ny, float nz) {
					glNormal3f(nx, ny, nz);
				}
				static void Normal3(short nx, short ny, short nz) {
					glNormal3s(nx, ny, nz);
				}
				static void Normal3(int nx, int ny, int nz) {
					glNormal3i(nx, ny, nz);
				}
				static void Normal3(double nx, double ny, double nz) {
					glNormal3d(nx, ny, nz);
				}
				static void Normal3(Byte nx, Byte ny, Byte nz) {
					glNormal3b(nx, ny, nz);
				}
				static void Normal3(float* v) {
					glNormal3fv(v);
				}
				static void Normal3(short* v) {
					glNormal3sv(v);
				}
				static void Normal3(int* v) {
					glNormal3iv(v);
				}
				static void Normal3(double* v) {
					glNormal3dv(v);
				}
				static void Normal3(SByte* v) {
					glNormal3bv(v);
				}
				static void Normal3(array<float>^ v) {
					pin_ptr<float> pin = &v[0];
					glNormal3fv(pin);
					pin = nullptr;
				}
				static void Normal3(array<short>^ v) {
					pin_ptr<short> pin = &v[0];
					glNormal3sv(pin);
					pin = nullptr;
				}
				static void Normal3(array<int>^ v) {
					pin_ptr<int> pin = &v[0];
					glNormal3iv(pin);
					pin = nullptr;
				}
				static void Normal3(array<double>^ v) {
					pin_ptr<double> pin = &v[0];
					glNormal3dv(pin);
					pin = nullptr;
				}
				static void Normal3(array<SByte>^ v) {
					pin_ptr<SByte> pin = &v[0];
					glNormal3bv(pin);
					pin = nullptr;
				}

				#pragma endregion

				static void MatrixMode(MatrixMode mode) {
					glMatrixMode((uint)mode);
				}
				static void LoadMatrix(double* m) {
					glLoadMatrixd(m);
				}
				static void LoadMatrix(array<double>^ m) {
					pin_ptr<double> pin = &m[0];
					glLoadMatrixd(pin);
					pin = nullptr;
				}
				static void LoadMatrix(float* m) {
					glLoadMatrixf(m);
				}
				static void LoadMatrix(array<float>^ m) {
					pin_ptr<float> pin = &m[0];
					glLoadMatrixf(pin);
					pin = nullptr;
				}
				static void LoadIdentity() {
					glLoadIdentity();
				}

				ref class Utility abstract sealed {
				public:
					static void Perspective(double fovy, double aspect, double zNear, double zFar) {
						glm::mat4 matrix = glm::perspective(glm::radians(fovy), aspect, zNear, zFar);

						glLoadMatrixf(&matrix[0][0]);
					}
					static void LookAt(double eyeX, double eyeY, double eyeZ, double targetX, double targetY, double targetZ, double upX, double upY, double upZ) {
						glm::mat4 matrix = glm::lookAt(
							glm::vec3(eyeX, eyeY, eyeZ),
							glm::vec3(targetX, targetY, targetZ),
							glm::vec3(upX, upY, upZ));

						glLoadMatrixf(&matrix[0][0]);
					}

					static void Orthographic(float width, float height, float zNear, float zFar) {
						width /= 2;
						height /= 2;
						
						glm::mat4 matrix = glm::ortho(-width, width, -height, height, zNear, zFar);
						glLoadMatrixf(&matrix[0][0]);
					}
					static void Orthographic(double width, double height, double zNear, double zFar) {
						width /= 2;
						height /= 2;

						glm::mat4 matrix = glm::ortho(-width, width, -height, height, zNear, zFar);
						glLoadMatrixf(&matrix[0][0]);
					}
					static void Orthographic(float left, float right, float bottom, float top, float zNear, float zFar) {
						glm::mat4 matrix = glm::ortho(left, right, bottom, top, zNear, zFar);
						glLoadMatrixf(&matrix[0][0]);
					}
					static void Orthographic(double left, double right, double bottom, double top, double zNear, double zFar) {
						glm::mat4 matrix = glm::ortho(left, right, bottom, top, zNear, zFar);
						glLoadMatrixf(&matrix[0][0]);
					}
				};

				ref class ToolKit abstract sealed {
				public:
					static void GetPixels(Image^ image);
					static void BindTexture(Image^ image, [Out] int% texture);
					static void Paint(Point3 coord, double nx, double ny, double nz, Point3 center, Action^ draw);
					static void Paint(Point3 coord, double nx, double ny, double nz, Point3 center, Action^ draw, Action^ blendFunc);

					static void DrawImage(Image^ image, double scaleX, double scaleY, double scaleZ);
					static void DrawImage(Image^ image, Color color, Color ambient, Color diffuse, Color specular, float shininess);
					static void DrawImage(Image^ image, double scaleX, double scaleY, double scaleZ, Color color, Color ambient, Color diffuse, Color specular, float shininess);

					static void LockAt(int width, int height, float x, float y, float z, float targetX, float targetY, float targetZ, float zNear, float zFar, float fov, bool perspective);
				};
			};
		}
	}
}