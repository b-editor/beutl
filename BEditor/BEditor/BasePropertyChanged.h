#include "pch.h"

using namespace System;
using namespace System::ComponentModel;
using namespace System::Diagnostics;
using namespace System::Linq;
using namespace System::Linq::Expressions;
using namespace System::Runtime::Serialization;

namespace BEditor {
	namespace Data {
		[DataContract(Namespace = "")]
		public ref class BasePropertyChanged abstract : public INotifyPropertyChanged {
		public:
			virtual event PropertyChangedEventHandler^ PropertyChanged;

		protected:
			generic<typename T> void SetValue(T src, T% dst, String^ name);
			void RaisePropertyChanged(String^ name);
		};
	}
}

