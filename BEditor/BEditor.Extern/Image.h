#pragma once

DLLExport(const char*) ImageClone(cv::Mat* mat, cv::Mat** returnmat) {
	try {
		const auto ret = mat->clone();
		*returnmat = new cv::Mat(ret);
		return nullptr;
	}
	catch (std::exception e) {
		return e.what();
	}
}

DLLExport(const char*) ImageSubMatrix(cv::Mat* mat, int rowSt, int rowEd, int colSt, int colEd, cv::Mat** returnmat) {
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

DLLExport(const char*) ImageCopyTo(cv::Mat* mat, cv::Mat* outmat) {
	try {
		mat->copyTo(*outmat);
		return nullptr;
	}
	catch (std::exception e) {
		return e.what();
	}
}
