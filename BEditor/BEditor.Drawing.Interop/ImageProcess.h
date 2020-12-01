#pragma once

DLLExport(void) Image_Flip(ImageStruct image, int mode) {
	cv::Mat mat(image.Height, image.Width, image.CvType, image.Data);
	cv::flip(mat, mat, mode);
}

DLLExport(void) Image_AreaExpansion(ImageStruct image, int top, int bottom, int left, int right) {
	cv::Mat mat(image.Height, image.Width, image.CvType, image.Data);
	cv::copyMakeBorder(mat, mat, top, bottom, left, right, cv::BORDER_CONSTANT);
}

DLLExport(void) Image_BoxFilter(ImageStruct image, float size) {
	cv::Mat mat(image.Height, image.Width, image.CvType, image.Data);
	cv::blur(mat, mat, cv::Size2f(size, size));
}
DLLExport(void) Image_GaussBlur(ImageStruct image, float size) {
	cv::Mat mat(image.Height, image.Width, image.CvType, image.Data);
	cv::GaussianBlur(mat, mat, cv::Size2f(size, size), 0.0);
}
DLLExport(void) Image_MedianBlur(ImageStruct image, int size) {
	cv::Mat mat(image.Height, image.Width, image.CvType, image.Data);
	cv::medianBlur(mat, mat, size);
}

DLLExport(void) Image_Dilate(ImageStruct image, int f) {
	cv::Mat mat(image.Height, image.Width, image.CvType, image.Data);
	cv::dilate(mat, mat, cv::noArray(), cv::Point(-1, -1), f);
}
DLLExport(void) Image_Erode(ImageStruct image, int f) {
	cv::Mat mat(image.Height, image.Width, image.CvType, image.Data);
	cv::erode(mat, mat, cv::noArray(), cv::Point(-1, -1), f);
}

DLLExport(void) Image_Decode(uchar* buffer, size_t Length, int flags, ImageStruct* image) {
	const cv::Mat bufMat(1, Length, CV_8UC1, buffer, cv::Mat::AUTO_STEP);
	const auto ret = cv::imdecode(bufMat, flags);
	ImageStruct img {
		ret.rows,
		ret.cols,
		ret.type(),
		ret.data
	};

	*image = img;
}
DLLExport(bool) Image_Save(ImageStruct image, const char* filename) {
	std::vector<int> paramsVec;
	return cv::imwrite(filename, cv::Mat(image.Height, image.Width, image.CvType, image.Data), paramsVec);
}