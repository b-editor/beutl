#pragma once

DLLExport(void) Image_BoxBlur(ImageStruct image, float size, ImageStruct out) {
	cv::Mat mat(image.Height, image.Width, image.CvType, image.Data);
	cv::Mat ret(out.Height, out.Width, out.CvType, out.Data);

	cv::blur(mat, ret, cv::Size2f(size, size));
}
DLLExport(void) Image_GaussBlur(ImageStruct image, float size, ImageStruct out) {
	cv::Mat mat(image.Height, image.Width, image.CvType, image.Data);
	cv::Mat ret(out.Height, out.Width, out.CvType, out.Data);

	cv::GaussianBlur(mat, ret, cv::Size2f(size, size), 0.0);
}
DLLExport(void) Image_MedianBlur(ImageStruct image, int size, ImageStruct out) {
	cv::Mat mat(image.Height, image.Width, image.CvType, image.Data);
	cv::Mat ret(out.Height, out.Width, out.CvType, out.Data);

	cv::medianBlur(mat, ret, size);
}

DLLExport(void) Image_Dilate(ImageStruct image, int f, ImageStruct out) {
	cv::Mat mat(image.Height, image.Width, image.CvType, image.Data);
	cv::Mat ret(out.Height, out.Width, out.CvType, out.Data);

	cv::dilate(mat, ret, cv::noArray(), cv::Point(-1, -1), f);
}
DLLExport(void) Image_Erode(ImageStruct image, int f, ImageStruct out) {
	cv::Mat mat(image.Height, image.Width, image.CvType, image.Data);
	cv::Mat ret(out.Height, out.Width, out.CvType, out.Data);

	cv::erode(mat, ret, cv::noArray(), cv::Point(-1, -1), f);
}

DLLExport(bool) Image_Save(ImageStruct image, const char* filename) {
	std::vector<int> paramsVec;
	return cv::imwrite(filename, cv::Mat(image.Height, image.Width, image.CvType, image.Data), paramsVec);
}