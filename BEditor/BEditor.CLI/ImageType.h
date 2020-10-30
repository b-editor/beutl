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
				static const int UsrType1 = 7;

				ImageType(int value) {
					this->value = value;
				}

				property int Value { int get(); }
				property int Depth { int get(); }
				property bool IsInterger { bool get(); }
				property int Channels { int get(); }
				property int Bits { int get(); }

				virtual bool Equals(ImageType type);
				virtual bool Equals(Object^ type) override;
				virtual int GetHashCode() override;
				virtual String^ ToString()override;

				static ImageType MakeType(int depth, int channels);
				static ImageType ByteChannel(int ch);
				static ImageType CharChannel(int ch);
				static ImageType UShortChannel(int ch);
				static ImageType ShortChannel(int ch);
				static ImageType IntChannel(int ch);
				static ImageType FloatChannel(int ch);
				static ImageType DoubleChannel(int ch);

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