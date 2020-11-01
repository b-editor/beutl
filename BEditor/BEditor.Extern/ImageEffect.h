#pragma once

#pragma region Effect

DLLExport(const char*) ImageFlip(cv::Mat* mat, int mode) {
	try {
		cv::flip(*mat, *mat, mode);

		return nullptr;
	}
	catch (std::exception e) {
		return e.what();
	}
}

DLLExport(const char*) ImageAreaExpansion1(cv::Mat* mat, int top, int bottom, int left, int right) {
	try {
		cv::copyMakeBorder(*mat, *mat, top, bottom, left, right, cv::BORDER_CONSTANT);

		return nullptr;
	}
	catch (std::exception e) {
		return e.what();
	}
}

DLLExport(const char*) ImageAreaExpansion2(cv::Mat* mat, int width, int height) {
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

DLLExport(const char*) ImageBlur(cv::Mat* mat, int blursize, bool alphablur) {
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

DLLExport(const char*) ImageGaussianBlur(cv::Mat* mat, int blursize, bool alphablur) {
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

DLLExport(const char*) ImageMedianBlur(cv::Mat* mat, int blursize, bool alphablur) {
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

DLLExport(const char*) ImageAdd(cv::Mat* base, cv::Mat* src1) {
	try {
		cv::add(*base, *src1, *base);
		return nullptr;
	}
	catch (std::exception e) {
		return e.what();
	}
}

DLLExport(const char*) ImageDilate(cv::Mat* mat, int f) {
	try {
		cv::dilate(*mat, *mat, cv::noArray(), cv::Point(-1, -1), f);

		return nullptr;
	}
	catch (std::exception e) {
		return e.what();
	}
}

DLLExport(const char*) ImageErode(cv::Mat* mat, int f) {
	try {
		cv::erode(*mat, *mat, cv::noArray(), cv::Point(-1, -1), f);

		return nullptr;
	}
	catch (std::exception e) {
		return e.what();
	}
}

DLLExport(const char*) ImageClip(cv::Mat* mat, int top, int bottom, int left, int right, cv::Mat** returnmat) {
	try {
		int width = mat->cols - left - right;
		int height = mat->rows - top - bottom;
		int x = left;
		int y = top;

		cv::Mat* tmp = new cv::Mat(*mat, cv::Rect(x, y, width, height));
		delete mat;

		*returnmat = tmp; //TODO : “®‚©‚È‚¢ê‡‚±‚±‚ğ•ÏX

		return nullptr;
	}
	catch (std::exception e) {
		return e.what();
	}
}

#pragma endregion

#pragma region Load

DLLExport(const char*) ImageEllipse(int width, int height, int line, float r, float g, float b, cv::Mat** mat) {
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
