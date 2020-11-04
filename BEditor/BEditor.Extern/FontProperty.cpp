#include "pch.h"

inline int FontGetStyle(TTF_Font* font) {
	return TTF_GetFontStyle(font);
}
inline void FontSetStyle(TTF_Font* font, int style) {
	TTF_SetFontStyle(font, style);
}


inline int FontGetOutline(TTF_Font* font) {
	return TTF_GetFontOutline(font);
}
inline void FontSetOutline(TTF_Font* font, int outline) {
	TTF_SetFontOutline(font, outline);
}


inline int FontGetHinting(TTF_Font* font) {
	return TTF_GetFontHinting(font);
}
inline void FontSetHinting(TTF_Font* font, int hinting) {
	TTF_SetFontHinting(font, hinting);
}


inline bool FontGetKerning(TTF_Font* font) {
	return TTF_GetFontKerning(font) != 0;
}
inline void FontSetKerning(TTF_Font* font, bool allowed) {
	TTF_SetFontKerning(font, allowed ? 1 : 0);
}
