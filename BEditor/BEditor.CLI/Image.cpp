#include "pch.h"

using namespace BEditor::CLI::Media;

inline Image^ Image::Ellipse(int width, int height, int line, Color color) {
	auto mat = new cv::Mat(width, height, CV_8UC4);

	if (width % 2 == 1) width++;
	if (height % 2 == 1) height++;

	auto min = cv::min(width, height);

	if (line >= min / 2)
		line = min / 2;

	if (line < min) min = line;
	if (min < 0) min = 0;



	cv::ellipse(*mat, cv::Point(width / 2, height / 2), cv::Size(width, height), 0, 0, 360, cv::Scalar(color.B, color.G, color.R, 255), min, cv::LineTypes::LINE_8);

	return gcnew Image(mat);
}
inline Image^ Image::Rectangle(int width, int height, int line, Color color) {
	auto mat = new cv::Mat(width, height, CV_8UC4);

	if (width % 2 == 1) width++;
	if (height % 2 == 1) height++;

	auto min = cv::min(width, height);

	if (line >= min / 2)
		line = min / 2;

	if (line < min) min = line;
	if (min < 0) min = 0;

	cv::rectangle(*mat, cv::Point(0, 0), cv::Point(width, height), cv::Scalar(color.B, color.G, color.R, 255), min);

	return gcnew Image(mat);
}

inline String^ Image::ToString() {
	if (Ptr == nullptr)return GetType()->FullName;
	return String::Format("(Width:{0} Height:{1} Type:{2} Data:{3})", Width, Height, Type, Data);
}
inline void Image::OnDispose(bool disposing) {
	if (!IsDisposed) {
		delete Ptr;
	}

	Ptr = nullptr;
}