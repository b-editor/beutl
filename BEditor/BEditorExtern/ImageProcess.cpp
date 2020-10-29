#include <opencv2/opencv.hpp>

#define DLLEXPORT extern "C" __declspec(dllexport) char const*


#pragma region Create

DLLEXPORT ImageCreate1(cv::Mat** returnmat) {
	try {
		*returnmat = new cv::Mat();
		return nullptr;
	}
	catch (std::exception e) {
		return e.what();
	}
}

DLLEXPORT ImageCreate2(int width, int height, int type, cv::Mat** returnmat) {
	try {
		*returnmat = new cv::Mat(height, width, type);
		return nullptr;
	}
	catch (std::exception e) {
		return e.what();
	}
}

DLLEXPORT ImageCreate3(int width, int height, void* data, int type, cv::Mat** returnmat) {
	try {
		*returnmat = new cv::Mat(height, width, type, data);
		return nullptr;
	}
	catch (std::exception e) {
		return e.what();
	}
}

DLLEXPORT ImageCreate4(cv::Mat* mat, int x, int y, int width, int height, cv::Mat** returnmat) {
	try {
		*returnmat = new cv::Mat(*mat, cv::Rect(x, y, width, height));
		return nullptr;
	}
	catch (std::exception e) {
		return e.what();
	}
}

#pragma endregion


DLLEXPORT ImageRead(const char* filename, cv::Mat** returnmat) {
	try {
		const auto ret = cv::imread(filename);
		*returnmat = new cv::Mat(ret);
		return nullptr;
	}
	catch (std::exception e) {
		return e.what();
	}
}

DLLEXPORT ImageDecode(uchar* buf, size_t bufLength, int flags, cv::Mat** returnmat) {
	try {
		const cv::Mat bufMat(1, bufLength, CV_8UC1, buf, cv::Mat::AUTO_STEP);
		const auto ret = cv::imdecode(bufMat, flags);
		*returnmat = new cv::Mat(ret);

		return nullptr;
	}
	catch (std::exception e) {
		return e.what();
	}
}

DLLEXPORT ImageDelete(cv::Mat* mat) {
	try {
		delete mat;
		return nullptr;
	}
	catch (std::exception e) {
		return e.what();
	}
}

#pragma region Methods

DLLEXPORT ImageData(cv::Mat* mat, uchar** data) {
	try {
		*data = mat->data;
		return nullptr;
	}
	catch (std::exception e) {
		return e.what();
	}
}

DLLEXPORT ImageDataEnd(cv::Mat* mat, const uchar** dataend) {
	try {
		*dataend = mat->dataend;
		return nullptr;
	}
	catch (std::exception e) {
		return e.what();
	}
}

DLLEXPORT ImageWidth(cv::Mat* mat, int* width) {
	try {
		*width = mat->cols;
		return nullptr;
	}
	catch (std::exception e) {
		return e.what();
	}
}

DLLEXPORT ImageHeight(cv::Mat* mat, int* height) {
	try {
		*height = mat->rows;
		return nullptr;
	}
	catch (std::exception e) {
		return e.what();
	}
}

DLLEXPORT ImageStep(cv::Mat* mat, size_t* step) {
	try {
		*step = mat->step;
		return nullptr;
	}
	catch (std::exception e) {
		return e.what();
	}
}

DLLEXPORT ImageDimension(cv::Mat* mat, int* dim) {
	try {
		*dim = mat->dims;
		return nullptr;
	}
	catch (std::exception e) {
		return e.what();
	}
}


DLLEXPORT ImageType(cv::Mat* mat, int* type) {
	try {
		*type = mat->type();
		return nullptr;
	}
	catch (std::exception e) {
		return e.what();
	}
}

DLLEXPORT ImageElemSize(cv::Mat* mat, size_t* elemsize) {
	try {
		*elemsize = mat->elemSize();
		return nullptr;
	}
	catch (std::exception e) {
		return e.what();
	}
}

DLLEXPORT ImageIsContinuous(cv::Mat* mat, int* value) {
	try {
		*value = mat->isContinuous() ? 1 : 0;
		return nullptr;
	}
	catch (std::exception e) {
		return e.what();
	}
}

DLLEXPORT ImageIsSubmatrix(cv::Mat* mat, int* value) {
	try {
		*value = mat->isSubmatrix() ? 1 : 0;
		return nullptr;
	}
	catch (std::exception e) {
		return e.what();
	}
}

DLLEXPORT ImageDepth(cv::Mat* mat, int* depth) {
	try {
		*depth = mat->depth();
		return nullptr;
	}
	catch (std::exception e) {
		return e.what();
	}
}

DLLEXPORT ImageChannels(cv::Mat* mat, int* ch) {
	try {
		*ch = mat->channels();
		return nullptr;
	}
	catch (std::exception e) {
		return e.what();
	}
}

DLLEXPORT ImageTotal(cv::Mat* mat, size_t* total) {
	try {
		*total = mat->total();
		return nullptr;
	}
	catch (std::exception e) {
		return e.what();
	}
}

DLLEXPORT ImageClone(cv::Mat* mat, cv::Mat** returnmat) {
	try {
		const auto ret = mat->clone();
		*returnmat = new cv::Mat(ret);
		return nullptr;
	}
	catch (std::exception e) {
		return e.what();
	}
}

DLLEXPORT ImageSave1(cv::Mat* mat, const char* filename, int* params, int paramsLength, int* returnValue) {
	try {
		std::vector<int> paramsVec;
		paramsVec.assign(params, params + paramsLength);
		*returnValue = cv::imwrite(filename, *mat, paramsVec) ? 1 : 0;
		return nullptr;
	}
	catch (std::exception e) {
		return e.what();
	}
}

DLLEXPORT ImageSave2(cv::Mat* mat, const char* filename, int* returnValue) {
	try {
		//int* params[1];

		std::vector<int> paramsVec;
		//paramsVec.assign(params, params + 0);
		*returnValue = cv::imwrite(filename, *mat, paramsVec) ? 1 : 0;

		//delete params;
		return nullptr;
	}
	catch (std::exception e) {
		return e.what();
	}
}

DLLEXPORT ImageSubMatrix(cv::Mat* mat, int rowSt, int rowEd, int colSt, int colEd, cv::Mat** returnmat) {
	try {
		const cv::Range rowRange(rowSt, rowEd);
		const cv::Range colRange(colSt, colEd);
		const auto ret = (*mat)(rowRange, colRange);
		*returnmat = new cv::Mat(ret);

		return nullptr;
	}
	catch (std::exception e) {
		return e.what();
	}
}

DLLEXPORT ImageCopyTo(cv::Mat* mat, cv::Mat* outmat) {
	try {
		mat->copyTo(*outmat);
		return nullptr;
	}
	catch (std::exception e) {
		return e.what();
	}
}


#pragma region Effect

DLLEXPORT ImageFlip(cv::Mat* mat, int mode) {
	try {
		cv::flip(*mat, *mat, mode);

		return nullptr;
	}
	catch (std::exception e) {
		return e.what();
	}
}

DLLEXPORT ImageAreaExpansion1(cv::Mat* mat, int top, int bottom, int left, int right) {
	try {
		cv::copyMakeBorder(*mat, *mat, top, bottom, left, right, cv::BORDER_CONSTANT);

		return nullptr;
	}
	catch (std::exception e) {
		return e.what();
	}
}

DLLEXPORT ImageAreaExpansion2(cv::Mat* mat, int width, int height) {
	try {
		int v = (height - mat->rows) / 2;
		int h = (width - mat->cols) / 2;

		cv::copyMakeBorder(*mat, *mat, v, v, h, h, cv::BORDER_CONSTANT);

		return nullptr;
	}
	catch (std::exception e) {
		return e.what();
	}
}

DLLEXPORT ImageBlur(cv::Mat* mat, int blursize, bool alphablur) {
	try {
		cv::Size* size = new cv::Size(blursize, blursize);

		if (alphablur) {
			int width = mat->cols + blursize;
			int height = mat->rows + blursize;

			ImageAreaExpansion2(mat, width, height);
			cv::blur(*mat, *mat, *size);
		}
		else {
			cv::blur(*mat, *mat, *size);
		}
		delete size;

		return nullptr;
	}
	catch (std::exception e) {
		return e.what();
	}
}

DLLEXPORT ImageGaussianBlur(cv::Mat* mat, int blursize, bool alphablur) {
	try {
		if (blursize % 2 != 1) {
			blursize++;
		}

		cv::Size* size = new cv::Size(blursize, blursize);

		if (alphablur) {
			int width = mat->cols + blursize;
			int height = mat->rows + blursize;

			ImageAreaExpansion2(mat, width, height);
			cv::GaussianBlur(*mat, *mat, *size, 0.0);
		}
		else {
			cv::GaussianBlur(*mat, *mat, *size, 0.0);
		}
		delete size;

		return nullptr;
	}
	catch (std::exception e) {
		return e.what();
	}
}

DLLEXPORT ImageMedianBlur(cv::Mat* mat, int blursize, bool alphablur) {
	try {
		if (blursize % 2 != 1) {
			blursize++;
		}

		cv::Size* size = new cv::Size(blursize, blursize);

		if (alphablur) {
			int width = mat->cols + blursize;
			int height = mat->rows + blursize;

			ImageAreaExpansion2(mat, width, height);
			cv::medianBlur(*mat, *mat, blursize);
		}
		else {
			cv::medianBlur(*mat, *mat, blursize);
		}

		delete size;

		return nullptr;
	}
	catch (std::exception e) {
		return e.what();
	}
}

DLLEXPORT ImageAdd(cv::Mat* base, cv::Mat* src1) {
	try {
		cv::add(*base, *src1, *base);
		return nullptr;
	}
	catch (std::exception e) {
		return e.what();
	}
}

DLLEXPORT ImageDilate(cv::Mat* mat, int f) {
	try {
		cv::dilate(*mat, *mat, cv::noArray(), cv::Point(-1, -1), f);

		return nullptr;
	}
	catch (std::exception e) {
		return e.what();
	}
}

DLLEXPORT ImageErode(cv::Mat* mat, int f) {
	try {
		cv::erode(*mat, *mat, cv::noArray(), cv::Point(-1, -1), f);

		return nullptr;
	}
	catch (std::exception e) {
		return e.what();
	}
}

DLLEXPORT ImageClip(cv::Mat* mat, int top, int bottom, int left, int right, cv::Mat** returnmat) {
	try {
		int width = mat->cols - left - right;
		int height = mat->rows - top - bottom;
		int x = left;
		int y = top;

		cv::Mat* tmp = new cv::Mat(*mat, cv::Rect(x, y, width, height));
		delete mat;

		*returnmat = tmp; //TODO : “®‚©‚È‚¢ê‡‚±‚±‚ð•ÏX

		return nullptr;
	}
	catch (std::exception e) {
		return e.what();
	}
}

#pragma endregion

#pragma region Load

DLLEXPORT ImageEllipse(int width, int height, int line, float r, float g, float b, cv::Mat** mat) {
	try {
		*mat = new cv::Mat(width, height, CV_8UC4);

		if (width % 2 == 1) width++;
		if (height % 2 == 1) height++;

		auto min = cv::min(width, height);

		if (line >= min / 2)
			line = min / 2;

		if (line < min) min = line;
		if (min < 0) min = 0;



		cv::ellipse(**mat, cv::Point(width / 2, height / 2), cv::Size(width, height), 0, 0, 360, cv::Scalar(b, g, r, 255), min, cv::LineTypes::LINE_8);

		return nullptr;
	}
	catch (std::exception e) {
		return e.what();
	}
}

#pragma endregion


#pragma endregion
