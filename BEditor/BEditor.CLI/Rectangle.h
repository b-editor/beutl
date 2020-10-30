#pragma once

using namespace System;
using namespace System::Runtime::Serialization;

namespace BEditor {
	namespace CLI {
		namespace Media {
			[DataContract(Namespace = "")]
			public value class Rectangle : IEquatable<Rectangle> {
			public:
				static initonly Rectangle Empty = Rectangle();

				Rectangle(int x, int y, int width, int height);
				Rectangle(Point2 point, Size size);

				[DataMember(Order = 0)]
				property int X;
				[DataMember(Order = 1)]
				property int Y;
				[DataMember(Order = 2)]
				property int Width;
				[DataMember(Order = 3)]
				property int Height;

				property int Top { int get(); void set(int value); }
				property int Bottom { int get(); }
				property int Left { int get(); void set(int value); }
				property int Right { int get(); }
				property Point2 TopLeft { Point2 get(); }
				property Point2 BottomRight { Point2 get(); }
				property Point2 Point { Point2 get(); void set(Point2 value); }
				property Media::Size Size { Media::Size get(); void set(Media::Size value); }

				static Rectangle FromLTRB(int left, int top, int right, int bottom);
				static Rectangle Inflate(Rectangle rect, int x, int y);
				static Rectangle Intersect(Rectangle a, Rectangle b);
				static Rectangle Union(Rectangle a, Rectangle b);
				void Inflate(int width, int height);
				void Inflate(Media::Size size);
				Rectangle Intersect(Rectangle rect);
				bool IntersectsWith(Rectangle rect);
				Rectangle Union(Rectangle rect);
				bool Contains(int x, int y);
				bool Contains(Point2 point);
				bool Contains(Rectangle rect);
				virtual bool Equals(Rectangle rect);
				virtual bool Equals(Object^ obj) override;
				virtual int GetHashCode() override;
				virtual String^ ToString() override;

				static Rectangle operator+(Rectangle rect, Point2 point);
				static Rectangle operator-(Rectangle rect, Point2 point);
				static Rectangle operator+(Rectangle rect, Media::Size size);
				static Rectangle operator-(Rectangle rect, Media::Size size);
				static bool operator==(Rectangle left, Rectangle right);
				static bool operator!=(Rectangle left, Rectangle right);
			private:

			};
		}
	}
}