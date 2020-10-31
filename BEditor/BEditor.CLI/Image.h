#pragma once
#include <opencv2\opencv.hpp>

using namespace System;
using namespace System::Collections::Generic;
using namespace System::IO;
using namespace System::Threading::Tasks;

namespace BEditor {
	namespace CLI {
		namespace Media {
			public ref class Image : DisposableObject {
			public:
				Image(int width, int height);
				Image(int width, int height, ImageType type);
				Image(int width, int height, ImageType type, IntPtr data);
				Image(cv::Mat* mat);
				Image(Image^ image, Rectangle rect);
				Image(String^ file);
				Image(Stream^ stream, ImageReadMode mode);
				Image(Stream^ stream);
				Image(array<Byte>^ imageByte, ImageReadMode mode);
				Image(array<Byte>^ imageByte);
				Image();

				property cv::Mat* Ptr;
				property IntPtr Data { IntPtr get(); }
				property IntPtr DataStart { IntPtr get(); }
				property IntPtr DataEnd { IntPtr get(); }
				property IntPtr DataLimit { IntPtr get(); }
				property int Width { int get(); }
				property int Height { int get(); }
				property Media::Size Size { Media::Size get(); }
				property ImageType Type { ImageType get(); }
				property long Step { long get(); }
				property int ElemSize { int get(); }
				property bool IsContinuous { bool get(); }
				property bool IsSubmatrix { bool get(); }
				property int Depth { int get(); }
				property int Channels { int get(); }
				property long Total { long get(); }
				property int Dimensions { int get(); }
				property Image^ default[Rectangle] {
					Image^ get(Rectangle roi) {
						return SubMatrix(roi);
					}
					void set(Rectangle roi, Image^ value) {
						if (value == nullptr) throw gcnew ArgumentNullException("value");
						value->ThrowIfDisposed();

						if (Dimensions != value->Dimensions) throw gcnew ArgumentException("value");
						if (roi.Size != value->Size) throw gcnew ArgumentException("value");

						auto sub = SubMatrix(roi);
						value->CopyTo(sub);
					}
				}

				Image^ Clone();
				Image^ Clone(Rectangle rect);
				bool Save(String^ file);
				Image^ SubMatrix(int rowStart, int rowEnd, int colStart, int colEnd);
				Image^ SubMatrix(Range rowRange, Range colRange);
				Image^ SubMatrix(Rectangle roi);
				void CopyTo(Image^ image);
				void Flip(FlipMode mode);
				void AreaExpansion(int top, int bottom, int left, int right);
				void AreaExpansion(int width, int height);
				void Blur(int blurSize, bool alphaBlur);
				void GaussianBlur(int blurSize, bool alphaBlur);
				void MedianBlur(int blurSize, bool alphaBlur);
				void Dilate(int f);
				void Erode(int f);
				void SetColor(Color color);
				void Shadow(float x, float y, int blur, float alpha, Color color);
				void Border(int size, Color color);
				void Clip(int top, int bottom, int left, int right);
				static Image^ Ellipse(int width, int height, int line, Color color);
				static Image^ Rectangle(int width, int height, int line, Color color);
				//static Image^ Text(int size, Color color, String^ text, String^ fontfile, FontStyle style);
				virtual String^ ToString() override;
			protected:
				virtual void OnDispose(bool disposing) override;
			};
		}
	}
}