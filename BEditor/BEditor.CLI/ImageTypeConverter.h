#pragma once

#include <GL\glew.h>
#include <GLFW\glfw3.h>
#include <exception>

using namespace System;
using namespace BEditor::CLI::Media;
using namespace BEditor::CLI::Graphics;

namespace BEditor {
	namespace CLI {
		namespace Extensions {
			public class TypeConverter {
			public:
				static GLenum ToPixelType(ImageType^ type) {
                    GLenum s = GL_BITMAP;
                    switch (type->Depth) {
                        case ImageType::Byte:
                            s = GL_UNSIGNED_BYTE;
                            break;
                        case ImageType::Char:
                            s = GL_BYTE;
                            break;
                        case ImageType::UShort:
                            s = GL_UNSIGNED_SHORT;
                            break;
                        case ImageType::Short:
                            s = GL_SHORT;
                            break;
                        case ImageType::Int:
                            s = GL_INT;
                            break;
                        case ImageType::Float:
                            s = GL_FLOAT;
                            break;
                        case ImageType::Double:
                            break;
                        case ImageType::UsrType1:
                            break;
                        default:
                            break;
                    }

                    return s;
				}
                static GLenum ToPixelFormat(ImageType^ type) {
                    switch (type->Channels) {
                        case 1:
                            return GL_RED;
                            break;
                        case 2:
                            return GL_RG;
                            break;
                        case 3:
                            return GL_BGR;
                            break;
                        case 4:
                            return GL_BGRA;
                            break;
                        default:
                            throw std::exception();
                            break;
                    }
                }
                static GLenum ToPixelInternal(ImageType^ type) {
                    switch (type->Channels) {
                        case 1:
                            return GL_ONE;
                            break;
                        case 2:
                            return GL_RG8;
                            break;
                        case 3:
                            return GL_RGB;
                            break;
                        case 4:
                            return GL_RGBA;
                            break;
                        default:
                            throw gcnew Exception();
                            break;
                    }
                }
			};
		}
	}
}