#include "pch.h"

inline int FontInit() {
	return TTF_Init();
}

inline void FontQuit() {
	TTF_Quit();
}


inline void FontOpen1(const char* file, int size, TTF_Font** font) {
	*font = TTF_OpenFont(file, size);
}

inline void FontOpen2(const char* file, int size, long index, TTF_Font** font) {
	*font = TTF_OpenFontIndex(file, size, index);
}

inline void FontClose(TTF_Font* font) {
	TTF_CloseFont(font);
}


inline int FontHeight(TTF_Font* font) {
	return TTF_FontHeight(font);
}

inline int FontAscent(TTF_Font* font) {
	return TTF_FontAscent(font);
}

inline int FontDescent(TTF_Font* font) {
	return TTF_FontDescent(font);
}

inline int FontLineSlip(TTF_Font* font) {
	return TTF_FontLineSkip(font);
}

inline long FontFaces(TTF_Font* font) {
	return TTF_FontFaces(font);
}

inline bool FontIsFixedWidth(TTF_Font* font) {
	return TTF_FontFaceIsFixedWidth(font) != 0;
}

inline const char* FontStyleName(TTF_Font* font) {
	return TTF_FontFaceStyleName(font);
}

inline const char* FontFamilyName(TTF_Font* font) {
	return TTF_FontFaceFamilyName(font);
}

inline int FontGlyphIsProvided(TTF_Font* font, Uint16 ch) {
	return TTF_GlyphIsProvided(font, ch);
}