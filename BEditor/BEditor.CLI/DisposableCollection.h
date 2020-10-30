#pragma once

using namespace System;
using namespace System::Collections::Generic;
using namespace System::Text;
using namespace System::Threading;

namespace BEditor {
	namespace CLI {
		public ref class DisposableCollection : List<IDisposable^>, public ICollection<IDisposable^>, IEnumerable<IDisposable^>, IList<IDisposable^>, IReadOnlyCollection<IDisposable^>, IReadOnlyList<IDisposable^>, IDisposable {
		public:
			~DisposableCollection() {
				List<IDisposable^>^ base = this;
				for each (IDisposable^ disposable in base) {
					delete disposable;
				}
			}
		};
	}
}