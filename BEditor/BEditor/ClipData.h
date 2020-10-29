#include "pch.h"

using namespace System;
using namespace System::Collections::ObjectModel;
using namespace System::IO;
using namespace System::Linq;
using namespace System::Runtime::Serialization;
using namespace System::Threading::Tasks;

namespace BEditor {
	namespace Data {
		namespace ObjectData {
			public ref class ClipData {
			public:
				ClipData(UInt32 id, ObservableCollection<EffectElement^>^ effects, int start, int end, Type^ type, int layer);
				~ClipData();

			private:

			};
		}
	}
}