#include "pch.h"

using namespace System;

using namespace BEditor::CLI::Media;
using Range_ = BEditor::CLI::Media::Range;

inline Range_::Range(int start, int end) {
	Start = start;
	End = end;
}

inline Range_ Range_::All::get() {
	return Range_(int::MinValue, int::MaxValue);
}
inline bool Range_::Equals(Range_ range) {
	return Start == range.Start && End == range.End;
}
inline bool Range_::Equals(Object^ obj) {
	return obj->GetType() == Range_::typeid && Equals((Range_)obj);
}
inline int Range_::GetHashCode() {
	return HashCode::Combine(Start, End);
}
inline String^ Range_::ToString() {
	return String::Format("(Start:{0} End:{1})", Start, End);
}

inline bool Range_::operator==(Range_ left, Range_ right) {
	return left.Equals(right);
}
inline bool Range_::operator!=(Range_ left, Range_ right) {
	return !left.Equals(right);
}