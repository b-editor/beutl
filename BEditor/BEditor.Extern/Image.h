#pragma once

DLLExport(const char*) ImageClone(cv::Mat* mat, cv::Mat** returnmat);

DLLExport(const char*) ImageSubMatrix(cv::Mat* mat, int rowSt, int rowEd, int colSt, int colEd, cv::Mat** returnmat);

DLLExport(const char*) ImageCopyTo(cv::Mat* mat, cv::Mat* outmat);

//Effect
DLLExport(const char*) ImageFlip(cv::Mat* mat, int mode);

DLLExport(const char*) ImageAreaExpansion1(cv::Mat* mat, int top, int bottom, int left, int right);

DLLExport(const char*) ImageAreaExpansion2(cv::Mat* mat, int width, int height);

DLLExport(const char*) ImageBlur(cv::Mat* mat, int blursize, bool alphablur);

DLLExport(const char*) ImageGaussianBlur(cv::Mat* mat, int blursize, bool alphablur);

DLLExport(const char*) ImageMedianBlur(cv::Mat* mat, int blursize, bool alphablur);

DLLExport(const char*) ImageAdd(cv::Mat* base, cv::Mat* src1);

DLLExport(const char*) ImageDilate(cv::Mat* mat, int f);

DLLExport(const char*) ImageErode(cv::Mat* mat, int f);

DLLExport(const char*) ImageClip(cv::Mat* mat, int top, int bottom, int left, int right, cv::Mat** returnmat);

//Load
DLLExport(const char*) ImageEllipse(int width, int height, int line, float r, float g, float b, cv::Mat** mat);

//Create
DLLExport(const char*) ImageCreate1(cv::Mat** returnmat);

DLLExport(const char*) ImageCreate2(int width, int height, int type, cv::Mat** returnmat);

DLLExport(const char*) ImageCreate3(int width, int height, void* data, int type, cv::Mat** returnmat);

DLLExport(const char*) ImageCreate4(cv::Mat* mat, int x, int y, int width, int height, cv::Mat** returnmat);


DLLExport(const char*) ImageRead(const char* filename, cv::Mat** returnmat);

DLLExport(const char*) ImageDecode(uchar* buf, size_t bufLength, int flags, cv::Mat** returnmat);

DLLExport(const char*) ImageDelete(cv::Mat* mat);

DLLExport(const char*) ImageSave1(cv::Mat* mat, const char* filename, int* params, int paramsLength, int* returnValue);

DLLExport(const char*) ImageSave2(cv::Mat* mat, const char* filename, int* returnValue);

//property
DLLExport(const char*) ImageData(cv::Mat* mat, uchar** data);

DLLExport(const char*) ImageDataEnd(cv::Mat* mat, const uchar** dataend);

DLLExport(const char*) ImageWidth(cv::Mat* mat, int* width);

DLLExport(const char*) ImageHeight(cv::Mat* mat, int* height);

DLLExport(const char*) ImageStep(cv::Mat* mat, size_t* step);

DLLExport(const char*) ImageDimension(cv::Mat* mat, int* dim);


DLLExport(const char*) ImageType(cv::Mat* mat, int* type);

DLLExport(const char*) ImageElemSize(cv::Mat* mat, size_t* elemsize);

DLLExport(const char*) ImageIsContinuous(cv::Mat* mat, int* value);

DLLExport(const char*) ImageIsSubmatrix(cv::Mat* mat, int* value);

DLLExport(const char*) ImageDepth(cv::Mat* mat, int* depth);

DLLExport(const char*) ImageChannels(cv::Mat* mat, int* ch);

DLLExport(const char*) ImageTotal(cv::Mat* mat, size_t* total);