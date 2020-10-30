#include "pch.h"

using namespace System;

using namespace BEditor::CLI::Media;

inline Point3::Point3(int x, int y, int z) {
	X = x;
	Y = y;
	Z = z;
}
inline Point3::Point3(float x, float y, float z) {
	X = x;
	Y = y;
	Z = z;
}
inline Point3::Point3(double x, double y, double z) {
	X = (float)x;
	Y = (float)y;
	Z = (float)z;
}

inline Point3 Point3::Add(Point3 point, Size size) {
	return Point3(
		point.X + size.Width,
		point.Y + size.Height,
		point.Z);
}
inline Point3 Point3::Add(Point3 point1, Point3 point2) {
	return Point3(
		point1.X + point2.X,
		point1.Y + point2.Y,
		point1.Z + point2.Z);
}
inline Point3 Point3::Subtract(Point3 point, Size size) {
	return Point3(
		point.X - size.Width,
		point.Y - size.Height,
		point.Z);
}
inline Point3 Point3::Subtract(Point3 point1, Point3 point2) {
	return Point3(
		point1.X - point2.X,
		point1.Y - point2.Y,
		point1.Z - point2.Z);
}
inline bool Point3::Equals(Object^ obj) {
	return obj->GetType() == Point3::typeid && Equals((Point3)obj);
}
inline bool Point3::Equals(Point3 point) {
	return
		X == point.X &&
		Y == point.Y &&
		Z == point.Z;
}
inline int Point3::GetHashCode() {
	return HashCode::Combine(X, Y, Z);
}
inline String^ Point3::ToString() {
	return String::Format("(X:{0} Y:{1} Z{2})", X, Y, Z);
}

inline Point3 Point3::operator+(Point3 point1, Point3 point2) {
	return Add(point1, point2);
}
inline Point3 Point3::operator+(Point3 point, Size size) {
	return Add(point, size);
}
inline Point3 Point3::operator-(Point3 point1, Point3 point2) {
	return Subtract(point1, point2);
}
inline Point3 Point3::operator-(Point3 point, Size size) {
	return Subtract(point, size);
}
inline bool Point3::operator==(Point3 left, Point3 right) {
	return left.Equals(right);
}
inline bool Point3::operator!=(Point3 left, Point3 right) {
	return !left.Equals(right);
}