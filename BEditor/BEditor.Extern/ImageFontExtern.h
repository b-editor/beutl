#pragma once
#include <ft2build.h>
#include <freetype\freetype.h>
#include FT_STROKER_H

DLLExport(bool) FontInit() {
	m_piLibrary = NULL;

	auto error = FT_Init_FreeType(&m_piLibrary);
	return error == 0;;
}
DLLExport(void) FontQuit() {
	FT_Done_FreeType(m_piLibrary);
}

DLLExport(ImageFont*) ImageFontCreate1(const char* filename, uint32_t height, bool isFitHeight, long index) {
	return new ImageFont(filename, height, isFitHeight, index);
}
DLLExport(ImageFont*) ImageFontCreate2(const char* filename, uint32_t height, bool isFitHeight) {
	return new ImageFont(filename, height, isFitHeight);
}
DLLExport(ImageFont*) ImageFontCreate3(const char* filename, uint32_t height) {
	return new ImageFont(filename, height);
}

DLLExport(void) ImageFontClose(ImageFont* font) {
	delete font;
}

DLLExport(void) ImageFontSizeText(ImageFont* font, int* left, int* top, int* right, int* bottom, const char* str) {
	font->CalcRect(left, top, right, bottom, str);
}

DLLExport(void) ImageFontDrawTextBGRA(ImageFont* font, int x, int y, const char* str, int r, int g, int b, uint8_t* image, int stride, int cx, int cy) {
	font->DrawTextBGRA(
		x,
		y,
		str,
		{ r, g, b, 255 },
		image,
		stride,
		cx,
		cy);
}

DLLExport(long) ImageFontGetStyle(ImageFont* font) {
	return font->m_piFace->style_flags;
}
DLLExport(void) ImageFontSetStyle(ImageFont* font, long value) {
	font->m_piFace->style_flags = value;
}

DLLExport(int) ImageFontHeight(ImageFont* font) {
	return font->m_piFace->height;
}
DLLExport(int) ImageFontAscender(ImageFont* font) {
	return font->m_piFace->ascender;
}
DLLExport(int) ImageFontDescender(ImageFont* font) {
	return font->m_piFace->descender;
}
DLLExport(const char*)ImageFontFamilyName(ImageFont* font) {
	return font->m_piFace->family_name;
}
DLLExport(const char*)ImageFontStyleName(ImageFont* font) {
	return font->m_piFace->style_name;
}