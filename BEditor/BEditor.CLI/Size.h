#pragma once

using namespace System;
using namespace System::Runtime::Serialization;

namespace BEditor {
	namespace CLI {
		namespace Media {
			public value class Size : IEquatable<Size> {
			public:
				static initonly Size Empty = Size();

				Size(int width, int height);

				[DataMember(Order = 0)]
				property int Width { int get(); void set(int value); }
				[DataMember(Order = 1)]
				property int Height { int get(); void set(int value); }
				property float Aspect { float get(); }

				static Size Add(Size size1, Size size2);
				static Size Subtract(Size size1, Size size2);
				virtual bool Equals(Size other);
				virtual bool Equals(Object^ obj) override;
				virtual String^ ToString() override;
				virtual int GetHashCode() override;

				static Size operator+(Size size1, Size size2);
				static Size operator-(Size size1, Size size2);
				static Size operator*(Size left, int right);
				static Size operator/(Size left, int right);
				static bool operator==(Size left, Size right);
				static bool operator!=(Size left, Size right);
			private:
				int width;
				int height;
			};
		}
	}
}