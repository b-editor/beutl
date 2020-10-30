#pragma once

using namespace System;
using namespace System::Runtime::Serialization;

namespace BEditor {
	namespace CLI {
		namespace Media {
			[DataContract(Namespace = "")]
			public value class Point2 : IEquatable<Point2> {
			public:
				static initonly Point2 Empty = Point2();

				Point2(int x, int y);
				Point2(float x, float y);
				Point2(double x, double y);

				[DataMember(Order = 0)]
				property float X;
				[DataMember(Order = 1)]
				property float Y;

				static Point2 Add(Point2 point, Size size);
				static Point2 Add(Point2 point1, Point2 point2);
				static Point2 Subtract(Point2 point, Size size);
				static Point2 Subtract(Point2 point1, Point2 point2);
				virtual bool Equals(Object^ obj) override;
				virtual bool Equals(Point2 point);
				virtual int GetHashCode() override;
				virtual String^ ToString() override;

				static Point2 operator+(Point2 point1, Point2 point2);
				static Point2 operator+(Point2 point, Size size);
				static Point2 operator-(Point2 point1, Point2 point2);
				static Point2 operator-(Point2 point, Size size);
				static bool operator==(Point2 left, Point2 right);
				static bool operator!=(Point2 left, Point2 right);
			};
		}
	}
}