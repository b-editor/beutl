#include "pch.h"

using namespace System;

using namespace BEditor::CLI::Media;

inline Point2::Point2(int x, int y) {
	this->X = x;
	this->Y = y;
}
inline Point2::Point2(float x, float y) {
	this->X = x;
	this->Y = y;
}
inline Point2::Point2(double x, double y) {
	this->X = (float)x;
	this->Y = (float)y;
}

inline Point2 Point2::Add(Point2 point, Size size) {
	return Point2(
		point.X + size.Width,
		point.Y + size.Height);
}
inline Point2 Point2::Add(Point2 point1, Point2 point2) {
	return Point2(
		point1.X + point2.X,
		point1.Y + point2.Y);
}
inline Point2 Point2::Subtract(Point2 point, Size size) {
	return Point2(
		point.X - size.Width,
		point.Y - size.Height);
}
inline Point2 Point2::Subtract(Point2 point1, Point2 point2) {
	return Point2(
		point1.X - point2.X,
		point1.Y - point2.Y);
}
inline bool Point2::Equals(Object^ obj) {
	return obj->GetType() == Point2::typeid && this->Equals((Point2)obj);
}
inline bool Point2::Equals(Point2 point) {
	return this->X == point.X && this->Y == point.Y;
}
inline int Point2::GetHashCode() {
	return HashCode::Combine(this->X, this->Y);
}
inline String^ Point2::ToString() {
	return String::Format("(X:{0} Y:{1})", this->X, this->Y);
}

inline Point2 Point2::operator+(Point2 point1, Point2 point2) {
	return Add(point1, point2);
}
inline Point2 Point2::operator+(Point2 point, Size size) {
	return Add(point, size);
}
inline Point2 Point2::operator-(Point2 point1, Point2 point2) {
	return Subtract(point1, point2);
}
inline Point2 Point2::operator-(Point2 point, Size size) {
	return Subtract(point, size);
}
inline bool Point2::operator==(Point2 left, Point2 right) {
	return left.Equals(right);
}
inline bool Point2::operator!=(Point2 left, Point2 right) {
	return !left.Equals(right);
}