#include "pch.h"

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

	//glBlendEquationSeparate(GL_FUNC_ADD, GL_FUNC_ADD);

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
inline void GL::ToolKit::Paint(Point3 coord, double nx, double ny, double nz, Point3 center, Action^ draw, Action^ blendFunc) {
	glEnable(GL_BLEND);

	blendFunc();

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

inline void GL::ToolKit::DrawImage(Image^ image, double scaleX, double scaleY, double scaleZ) {
	if (image == nullptr) throw gcnew ArgumentNullException("image");
	image->ThrowIfDisposed();

	int id;
	GL::ToolKit::BindTexture(image, id);

	float* white = new float[] { 1, 1, 1, 1 };
	glColor4fv(white);
	glMaterialfv(GL_FRONT, GL_AMBIENT, white);
	glMaterialfv(GL_FRONT, GL_DIFFUSE, white);
	glMaterialfv(GL_FRONT, GL_SPECULAR, white);
	glMaterialf(GL_FRONT, GL_SHININESS, 10);

	delete[] white;

	glEnable(GL_TEXTURE_2D);

	int w = image->Ptr->cols / 2;
	int h = image->Ptr->rows / 2;

	glBindTexture(GL_TEXTURE_2D, id);

	glScaled(scaleX, scaleY, scaleZ);
	glBegin(GL_QUADS); {
		//右下
		glTexCoord2f(1, 1);
		glVertex3f(w, -h, 0);
		
		//左下
		glTexCoord2f(0, 1);
		glVertex3f(-w, -h, 0);

		//左上
		glTexCoord2f(0, 0);
		glVertex3f(-w, h, 0);

		//右上
		glTexCoord2f(1, 0);
		glVertex3f(w, h, 0);
	}
	glEnd();

	glDisable(GL_TEXTURE_2D);
	glDisable(GL_BLEND);

	GL::DeleteTexture(id);
}
inline void GL::ToolKit::DrawImage(Image^ image, Color color, Color ambient, Color diffuse, Color specular, float shininess) {
	if (image == nullptr) throw gcnew ArgumentNullException("image");
	image->ThrowIfDisposed();

	int id;
	GL::ToolKit::BindTexture(image, id);

	GL::Color4(color);
	GL::Material(GL_FRONT, GL_AMBIENT, ambient);
	GL::Material(GL_FRONT, GL_DIFFUSE, diffuse);
	GL::Material(GL_FRONT, GL_SPECULAR, specular);
	GL::Material(GL_FRONT, GL_SHININESS, shininess);

	glEnable(GL_TEXTURE_2D);

	int w = image->Ptr->cols / 2;
	int h = image->Ptr->rows / 2;

	glBindTexture(GL_TEXTURE_2D, id);

	glBegin(GL_QUADS); {
		//右下
		glTexCoord2f(1, 1);
		glVertex3f(w, -h, 0);

		//左下
		glTexCoord2f(0, 1);
		glVertex3f(-w, -h, 0);

		//左上
		glTexCoord2f(0, 0);
		glVertex3f(-w, h, 0);

		//右上
		glTexCoord2f(1, 0);
		glVertex3f(w, h, 0);
	}
	glEnd();

	glDisable(GL_TEXTURE_2D);
	glDisable(GL_BLEND);

	GL::DeleteTexture(id);
}
inline void GL::ToolKit::DrawImage(Image^ image, double scaleX, double scaleY, double scaleZ, Color color, Color ambient, Color diffuse, Color specular, float shininess) {
	if (image == nullptr) throw gcnew ArgumentNullException("image");
	image->ThrowIfDisposed();

	int id;
	GL::ToolKit::BindTexture(image, id);

	GL::Color4(color);
	GL::Material(GL_FRONT, GL_AMBIENT, ambient);
	GL::Material(GL_FRONT, GL_DIFFUSE, diffuse);
	GL::Material(GL_FRONT, GL_SPECULAR, specular);
	GL::Material(GL_FRONT, GL_SHININESS, shininess);

	glEnable(GL_TEXTURE_2D);

	int w = image->Ptr->cols / 2;
	int h = image->Ptr->rows / 2;

	glBindTexture(GL_TEXTURE_2D, id);

	glScaled(scaleX, scaleY, scaleZ);
	glBegin(GL_QUADS); {
		//右下
		glTexCoord2f(1, 1);
		glVertex3f(w, -h, 0);

		//左下
		glTexCoord2f(0, 1);
		glVertex3f(-w, -h, 0);

		//左上
		glTexCoord2f(0, 0);
		glVertex3f(-w, h, 0);

		//右上
		glTexCoord2f(1, 0);
		glVertex3f(w, h, 0);
	}
	glEnd();

	glDisable(GL_TEXTURE_2D);
	glDisable(GL_BLEND);

	GL::DeleteTexture(id);
}
inline void GL::ToolKit::LockAt(int width, int height, float x, float y, float z, float targetX, float targetY, float targetZ, float zNear, float zFar, float fov, bool perspective) {
	if (perspective) {
		// 視体積の設定
		glMatrixMode(GL_PROJECTION);

		GL::Utility::Perspective(fov, (width / height), zNear, zFar);
		
		glMatrixMode(GL_MODELVIEW);

		// 視界の設定
		GL::Utility::LookAt(x, y, z, targetX, targetY, targetZ, 0, 1, 0);

		glEnable(GL_DEPTH_TEST);
		glDisable(GL_LIGHTING);
	}
	else {
		glMatrixMode(GL_PROJECTION);
		// 視体積の設定
		GL::Utility::Orthographic(width, height, zNear, zFar);

		glMatrixMode(GL_MODELVIEW);

		// 視界の設定
		GL::Utility::LookAt(x, y, z, targetX, targetY, targetZ, 0, 1, 0);

		glDisable(GL_DEPTH_TEST);
		glDisable(GL_LIGHTING);
	}
}