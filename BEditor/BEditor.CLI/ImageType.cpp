#include "pch.h"

using namespace System;
using namespace System::Collections::Generic;
using namespace System::IO;
using namespace System::Threading::Tasks;

using namespace BEditor::CLI::Media;

inline int ImageType::Value::get() {
	return this->value;
}
inline int ImageType::Depth::get() {
	return this->value & (DepthMax - 1);
}
inline bool ImageType::IsInterger::get() {
	return this->Depth < Float;
}
inline int ImageType::Channels::get() {
	return (this->value >> ChannelShift) + 1;
}
inline int ImageType::Bits::get() {
	int depth = Depth;

	// if‚Ì‚Ù‚¤‚ª‘‚¢‚Ì‚Å
	if (depth == Byte || depth == Char || depth == UsrType1) return 8;
	else if (depth == UShort || depth == Short) return 16;
	else if (depth == Int || depth == Float) return 32;
	else if (depth == Double)return 64;
	else throw gcnew Exception();
}

inline bool ImageType::Equals(ImageType type) {
	return this->value == type.value;
}
inline bool ImageType::Equals(Object^ obj) {
	if (obj == nullptr) {
		return false;
	}

	if (obj->GetType() != ImageType::typeid) {
		return false;
	}

	return Equals((ImageType)obj);
}
inline int ImageType::GetHashCode() {
	return this->value.GetHashCode();
}
inline String^ ImageType::ToString() {
	String^ s;
	int depth = Depth;

	if (depth == Byte)s = "Byte";
	else if (depth == Char)s = "Char";
	else if (depth == UShort)s = "UShort";
	else if (depth == Short)s = "Short";
	else if (depth == Int)s = "Int";
	else if (depth == Float)s = "Float";
	else if (depth == Double)s = "Double";
	else if (depth == UsrType1)s = "CV_USRTYPE1";
	else return String::Format("Unsupported type value ({0})", value);

	int ch = Channels;
	if (ch <= 4) {
		return s + "Channel" + ch;
	}
	else {
		return s + "Channel(" + ch + ")";
	}
}

inline ImageType ImageType::MakeType(int depth, int channels) {
	if (channels <= 0 || channels >= ChannelMax) {
		throw gcnew	Exception("Channels count should be 1.." + (ChannelMax - 1));
	}

	if (depth < 0 || depth >= DepthMax) {
		throw gcnew Exception("Data type depth should be 0.." + (DepthMax - 1));
	}

	return (depth & (DepthMax - 1)) + ((channels - 1) << ChannelShift);
}
inline ImageType ImageType::ByteChannel(int ch) {
	return MakeType(Byte, ch);
}
inline ImageType ImageType::CharChannel(int ch) {
	return MakeType(Char, ch);
}
inline ImageType ImageType::UShortChannel(int ch) {
	return MakeType(UShort, ch);
}
inline ImageType ImageType::ShortChannel(int ch) {
	return MakeType(Short, ch);
}
inline ImageType ImageType::IntChannel(int ch) {
	return MakeType(Int, ch);
}
inline ImageType ImageType::FloatChannel(int ch) {
	return MakeType(Float, ch);
}
inline ImageType ImageType::DoubleChannel(int ch) {
	return MakeType(Double, ch);
}