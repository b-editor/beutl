#include "pch.h"

using namespace System;
using namespace System::Runtime::InteropServices;
using namespace System::Threading::Tasks;
using namespace BEditor::CLI::Media;

inline Image^ Image::Clone() {
	ThrowIfDisposed();
	return gcnew Image(&Ptr->clone());
}
inline Image^ Image::Clone(Media::Rectangle rect) {
	auto part = gcnew Image(this, rect);
	auto result = part->Clone();

	delete part;

	return result;
}

inline bool Image::Save(String^ file) {
	ThrowIfDisposed();
	char* filec = (char*)Marshal::StringToHGlobalAnsi(file).ToPointer();

	auto result = cv::imwrite(filec, *Ptr);

	Marshal::FreeHGlobal((IntPtr)filec);
	return result;
}

inline Image^ Image::SubMatrix(int rowStart, int rowEnd, int colStart, int colEnd) {
	if (rowStart >= rowEnd) throw gcnew ArgumentException("rowStart >= rowEnd");
	if (colStart >= colEnd) throw gcnew ArgumentException("colStart >= colEnd");

	ThrowIfDisposed();

	const cv::Range rowRange(rowStart, rowEnd);
	const cv::Range colRange(colStart, colEnd);
	auto ret = (*Ptr)(rowRange, colRange);

	return gcnew Image(&ret);
}
inline Image^ Image::SubMatrix(Range rowRange, Range colRange) {
	return SubMatrix(
		rowRange.Start,
		rowRange.End,
		colRange.Start,
		colRange.End);
}
inline Image^ Image::SubMatrix(Media::Rectangle roi) {
	return SubMatrix(roi.Y, roi.Y + roi.Height, roi.X, roi.X + roi.Width);
}

inline void Image::CopyTo(Image^ image) {
	ThrowIfDisposed();
	if (image == nullptr) throw gcnew ArgumentNullException("image");
	image->ThrowIfDisposed();

	Ptr->copyTo(*image->Ptr);
}