#pragma once

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
	TTF_SetFontKerning(font, allowed ? 1 : 0);
}
