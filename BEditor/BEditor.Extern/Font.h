#pragma once

DLLExport(int) FontInit();

DLLExport(void) FontQuit();


DLLExport(void) FontOpen1(const char* file, int size, TTF_Font** font);

DLLExport(void) FontOpen2(const char* file, int size, long index, TTF_Font** font);

DLLExport(void) FontClose(TTF_Font* font);


DLLExport(int) FontHeight(TTF_Font* font);

DLLExport(int) FontAscent(TTF_Font* font);

DLLExport(int) FontDescent(TTF_Font* font);

DLLExport(int) FontLineSlip(TTF_Font* font);

DLLExport(long) FontFaces(TTF_Font* font);

DLLExport(bool) FontIsFixedWidth(TTF_Font* font);

DLLExport(const char*) FontStyleName(TTF_Font* font);

DLLExport(const char*) FontFamilyName(TTF_Font* font);

DLLExport(int) FontGlyphIsProvided(TTF_Font* font, Uint16 ch);

#pragma region Property

DLLExport(int) FontGetStyle(TTF_Font* font);
DLLExport(void) FontSetStyle(TTF_Font* font, int style);

DLLExport(int) FontGetOutline(TTF_Font* font);
DLLExport(void) FontSetOutline(TTF_Font* font, int outline);

DLLExport(int) FontGetHinting(TTF_Font* font);
DLLExport(void) FontSetHinting(TTF_Font* font, int hinting);

DLLExport(bool) FontGetKerning(TTF_Font* font);
DLLExport(void) FontSetKerning(TTF_Font* font, bool allowed);

#pragma endregion

#pragma region Render

DLLExport(bool) FontGlyphMetrics(TTF_Font* font, Uint16 ch, int* minx, int* maxx, int* miny, int* maxy, int* advance);

DLLExport(bool) FontSizeText(TTF_Font* font, const char* text, int* width, int* height);

DLLExport(bool) FontSizeUNICODE(TTF_Font* font, const Uint16* text, int* width, int* height);

DLLExport(bool) FontSizeUTF8(TTF_Font* font, const char* text, int* width, int* height);

DLLExport(void) FontRenderGlyph(TTF_Font* font, Uint16 ch, uchar r, uchar g, uchar b, uchar a, cv::Mat** returnmat);

DLLExport(void) FontRenderText(TTF_Font* font, const char* text, uchar r, uchar g, uchar b, uchar a, cv::Mat** returnMat);

DLLExport(void) FontRenderUNICODE(TTF_Font* font, const Uint16* text, uchar r, uchar g, uchar b, uchar a, cv::Mat** returnMat);

DLLExport(void) FontRenderUTF8(TTF_Font* font, const char* text, uchar r, uchar g, uchar b, uchar a, cv::Mat** returnmat);

#pragma endregion
