#include "pch.h"

using namespace System;

using namespace BEditor::CLI::Media;


inline Size::Size(int width, int height) {
	Width = width;
	Height = height;
}

inline int Size::Width::get() {
	return this->width;
}
inline void Size::Width::set(int value) {
	if (value < 0) throw gcnew Exception("Width < 0");

	this->width = value;
}
inline int Size::Height::get() {
	return height;
}
inline void Size::Height::set(int value) {
	if (value < 0) throw gcnew Exception("Height < 0");

	height = value;
}
inline float Size::Aspect::get() {
	return width / height;
}

inline Size Size::Add(Size size1, Size size2) {
	return Size(
		size1.Width + size2.Width,
		size1.Height + size2.Height);
}
inline Size Size::Subtract(Size size1, Size size2) {
	return Size(
		size1.Width - size2.Width,
		size1.Height - size2.Height);
}
inline bool Size::Equals(Size size) {
	return this->width == size.width && this->height == size.height;
}
inline bool Size::Equals(Object^ obj) {
	return obj->GetType() == Size::typeid && this->Equals((Size)obj);
}
inline String^ Size::ToString() {
	return String::Format("(Width:{0} Height:{0})", this->width, this->height);
}
inline int Size::GetHashCode() {
	return HashCode::Combine(this->width, this->height);
}

inline Size Size::operator+(Size size1, Size size2) {
	return Add(size1, size2);
}
inline Size Size::operator-(Size size1, Size size2) {
	return Subtract(size1, size2);
}
inline Size Size::operator*(Size left, int right) {
	return Size(left.width * right, left.height * right);
}
inline Size Size::operator/(Size left, int right) {
	return Size(left.width / right, left.height / right);
}
inline bool Size::operator==(Size left, Size right) {
	return left.Equals(right);
}
inline bool Size::operator!=(Size left, Size right) {
	return !left.Equals(right);
}