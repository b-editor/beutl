#include "pch.h"
#include <math.h>

using namespace System;

using namespace BEditor::CLI::Media;
using Rect = BEditor::CLI::Media::Rectangle;

inline Rect::Rectangle(int x, int y, int width, int height) {
	X = x;
	Y = y;
	Width = width;
	Height = height;
}
inline Rect::Rectangle(Point2 point, Media::Size size) {
	X = point.X;
	Y = point.Y;
	Width = size.Width;
	Height = size.Height;
}

inline int Rect::Top::get() {
	return Y;
}
inline void Rect::Top::set(int value) {
	Y = value;
}

inline int Rect::Bottom::get() {
	return Y + Height;
}

inline int Rect::Left::get() {
	return X;
}
inline void Rect::Left::set(int value) {
	X = value;
}

inline int Rect::Right::get() {
	return X + Width;
}

inline Point2 Rect::TopLeft::get() {
	return Point2(X, Y);
}
inline Point2 Rect::BottomRight::get() {
	return Point2(X + Width, Y + Height);
}

inline Point2 Rect::Point::get() {
	return Point2(X, Y);
}
inline void Rect::Point::set(Point2 value) {
	X = (int)value.X;
	Y = (int)value.Y;
}

inline BEditor::CLI::Media::Size Rect::Size::get() {
	return BEditor::CLI::Media::Size(Width, Height);
}
inline void Rect::Size::set(BEditor::CLI::Media::Size value) {
	Width = value.Width;
	Height = value.Height;
}


inline Rect Rect::FromLTRB(int left, int top, int right, int bottom) {
	auto r = Rectangle(
		left,
		top,
		right - left,
		bottom - top);

	if (r.Width < 0)
		throw gcnew ArgumentException("right > left");
	if (r.Height < 0)
		throw gcnew ArgumentException("bottom > top");
	return r;
}
inline Rect Rect::Inflate(Rect rect, int x, int y) {
	rect.Inflate(x, y);
	return rect;
}
inline Rect Rect::Intersect(Rect a, Rect b) {
	int x1 = std::max(a.X, b.X);
	int x2 = std::min(a.X + a.Width, b.X + b.Width);
	int y1 = std::max(a.Y, b.Y);
	int y2 = std::min(a.Y + a.Height, b.Y + b.Height);
	
	if (x2 >= x1 && y2 >= y1)
		return Rectangle(x1, y1, x2 - x1, y2 - y1);
	return Empty;
}
inline Rect Rect::Union(Rect a, Rect b) {
	int x1 = std::min(a.X, b.X);
	int x2 = std::max(a.X + a.Width, b.X + b.Width);
	int y1 = std::min(a.Y, b.Y);
	int y2 = std::max(a.Y + a.Height, b.Y + b.Height);

	return Rectangle(x1, y1, x2 - x1, y2 - y1);
}

inline void Rect::Inflate(int width, int height) {
	X -= width;
	Y -= height;
	Width += (2 * width);
	Height += (2 * height);
}
inline void Rect::Inflate(Media::Size size) {
	this->Inflate(size.Width, size.Height);
}
inline Rect Rect::Intersect(Rect rect) {
	return Intersect(*this, rect);
}
inline bool Rect::IntersectsWith(Rect rect) {
	return
		(X < rect.X + rect.Width) &&
		(X + Width > rect.X) &&
		(Y < rect.Y + rect.Height) &&
		(Y + Height > rect.Y);
}
inline Rect Rect::Union(Rect rect) {
	return Union(*this, rect);
}
inline bool Rect::Contains(int x, int y) {
	return (X <= x && Y <= y && X + Width > x && Y + Height > y);
}
inline bool Rect::Contains(Point2 point) {
	return Contains((int)point.X, (int)point.Y);
}
inline bool Rect::Contains(Rect rect) {
	return 
		X <= rect.X &&
		Y <= rect.Y &&
		(rect.X + rect.Width) <= (X + Width) &&
		(rect.Y + rect.Height) <= (Y + Height);
}

inline bool Rect::Equals(Rect rect) {
	return
		X == rect.X && 
		Y == rect.Y && 
		Width == rect.Width && 
		Height == rect.Height;
}
inline bool Rect::Equals(Object^ obj) {
	return obj->GetType() == Rectangle::typeid && Equals((Rectangle)obj);
}
inline int Rect::GetHashCode() {
	return HashCode::Combine(X, Y, Width, Height);
}
inline String^ Rect::ToString() {
	return String::Format("(X:{0} Y:{1} Width:{2} Height:{3})", X, Y, Width, Height);
}

inline Rect Rect::operator+(Rect rect, Point2 point) {
	return Rect((int)(rect.X + point.X), (int)(rect.Y + point.Y), rect.Width, rect.Height);
}
inline Rect Rect::operator-(Rect rect, Point2 point) {
	return Rect((int)(rect.X - point.X), (int)(rect.Y - point.Y), rect.Width, rect.Height);
}
inline Rect Rect::operator+(Rect rect, Media::Size size) {
	return Rect(rect.X, rect.Y, rect.Width + size.Width, rect.Height + size.Height);
}
inline Rect Rect::operator-(Rect rect, Media::Size size) {
	return Rect(rect.X, rect.Y, rect.Width - size.Width, rect.Height - size.Height);
}
inline bool Rect::operator==(Rect left, Rect right) {
	return left.Equals(right);
}
inline bool Rect::operator!=(Rect left, Rect right) {
	return !left.Equals(right);
}