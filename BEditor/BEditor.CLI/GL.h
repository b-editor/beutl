#pragma once

#include <GL\glew.h>
#include <GLFW\glfw3.h>
#include "ReadBufferMode.h"
#include "PixelFormat.h"
#include "PixelType.h"
#include "GLEnums.h"

using namespace System;
using namespace System::Runtime::InteropServices;

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
				static void GenTextures(int n, [Out] int% textures) {
					uint* tex;
					glGenTextures(n, tex);

					textures = tex[0];

					delete tex;
				}
				static void GenTextures(int n, int* textures) {
					glGenTextures(n, (uint*)textures);
				}
				static void GenTextures(int n, array<int>^ textures) {
					uint* tex;
					glGenTextures(n, tex);

					Marshal::Copy(IntPtr(tex), textures, 0, textures->Length);

					delete tex;
				}
				static void TexParameter() {

				}
			};
		}
	}
}