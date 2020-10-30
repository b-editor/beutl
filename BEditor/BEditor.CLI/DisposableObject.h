#pragma once

using namespace System;
using namespace System::Collections::Generic;
using namespace System::Text;
using namespace System::Threading;

namespace BEditor {
	namespace CLI {
		public ref class DisposableObject abstract : public IDisposable {
		public:
			property bool IsDisposed {
				bool get() {
					return isDisposed;
				}
			}

			~DisposableObject() {
				OnDispose(true);
				isDisposed = true;
				GC::SuppressFinalize(this);
			}

			!DisposableObject() {
				OnDispose(false);
			}
			void ThrowIfDisposed() {
				if (isDisposed) {
					throw gcnew ObjectDisposedException(GetType()->FullName);
				}
			}
		protected:
			virtual void OnDispose(bool disposing) abstract;
		private:
			bool isDisposed;
		};
	}
}