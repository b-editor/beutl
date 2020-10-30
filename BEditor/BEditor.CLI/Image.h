#pragma once
#include "ImageType.h"
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
				Image(int width, int height){ }
				Image(int width, int height, ImageType type) { }
				Image(int width, int height, ImageType type, IntPtr data){ }
				Image(IntPtr mat){ }

			};
		}
	}
}