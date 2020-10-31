#include "pch.h"

using namespace System;

using namespace BEditor::CLI::Media;

inline Color::Color(int r, int g, int b, int a) {
	R = r;
	G = g;
	B = b;
	A = a;
}
inline Color::Color(Byte r, Byte g, Byte b, Byte a) {
	R = r;
	G = g;
	B = b;
	A = a;
}
inline Color::Color(float r, float g, float b, float a) {
	R = r * 255;
	G = g * 255;
	B = b * 255;
	A = a * 255;
}

inline float Color::ScR::get() {
	return R / 255;
}
inline float Color::ScG::get() {
	return G / 255;
}
inline float Color::ScB::get() {
	return B / 255;
}
inline float Color::ScA::get() {
	return A / 255;
}
inline void Color::ScR::set(float value) {
	R = value * 255;
}
inline void Color::ScG::set(float value) {
	G = value * 255;
}
inline void Color::ScB::set(float value) {
	B = value * 255;
}
inline void Color::ScA::set(float value) {
	A = value * 255;
}

inline bool Color::Equals(Color color) {
	return
		R == color.R &&
		G == color.G &&
		B == color.B &&
		A == color.A;
}
inline bool Color::Equals(Object^ obj) {
	return obj->GetType() == Color::typeid && Equals((Color)obj);
}
inline int Color::GetHashCode() {
	return HashCode::Combine(R, G, B, A);
}
inline String^ Color::ToString() {
	return String::Format("(Red:{0} Green:{1} Blue:{2} Alpha:{3})", R, G, B, A);
}
inline bool Color::operator==(Color left, Color right) {
	return left.Equals(right);
}
inline bool Color::operator!=(Color left, Color right) {
	return !left.Equals(right);
}