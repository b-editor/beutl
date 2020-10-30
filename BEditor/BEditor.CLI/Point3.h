#pragma once

using namespace System;
using namespace System::Runtime::Serialization;

namespace BEditor {
	namespace CLI {
		namespace Media {
			[DataContract(Namespace = "")]
			public value class Point3 : IEquatable<Point3> {
			public:
				static initonly Point3 Empty = Point3();

				Point3(int x, int y, int z);
				Point3(float x, float y, float z);
				Point3(double x, double y, double z);

				[DataMember(Order = 0)]
				property float X;
				[DataMember(Order = 1)]
				property float Y;
				[DataMember(Order = 2)]
				property float Z;

				static Point3 Add(Point3 point, Size size);
				static Point3 Add(Point3 point1, Point3 point2);
				static Point3 Subtract(Point3 point, Size size);
				static Point3 Subtract(Point3 point1, Point3 point2);
				virtual bool Equals(Object^ obj) override;
				virtual bool Equals(Point3 point);
				virtual int GetHashCode() override;
				virtual String^ ToString() override;

				static Point3 operator+(Point3 point1, Point3 point2);
				static Point3 operator+(Point3 point, Size size);
				static Point3 operator-(Point3 point1, Point3 point2);
				static Point3 operator-(Point3 point, Size size);
				static bool operator==(Point3 left, Point3 right);
				static bool operator!=(Point3 left, Point3 right);
			};
		}
	}
}