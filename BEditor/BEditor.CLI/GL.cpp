#include "pch.h"
#include <GL\glew.h>
#include <GLFW\glfw3.h>

using namespace System;
using namespace BEditor::CLI::Media;
using namespace BEditor::CLI::Graphics;
using namespace BEditor::CLI::Extensions;

inline void GL::ToolKit::GetPixels(Image^ image) {
	if (image == nullptr) throw gcnew ArgumentNullException("image");
	image->ThrowIfDisposed();

	ImageType^ type = image->Type;
	cv::Mat* mat = image->Ptr;

	glReadBuffer(GL_FRONT);
	glReadPixels(
		0,
		0,
		mat->cols,
		mat->rows,
		TypeConverter::ToPixelFormat(type),
		TypeConverter::ToPixelType(type),
		mat->data);

	cv::flip(*mat, *mat, 0);
}
inline void GL::ToolKit::BindTexture(Image^ image, [Out] int% texture) {
	if (image == nullptr) throw gcnew ArgumentNullException("image");
	image->ThrowIfDisposed();

	cv::Mat* mat = image->Ptr;
	ImageType^ type = image->Type;

	uint tex;
	glGenTextures(1, &tex);
	glBindTexture(GL_TEXTURE_2D, tex);

	glTexImage2D(GL_TEXTURE_2D, 0, TypeConverter::ToPixelInternal(type), mat->cols, mat->rows, 0,
		TypeConverter::ToPixelFormat(type), TypeConverter::ToPixelType(type), mat->data);

	glTexParameteri(GL_TEXTURE_2D, GL_TEXTURE_MIN_FILTER, GL_LINEAR);
	glTexParameteri(GL_TEXTURE_2D, GL_TEXTURE_MAG_FILTER, GL_LINEAR);
	glTexParameteri(GL_TEXTURE_2D, GL_TEXTURE_WRAP_S, GL_REPEAT);
	glTexParameteri(GL_TEXTURE_2D, GL_TEXTURE_WRAP_T, GL_REPEAT);

	glGenerateMipmap(GL_TEXTURE_2D);
}
inline void GL::ToolKit::Paint(Point3 coord, double nx, double ny, double nz, Point3 center, Action^ draw) {
	glEnable(GL_BLEND);

	glBlendEquationSeparate(GL_FUNC_ADD, GL_FUNC_ADD);
	glBlendFunc(GL_SRC_ALPHA, GL_ONE_MINUS_SRC_ALPHA);

	glPushMatrix(); {
		glTranslatef(coord.X, coord.Y, coord.Z);

		glPushMatrix(); {
			glRotated(nx, 1, 0, 0);
			glRotated(ny, 0, 1, 0);
			glRotated(nz, 0, 0, 1);
		
			glPushMatrix(); {
				glTranslatef(center.X, center.Y, center.Z);

				draw();
			}
			glPopMatrix();
		}
		glPopMatrix();
	}
	glPopMatrix();
}
inline void GL::ToolKit::Paint(Point3 coord, double nx, double ny, double nz, Point3 center, Action^ draw, Action^ blendfunc) {
	glEnable(GL_BLEND);

	blendfunc();

	glPushMatrix(); {
		glTranslatef(coord.X, coord.Y, coord.Z);

		glPushMatrix(); {
			glRotated(nx, 1, 0, 0);
			glRotated(ny, 0, 1, 0);
			glRotated(nz, 0, 0, 1);
		
			glPushMatrix(); {
				glTranslatef(center.X, center.Y, center.Z);

				draw();
			}
			glPopMatrix();
		}
		glPopMatrix();
	}
	glPopMatrix();
}