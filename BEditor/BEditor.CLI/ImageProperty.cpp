#include "pch.h"

using namespace System;
using namespace BEditor::CLI::Media;

inline IntPtr Image::Data::get() {
	ThrowIfDisposed();
	return IntPtr(Ptr->data);
}
inline IntPtr Image::DataStart::get() {
	ThrowIfDisposed();
	return IntPtr((void*)Ptr->datastart);
}
inline IntPtr Image::DataEnd::get() {
	ThrowIfDisposed();
	return IntPtr((void*)Ptr->dataend);
}
inline IntPtr Image::DataLimit::get() {
	ThrowIfDisposed();
	return IntPtr((void*)Ptr->datalimit);
}

inline int Image::Width::get() {
	ThrowIfDisposed();
	return Ptr->cols;
}
inline int Image::Height::get() {
	ThrowIfDisposed();
	return Ptr->rows;
}
inline BEditor::CLI::Media::Size Image::Size::get() {
	return BEditor::CLI::Media::Size(Width, Height);
}
inline ImageType Image::Type::get() {
	ThrowIfDisposed();
	return Ptr->type();
}

inline long Image::Step::get() {
	ThrowIfDisposed();
	return Ptr->step;
}
inline int Image::ElemSize::get() {
	ThrowIfDisposed();
	return Ptr->elemSize();
}

inline bool Image::IsContinuous::get() {
	ThrowIfDisposed();
	return Ptr->isContinuous();
}
inline bool Image::IsSubmatrix::get() {
	ThrowIfDisposed();
	return Ptr->isSubmatrix();
}

inline int Image::Depth::get() {
	ThrowIfDisposed();
	return Ptr->depth();
}
inline int Image::Channels::get() {
	ThrowIfDisposed();
	return Ptr->channels();
}
inline long Image::Total::get() {
	ThrowIfDisposed();
	return Ptr->total();
}
inline int Image::Dimensions::get() {
	ThrowIfDisposed();
	return Ptr->dims;
}