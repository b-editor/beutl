#pragma once

DLLExport(const char*) ImageData(cv::Mat* mat, uchar** data) {
	try {
		*data = mat->data;
		return nullptr;
	}
	catch (std::exception e) {
		return e.what();
	}
}

DLLExport(const char*) ImageDataEnd(cv::Mat* mat, const uchar** dataend) {
	try {
		*dataend = mat->dataend;
		return nullptr;
	}
	catch (std::exception e) {
		return e.what();
	}
}

DLLExport(const char*) ImageWidth(cv::Mat* mat, int* width) {
	try {
		*width = mat->cols;
		return nullptr;
	}
	catch (std::exception e) {
		return e.what();
	}
}

DLLExport(const char*) ImageHeight(cv::Mat* mat, int* height) {
	try {
		*height = mat->rows;
		return nullptr;
	}
	catch (std::exception e) {
		return e.what();
	}
}

DLLExport(const char*) ImageStep(cv::Mat* mat, size_t* step) {
	try {
		*step = mat->step;
		return nullptr;
	}
	catch (std::exception e) {
		return e.what();
	}
}

DLLExport(const char*) ImageDimension(cv::Mat* mat, int* dim) {
	try {
		*dim = mat->dims;
		return nullptr;
	}
	catch (std::exception e) {
		return e.what();
	}
}


DLLExport(const char*) ImageType(cv::Mat* mat, int* type) {
	try {
		*type = mat->type();
		return nullptr;
	}
	catch (std::exception e) {
		return e.what();
	}
}

DLLExport(const char*) ImageElemSize(cv::Mat* mat, size_t* elemsize) {
	try {
		*elemsize = mat->elemSize();
		return nullptr;
	}
	catch (std::exception e) {
		return e.what();
	}
}

DLLExport(const char*) ImageIsContinuous(cv::Mat* mat, int* value) {
	try {
		*value = mat->isContinuous() ? 1 : 0;
		return nullptr;
	}
	catch (std::exception e) {
		return e.what();
	}
}

DLLExport(const char*) ImageIsSubmatrix(cv::Mat* mat, int* value) {
	try {
		*value = mat->isSubmatrix() ? 1 : 0;
		return nullptr;
	}
	catch (std::exception e) {
		return e.what();
	}
}

DLLExport(const char*) ImageDepth(cv::Mat* mat, int* depth) {
	try {
		*depth = mat->depth();
		return nullptr;
	}
	catch (std::exception e) {
		return e.what();
	}
}

DLLExport(const char*) ImageChannels(cv::Mat* mat, int* ch) {
	try {
		*ch = mat->channels();
		return nullptr;
	}
	catch (std::exception e) {
		return e.what();
	}
}

DLLExport(const char*) ImageTotal(cv::Mat* mat, size_t* total) {
	try {
		*total = mat->total();
		return nullptr;
	}
	catch (std::exception e) {
		return e.what();
	}
}
