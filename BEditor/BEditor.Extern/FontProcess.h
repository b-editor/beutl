#pragma once

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