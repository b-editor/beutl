#include "pch.h"

using namespace System;
using namespace System::Collections::ObjectModel;
using namespace System::IO;
using namespace System::Runtime::Serialization;

namespace BEditor {
	namespace Data {
		namespace ProjectData {
			public ref class Scene : ComponentObject {
			public:
				Scene(int width, int height);
				Scene(int width, int height, ObservableCollection<ClipData^>^ datas)
			};
		}
	}
}