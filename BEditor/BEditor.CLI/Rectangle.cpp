#include "pch.h"
#include <math.h>

using namespace System;

using namespace BEditor::CLI::Media;

inline Rectangle::Rectangle(int x, int y, int width, int height) {
	X = x;
	Y = y;
	Width = width;
	Height = height;
}
inline Rectangle::Rectangle(Point2 point, Media::Size size) {
	X = point.X;
	Y = point.Y;
	Width = size.Width;
	Height = size.Height;
}

inline int Rectangle::Top::get() {
	return Y;
}
inline void Rectangle::Top::set(int value) {
	Y = value;
}

inline int Rectangle::Bottom::get() {
	return Y + Height;
}

inline int Rectangle::Left::get() {
	return X;
}
inline void Rectangle::Left::set(int value) {
	X = value;
}

inline int Rectangle::Right::get() {
	return X + Width;
}

inline Point2 Rectangle::TopLeft::get() {
	return Point2(X, Y);
}
inline Point2 Rectangle::BottomRight::get() {
	return Point2(X + Width, Y + Height);
}

inline Point2 Rectangle::Point::get() {
	return Point2(X, Y);
}
inline void Rectangle::Point::set(Point2 value) {
	X = (int)value.X;
	Y = (int)value.Y;
}

inline BEditor::CLI::Media::Size Rectangle::Size::get() {
	return BEditor::CLI::Media::Size(Width, Height);
}
inline void Rectangle::Size::set(BEditor::CLI::Media::Size value) {
	Width = value.Width;
	Height = value.Height;
}


inline Rectangle Rectangle::FromLTRB(int left, int top, int right, int bottom) {
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
inline Rectangle Rectangle::Inflate(Rectangle rect, int x, int y) {
	rect.Inflate(x, y);
	return rect;
}
inline Rectangle Rectangle::Intersect(Rectangle a, Rectangle b) {
	int x1 = std::max(a.X, b.X);
	int x2 = std::min(a.X + a.Width, b.X + b.Width);
	int y1 = std::max(a.Y, b.Y);
	int y2 = std::min(a.Y + a.Height, b.Y + b.Height);
	
	if (x2 >= x1 && y2 >= y1)
		return Rectangle(x1, y1, x2 - x1, y2 - y1);
	return Empty;
}
inline Rectangle Rectangle::Union(Rectangle a, Rectangle b) {
	int x1 = std::min(a.X, b.X);
	int x2 = std::max(a.X + a.Width, b.X + b.Width);
	int y1 = std::min(a.Y, b.Y);
	int y2 = std::max(a.Y + a.Height, b.Y + b.Height);

	return Rectangle(x1, y1, x2 - x1, y2 - y1);
}

inline void Rectangle::Inflate(int width, int height) {
	X -= width;
	Y -= height;
	Width += (2 * width);
	Height += (2 * height);
}
inline void Rectangle::Inflate(Media::Size size) {
	this->Inflate(size.Width, size.Height);
}
inline Rectangle Rectangle::Intersect(Rectangle rect) {
	return Intersect(*this, rect);
}
inline bool Rectangle::IntersectsWith(Rectangle rect) {
	return
		(X < rect.X + rect.Width) &&
		(X + Width > rect.X) &&
		(Y < rect.Y + rect.Height) &&
		(Y + Height > rect.Y);
}
inline Rectangle Rectangle::Union(Rectangle rect) {
	return Union(*this, rect);
}
inline bool Rectangle::Contains(int x, int y) {
	return (X <= x && Y <= y && X + Width > x && Y + Height > y);
}
inline bool Rectangle::Contains(Point2 point) {
	return Contains((int)point.X, (int)point.Y);
}
inline bool Rectangle::Contains(Rectangle rect) {
	return 
		X <= rect.X &&
		Y <= rect.Y &&
		(rect.X + rect.Width) <= (X + Width) &&
		(rect.Y + rect.Height) <= (Y + Height);
}

inline bool Rectangle::Equals(Rectangle rect) {
	return
		X == rect.X && 
		Y == rect.Y && 
		Width == rect.Width && 
		Height == rect.Height;
}
inline bool Rectangle::Equals(Object^ obj) {
	return obj->GetType() == Rectangle::typeid && Equals((Rectangle)obj);
}
inline int Rectangle::GetHashCode() {
	return HashCode::Combine(X, Y, Width, Height);
}
inline String^ Rectangle::ToString() {
	return String::Format("(X:{0} Y:{1} Width:{2} Height:{3})", X, Y, Width, Height);
}

inline Rectangle Rectangle::operator+(Rectangle rect, Point2 point) {
	return Rectangle((int)(rect.X + point.X), (int)(rect.Y + point.Y), rect.Width, rect.Height);
}
inline Rectangle Rectangle::operator-(Rectangle rect, Point2 point) {
	return Rectangle((int)(rect.X - point.X), (int)(rect.Y - point.Y), rect.Width, rect.Height);
}
inline Rectangle Rectangle::operator+(Rectangle rect, Media::Size size) {
	return Rectangle(rect.X, rect.Y, rect.Width + size.Width, rect.Height + size.Height);
}
inline Rectangle Rectangle::operator-(Rectangle rect, Media::Size size) {
	return Rectangle(rect.X, rect.Y, rect.Width - size.Width, rect.Height - size.Height);
}
inline bool Rectangle::operator==(Rectangle left, Rectangle right) {
	return left.Equals(right);
}
inline bool Rectangle::operator!=(Rectangle left, Rectangle right) {
	return !left.Equals(right);
}