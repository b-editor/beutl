#include "pch.h"

inline bool FontGlyphMetrics(TTF_Font* font, Uint16 ch, int* minx, int* maxx, int* miny, int* maxy, int* advance) {
	return TTF_GlyphMetrics(font, ch, minx, maxx, miny, maxy, advance);
}

inline bool FontSizeText(TTF_Font* font, const char* text, int* width, int* height) {
	return TTF_SizeText(font, text, width, height);
}

inline bool FontSizeUNICODE(TTF_Font* font, const Uint16* text, int* width, int* height) {
	return TTF_SizeUNICODE(font, text, width, height);
}

inline bool FontSizeUTF8(TTF_Font* font, const char* text, int* width, int* height) {
	return TTF_SizeUTF8(font, text, width, height);
}

inline void FontRenderGlyph(TTF_Font* font, Uint16 ch, uchar r, uchar g, uchar b, uchar a, cv::Mat** returnmat) {
	SDL_Color color;
	color.r = r;
	color.g = g;
	color.b = b;
	color.a = a;

	auto surface = TTF_RenderGlyph_Blended(font, ch, color);

	SDL_LockSurface(surface);
	cv::Mat* mat = new cv::Mat(surface->h, surface->w, CV_8UC4, surface->pixels);
	const auto tmp = mat->clone();
	*returnmat = new cv::Mat(tmp);

	SDL_UnlockSurface(surface);

	delete mat;
	delete surface;
}

inline void FontRenderText(TTF_Font* font, const char* text, uchar r, uchar g, uchar b, uchar a, cv::Mat** returnMat) {
	SDL_Color color;
	color.r = r;
	color.g = g;
	color.b = b;
	color.a = a;

	auto surface = TTF_RenderText_Blended(font, text, color);

	SDL_LockSurface(surface);
	*returnMat = new cv::Mat(surface->h, surface->w, CV_8UC4, surface->pixels);

	SDL_UnlockSurface(surface);
}

inline void FontRenderUNICODE(TTF_Font* font, const Uint16* text, uchar r, uchar g, uchar b, uchar a, cv::Mat** returnMat) {
	SDL_Color color;
	color.r = r;
	color.g = g;
	color.b = b;
	color.a = a;

	auto surface = TTF_RenderUNICODE_Blended(font, text, color);

	SDL_LockSurface(surface);
	*returnMat = new cv::Mat(surface->h, surface->w, CV_8UC4, surface->pixels);

	SDL_UnlockSurface(surface);
}

inline void FontRenderUTF8(TTF_Font* font, const char* text, uchar r, uchar g, uchar b, uchar a, cv::Mat** returnmat) {
	SDL_Color color;
	color.r = r;
	color.g = g;
	color.b = b;
	color.a = a;

	auto surface = TTF_RenderUTF8_Blended(font, text, color);

	SDL_LockSurface(surface);
	*returnmat = new cv::Mat(surface->h, surface->w, CV_8UC4, surface->pixels);
	/*const auto tmp = mat->clone();
	*returnmat = new cv::Mat(tmp);*/

	SDL_UnlockSurface(surface);

	//ピクセルのデータは同じ
	//delete mat;
	//delete surface;
}