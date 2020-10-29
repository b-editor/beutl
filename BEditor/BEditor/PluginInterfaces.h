#include "pch.h"

using namespace System;
using namespace System::Runtime;
using namespace System::Collections::Generic;

namespace BEditor {
	namespace Plugin {
		public interface class IPlugin {
		public:
			property String^ PluginName;
			property String^ Infomation;
		};

		public interface class IEasingFunctions {
		public:
			property List<Tuple<String^, Type^>^>^ EasingFunc;
		};

		public interface class IEffects {
		public:
			property List<Tuple<String^, Type^>^>^ Effects;
		};

		public interface class IObjects {
		public:
			property List<Tuple<String^, Type^>^>^ Objects;
		};
	}
}