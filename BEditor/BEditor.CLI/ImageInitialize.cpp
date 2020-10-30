#include "pch.h"

using namespace System;
using namespace System::IO;
using namespace System::Runtime::InteropServices;

using namespace BEditor::CLI::Media;
using namespace BEditor::CLI::Exceptions;

static cv::Mat* ImgDecode(uchar* buf, size_t bufLength, int flags) {
	const cv::Mat bufMat(1, bufLength, CV_8UC1, buf, cv::Mat::AUTO_STEP);
	const auto ret = cv::imdecode(bufMat, flags);

	return new cv::Mat(ret);
}

inline Image::Image() {
	Ptr = new cv::Mat();
}
inline Image::Image(int width, int height) {
	Ptr = new cv::Mat(height, width, ImageType::ByteCh4);
}
inline Image::Image(int width, int height, ImageType type) {
	Ptr = new cv::Mat(height, width, type);
}
inline Image::Image(int width, int height, ImageType type, IntPtr data) {
	if (data == IntPtr::Zero) throw gcnew IntPtrZeroException("data");

	Ptr = new cv::Mat(height, width, type, (void*)data);
}

inline Image::Image(cv::Mat* mat) {
	if (mat == nullptr) throw gcnew ArgumentNullException("mat");

	Ptr = mat;
}
inline Image::Image(Image^ image, Media::Rectangle rect) {
	if (image == nullptr) throw gcnew ArgumentNullException("image");
	image->ThrowIfDisposed();

	Ptr = new cv::Mat(*image->Ptr, cv::Rect(rect.X, rect.Y, rect.Width, rect.Height));

	GC::KeepAlive(image);
}
inline Image::Image(String^ file) {
	if (!File::Exists(file)) throw gcnew ArgumentException("file");

	char* filec = (char*)Marshal::StringToHGlobalAnsi(file).ToPointer();
	const auto cvmat = cv::imread(filec);

	Ptr = new cv::Mat(cvmat);

	Marshal::FreeHGlobal((IntPtr)filec);
}
inline Image::Image(Stream^ stream, ImageReadMode mode) {
	if (stream == nullptr) throw gcnew ArgumentNullException("stream");
	if (stream->Length > int::MaxValue) throw gcnew ArgumentException("Not supported stream (too long)");

	auto memory = gcnew MemoryStream();
	stream->CopyTo(memory);
	
	auto manageArr = memory->ToArray();
	pin_ptr<Byte> array = &manageArr[0]; //スコープ外に行くと固定が解除される
	Ptr = ImgDecode(array, manageArr->Length, (int)mode);

	delete memory;
}
inline Image::Image(Stream^ stream) : Image(stream, ImageReadMode::Color) {

}
inline Image::Image(array<Byte>^ imageBytes, ImageReadMode mode) {
	if (imageBytes == nullptr) throw gcnew ArgumentNullException("imageBytes");

	pin_ptr<Byte> array = &imageBytes[0]; //スコープ外に行くと固定が解除される
	Ptr = ImgDecode(array, imageBytes->Length, (int)mode);
}