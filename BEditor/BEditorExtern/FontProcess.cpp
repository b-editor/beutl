#include <SDL_ttf.h>
#include <opencv2/opencv.hpp>

#define DLLExport(T) extern "C" __declspec(dllexport) T

//C#から呼び出すと環境変数に設定しているときバグるのでC++から呼び出す

DLLExport(int) FontInit() {
	return TTF_Init();
}

DLLExport(void) FontQuit() {
	TTF_Quit();
}


DLLExport(void) FontOpen1(const char* file, int size, TTF_Font** font) {
	*font = TTF_OpenFont(file, size);
}

DLLExport(void) FontOpen2(const char* file, int size, long index, TTF_Font** font) {
	*font = TTF_OpenFontIndex(file, size, index);
}

DLLExport(void) FontClose(TTF_Font* font) {
	TTF_CloseFont(font);
}


DLLExport(int) FontGetStyle(TTF_Font* font) {
	return TTF_GetFontStyle(font);
}
DLLExport(void) FontSetStyle(TTF_Font* font, int style) {
	TTF_SetFontStyle(font, style);
}


DLLExport(int) FontGetOutline(TTF_Font* font) {
	return TTF_GetFontOutline(font);
}
DLLExport(void) FontSetOutline(TTF_Font* font, int outline) {
	TTF_SetFontOutline(font, outline);
}


DLLExport(int) FontGetHinting(TTF_Font* font) {
	return TTF_GetFontHinting(font);
}
DLLExport(void) FontSetHinting(TTF_Font* font, int hinting) {
	TTF_SetFontHinting(font, hinting);
}


DLLExport(bool) FontGetKerning(TTF_Font* font) {
	return TTF_GetFontKerning(font) != 0;
}
DLLExport(void) FontSetKerning(TTF_Font* font, bool allowed) {
	if (allowed) {
		TTF_SetFontKerning(font, 1);
	}
	else {
		TTF_SetFontKerning(font, 0);
	}
}


DLLExport(int) FontHeight(TTF_Font* font) {
	return TTF_FontHeight(font);
}

DLLExport(int) FontAscent(TTF_Font* font) {
	return TTF_FontAscent(font);
}

DLLExport(int) FontDescent(TTF_Font* font) {
	return TTF_FontDescent(font);
}

DLLExport(int) FontLineSlip(TTF_Font* font) {
	return TTF_FontLineSkip(font);
}

DLLExport(long) FontFaces(TTF_Font* font) {
	return TTF_FontFaces(font);
}

DLLExport(bool) FontIsFixedWidth(TTF_Font* font) {
	return TTF_FontFaceIsFixedWidth(font) != 0;
}

DLLExport(const char*) FontStyleName(TTF_Font* font) {
	return TTF_FontFaceStyleName(font);
}

DLLExport(const char*) FontFamilyName(TTF_Font* font) {
	return TTF_FontFaceFamilyName(font);
}

DLLExport(int) FontGlyphIsProvided(TTF_Font* font, Uint16 ch) {
	return TTF_GlyphIsProvided(font, ch);
}

DLLExport(bool) FontGlyphMetrics(TTF_Font* font, Uint16 ch, int* minx, int* maxx, int* miny, int* maxy, int* advance) {
	return TTF_GlyphMetrics(font, ch, minx, maxx, miny, maxy, advance);
}

DLLExport(bool) FontSizeText(TTF_Font* font, const char* text, int* width, int* height) {
	return TTF_SizeText(font, text, width, height);
}

DLLExport(bool) FontSizeUNICODE(TTF_Font* font, const Uint16* text, int* width, int* height) {
	return TTF_SizeUNICODE(font, text, width, height);
}

DLLExport(bool) FontSizeUTF8(TTF_Font* font, const char* text, int* width, int* height) {
	return TTF_SizeUTF8(font, text, width, height);
}

DLLExport(void) FontRenderGlyph(TTF_Font* font, Uint16 ch, SDL_Color color, cv::Mat** returnmat) {
	auto surface = TTF_RenderGlyph_Blended(font, ch, color);

	SDL_LockSurface(surface);
	cv::Mat* mat = new cv::Mat(surface->h, surface->w, CV_8UC4, surface->pixels);
	const auto tmp = mat->clone();
	*returnmat = new cv::Mat(tmp);

	SDL_UnlockSurface(surface);

	delete mat;
	delete surface;
}

DLLExport(void) FontRenderText(TTF_Font* font, const char* text, SDL_Color color, cv::Mat** returnMat) {
	auto surface = TTF_RenderText_Blended(font, text, color);

	SDL_LockSurface(surface);
	cv::Mat* mat = new cv::Mat(surface->h, surface->w, CV_8UC4, surface->pixels);
	const auto tmp = mat->clone();
	*returnMat = new cv::Mat(tmp);

	SDL_UnlockSurface(surface);

	delete mat;
	delete surface;
}

DLLExport(void) FontRenderUNICODE(TTF_Font* font, const Uint16* text, SDL_Color color, cv::Mat** returnMat) {
	auto surface = TTF_RenderUNICODE_Blended(font, text, color);

	SDL_LockSurface(surface);
	cv::Mat* mat = new cv::Mat(surface->h, surface->w, CV_8UC4, surface->pixels);
	const auto tmp = mat->clone();
	*returnMat = new cv::Mat(tmp);

	SDL_UnlockSurface(surface);

	delete mat;
	delete surface;
}

DLLExport(void) FontRenderUTF8(TTF_Font* font, const char* text, SDL_Color color, cv::Mat** returnmat) {
	auto surface = TTF_RenderUTF8_Blended(font, text, color);

	SDL_LockSurface(surface);
	cv::Mat* mat = new cv::Mat(surface->h, surface->w, CV_8UC4, surface->pixels);
	const auto tmp = mat->clone();
	*returnmat = new cv::Mat(tmp);

	SDL_UnlockSurface(surface);

	delete mat;
	delete surface;
}