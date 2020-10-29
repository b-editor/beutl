#include "pch.h"

using namespace System;
using namespace System::Collections::Generic;
using namespace System::Collections::ObjectModel;
using namespace System::Linq;
using namespace System::Runtime::Serialization;
using namespace System::Threading::Tasks;

namespace BEditor {
	namespace Data {
		namespace EffectData {
			public ref class EffectElement abstract : ComponentObject {
			public:
				property String^ Name {
					virtual String^ get() abstract;
				}
			private:

			};
		}
	}
}