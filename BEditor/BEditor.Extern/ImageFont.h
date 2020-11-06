#pragma once

#include <ft2build.h>
#include <freetype\freetype.h>
#include <freetype\ftstroke.h>

#include "Color.h"

class ImageFont {
public:
	ImageFont(const char* filename, uint32_t height, bool isFitHeight = true, long index = 0);
	~ImageFont();


	int	CalcRect(int* left, int* top, int* right, int* bottom, const char* str);
	int	CalcRect(int* left, int* top, int* right, int* bottom, const std::u32string& u32str);

	int DrawTextGRAY(int x, int y, const char* str, uint8_t color, uint8_t* image, int stride, int cx, int cy);
	int DrawTextGRAY(int x, int y, const std::u32string& u32str, uint8_t color, uint8_t* image, int stride, int cx, int cy);

	int DrawTextBGRA(int x, int y, const char* str, Color color, uint8_t* image, int stride, int cx, int cy);
	int DrawTextBGRA(int x, int y, const std::u32string& u32str, Color color, uint8_t* image, int stride, int cx, int cy);

	FT_Face m_piFace;
protected:
	int	m_nBaseline;
};

DLLExport(bool) FontInit();
DLLExport(void) FontQuit();

DLLExport(ImageFont*) ImageFontCreate1(const char* filename, uint32_t height, bool isFitHeight, long index);
DLLExport(ImageFont*) ImageFontCreate2(const char* filename, uint32_t height, bool isFitHeight);
DLLExport(ImageFont*) ImageFontCreate3(const char* filename, uint32_t height);

DLLExport(void) ImageFontClose(ImageFont* font);

DLLExport(void) ImageFontSizeText(ImageFont* font, int* left, int* top, int* right, int* bottom, const char* str);

DLLExport(void) ImageFontDrawTextBGRA(ImageFont* font, int x, int y, const char* str, int r, int g, int b, uint8_t* image, int stride, int cx, int cy);

DLLExport(long) ImageFontGetStyle(ImageFont* font);
DLLExport(void) ImageFontSetStyle(ImageFont* font, long value);

DLLExport(int) ImageFontHeight(ImageFont* font);
DLLExport(int) ImageFontAscender(ImageFont* font);
DLLExport(int) ImageFontDescender(ImageFont* font);
DLLExport(const char*)ImageFontFamilyName(ImageFont* font);
DLLExport(const char*)ImageFontStyleName(ImageFont* font);