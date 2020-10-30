#pragma once

using namespace System;

namespace BEditor {
	namespace CLI {
		namespace Exceptions {
			public ref class IntPtrZeroException : ArgumentException {
			public:
				IntPtrZeroException(String^ paramName) :ArgumentException("", paramName) { }
				IntPtrZeroException(String^ message, String^ paramName) :ArgumentException(message, paramName) { }

			private:

			};
		}
	}
}