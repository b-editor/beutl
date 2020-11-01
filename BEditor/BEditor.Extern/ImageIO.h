#pragma once

#pragma region Create

DLLExport(const char*) ImageCreate1(cv::Mat** returnmat) {
	try {
		*returnmat = new cv::Mat();
		return nullptr;
	}
	catch (std::exception e) {
		return e.what();
	}
}

DLLExport(const char*) ImageCreate2(int width, int height, int type, cv::Mat** returnmat) {
	try {
		*returnmat = new cv::Mat(height, width, type);
		return nullptr;
	}
	catch (std::exception e) {
		return e.what();
	}
}

DLLExport(const char*) ImageCreate3(int width, int height, void* data, int type, cv::Mat** returnmat) {
	try {
		*returnmat = new cv::Mat(height, width, type, data);
		return nullptr;
	}
	catch (std::exception e) {
		return e.what();
	}
}

DLLExport(const char*) ImageCreate4(cv::Mat* mat, int x, int y, int width, int height, cv::Mat** returnmat) {
	try {
		*returnmat = new cv::Mat(*mat, cv::Rect(x, y, width, height));
		return nullptr;
	}
	catch (std::exception e) {
		return e.what();
	}
}

#pragma endregion


DLLExport(const char*) ImageRead(const char* filename, cv::Mat** returnmat) {
	try {
		const auto ret = cv::imread(filename);
		*returnmat = new cv::Mat(ret);
		return nullptr;
	}
	catch (std::exception e) {
		return e.what();
	}
}

DLLExport(const char*) ImageDecode(uchar* buf, size_t bufLength, int flags, cv::Mat** returnmat) {
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

DLLExport(const char*) ImageDelete(cv::Mat* mat) {
	try {
		delete mat;
		return nullptr;
	}
	catch (std::exception e) {
		return e.what();
	}
}

DLLExport(const char*) ImageSave1(cv::Mat* mat, const char* filename, int* params, int paramsLength, int* returnValue) {
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

DLLExport(const char*) ImageSave2(cv::Mat* mat, const char* filename, int* returnValue) {
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
