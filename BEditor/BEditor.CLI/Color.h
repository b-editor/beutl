#pragma once

using namespace System;
using namespace System::Runtime::Serialization;

namespace BEditor {
	namespace CLI {
		namespace Media {
			[DataContract(Namespace = "")]
			public value class Color : IEquatable<Color> {
			public:
				static initonly Color White = Color(255, 255, 255, 255);
				static initonly Color Black = Color(0, 0, 0, 255);
				static initonly Color Red = Color(244, 67, 54, 255);
				static initonly Color Pink = Color(233, 30, 99, 255);
				static initonly Color Purple = Color(156, 39, 176, 255);
				static initonly Color DeepPurple = Color(103, 58, 183, 255);
				static initonly Color Indigo = Color(63, 81, 181, 255);
				static initonly Color Blue = Color(33, 150, 243, 255);
				static initonly Color LightBlue = Color(3, 169, 244, 255);
				static initonly Color Cyan = Color(0, 188, 212, 255);
				static initonly Color Teal = Color(0, 150, 136, 255);
				static initonly Color Green = Color(76, 175, 80, 255);
				static initonly Color LightGreen = Color(139, 195, 74, 255);
				static initonly Color Lime = Color(205, 220, 57, 255);
				static initonly Color Yellow = Color(255, 235, 59, 255);
				static initonly Color Amber = Color(255, 193, 7, 255);
				static initonly Color Orange = Color(255, 152, 0, 255);
				static initonly Color DeepOrange = Color(255, 87, 34, 255);
				static initonly Color Brown = Color(121, 85, 72, 255);
				static initonly Color Grey = Color(158, 158, 158, 255);
				static initonly Color BlueGrey = Color(96, 125, 139, 255);

				Color(int r, int g, int b, int a);
				Color(Byte r, Byte g, Byte b, Byte a);
				Color(float r, float g, float b, float a);

				[DataMember(Order = 0)]
				property float R;
				[DataMember(Order = 1)]
				property float G;
				[DataMember(Order = 2)]
				property float B;
				[DataMember(Order = 3)]
				property float A;

				virtual bool Equals(Color color);
				virtual bool Equals(Object^ obj) override;
				virtual int GetHashCode() override;
				virtual String^ ToString() override;
				static bool operator==(Color left, Color right);
				static bool operator!=(Color left, Color right);
			};
		}
	}
}