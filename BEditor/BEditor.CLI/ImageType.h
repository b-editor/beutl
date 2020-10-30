#pragma once

using namespace System;
using namespace System::Collections::Generic;
using namespace System::IO;
using namespace System::Threading::Tasks;

namespace BEditor {
	namespace CLI {
		namespace Media {
			public value class ImageType : public IEquatable<ImageType> {
			public:
				static const int Byte = 0;
				static const int Char = 1;
				static const int UShort = 2;
				static const int Short = 3;
				static const int Int = 4;
				static const int Float = 5;
				static const int Double = 6;
				static const int CV_USRTYPE1 = 7;

				ImageType(int value) {
					this->value = value;
				}

				property int Value {
					int get() {
						return value;
					}
				}
				property int Depth {
					int get() {
						return Value & (DepthMax - 1);
					}
				}
				property bool IsInterger {
					bool get() {
						return Depth < Float;
					}
				}
				property int Channels {
					int get() {
						return (value >> ChannelShift) + 1;
					}
				}
				property int Bits {
					int get() {
						int depth = Depth;

						// if‚Ì‚Ù‚¤‚ª‘‚¢‚Ì‚Å
						if (depth == Byte || depth == Char || depth == CV_USRTYPE1) return 8;
						else if (depth == UShort || depth == Short) return 16;
						else if (depth == Int || depth == Float) return 32;
						else if (depth == Double)return 64;
						else throw gcnew Exception();
					}
				}

				virtual bool Equals(ImageType type) {
					return value == type.value;
				}
				virtual bool Equals(Object^ type) override {
					if (type == nullptr) {
						return false;
					}

					if (type->GetType() != ImageType::typeid) {
						return false;
					}

					return Equals((ImageType)type);
				}
				virtual int GetHashCode() override {
					return value.GetHashCode();
				}
				virtual String^ ToString()override {
					String^ s;
					int depth = Depth;

					if (depth == Byte)s = "Byte";
					else if (depth == Char)s = "Char";
					else if (depth == UShort)s = "UShort";
					else if (depth == Short)s = "Short";
					else if (depth == Int)s = "Int";
					else if (depth == Float)s = "Float";
					else if (depth == Double)s = "Double";
					else if (depth == CV_USRTYPE1)s = "CV_USRTYPE1";
					else return String::Format("Unsupported type value ({0})", value);

					int ch = Channels;
					if (ch <= 4) {
						return s + "Channel" + ch;
					}
					else {
						return s + "Channel(" + ch + ")";
					}
				}

				static ImageType ByteChannel(int ch) {
					return MakeType(Byte, ch);
				}
				static ImageType CharChannel(int ch) {
					return MakeType(Char, ch);
				}
				static ImageType UShortChannel(int ch) {
					return MakeType(UShort, ch);
				}
				static ImageType ShortChannel(int ch) {
					return MakeType(Short, ch);
				}
				static ImageType IntChannel(int ch) {
					return MakeType(Int, ch);
				}
				static ImageType FloatChannel(int ch) {
					return MakeType(Float, ch);
				}
				static ImageType DoubleChannel(int ch) {
					return MakeType(Double, ch);
				}
				static ImageType MakeType(int depth, int channels) {
					if (channels <= 0 || channels >= ChannelMax) {
						throw gcnew	Exception("Channels count should be 1.." + (ChannelMax - 1));
					}

					if (depth < 0 || depth >= DepthMax) {
						throw gcnew Exception("Data type depth should be 0.." + (DepthMax - 1));
					}

					return (depth & (DepthMax - 1)) + ((channels - 1) << ChannelShift);
				}

				static initonly ImageType ByteCh1 = ByteChannel(1);
				static initonly ImageType ByteCh2 = ByteChannel(2);
				static initonly ImageType ByteCh3 = ByteChannel(3);
				static initonly ImageType ByteCh4 = ByteChannel(4);
				static initonly ImageType CharCh1 = CharChannel(1);
				static initonly ImageType CharCh2 = CharChannel(2);
				static initonly ImageType CharCh3 = CharChannel(3);
				static initonly ImageType CharCh4 = CharChannel(4);
				static initonly ImageType UShortCh1 = UShortChannel(1);
				static initonly ImageType UShortCh2 = UShortChannel(2);
				static initonly ImageType UShortCh3 = UShortChannel(3);
				static initonly ImageType UShortCh4 = UShortChannel(4);
				static initonly ImageType ShortCh1 = ShortChannel(1);
				static initonly ImageType ShortCh2 = ShortChannel(2);
				static initonly ImageType ShortCh3 = ShortChannel(3);
				static initonly ImageType ShortCh4 = ShortChannel(4);
				static initonly ImageType IntCh1 = IntChannel(1);
				static initonly ImageType IntCh2 = IntChannel(2);
				static initonly ImageType IntCh3 = IntChannel(3);
				static initonly ImageType IntCh4 = IntChannel(4);
				static initonly ImageType FloatCh1 = FloatChannel(1);
				static initonly ImageType FloatCh2 = FloatChannel(2);
				static initonly ImageType FloatCh3 = FloatChannel(3);
				static initonly ImageType FloatCh4 = FloatChannel(4);
				static initonly ImageType DoubleCh1 = DoubleChannel(1);
				static initonly ImageType DoubleCh2 = DoubleChannel(2);
				static initonly ImageType DoubleCh3 = DoubleChannel(3);
				static initonly ImageType DoubleCh4 = DoubleChannel(4);

				static bool operator ==(ImageType left, ImageType right) {
					return left.Equals(right);
				}
				static bool operator !=(ImageType left, ImageType right) {
					return !left.Equals(right);
				}
				static operator int(ImageType type) {
					return type.value;
				}
				static operator ImageType (int value){
					return ImageType(value);
				}

			private:
				static const int ChannelMax = 512;
				static const int ChannelShift = 3;
				static const int DepthMax = 1 << ChannelShift;

				int value;
			};
		}
	}
}