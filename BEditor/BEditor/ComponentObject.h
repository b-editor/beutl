#include "pch.h"

using namespace System::Collections::Generic;
using namespace System::ComponentModel;
using namespace System::Linq::Expressions;
using namespace System::Runtime::Serialization;

namespace BEditor {
	namespace Data {
		[DataContract(Namespace = "")]
		public ref class ComponentObject abstract : BasePropertyChanged, public IExtensibleDataObject {
		private:
			Dictionary<String^, Object^>^ componentData;

		public:
			virtual property ExtensionDataObject^ ExtensionData;

			property Dictionary<String^, Object^>^ ComponentData {
				Dictionary<String^, Object^>^ get() {
					if (componentData == nullptr) {
						componentData = gcnew Dictionary<String^, Object^>();
					}

					return componentData;
				}
			}
		};
	}
}