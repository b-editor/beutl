#pragma once

using namespace System;

namespace BEditor {
	namespace CLI {
		namespace Media {
			[DataContract(Namespace = "")]
			public value class Range : IEquatable<Range> {
			public:
				property int Start;
				property int End;

				Range(int start, int end);

				static property Range All { Range get(); }
				virtual bool Equals(Range range);
				virtual bool Equals(Object^ obj) override;
				virtual int GetHashCode() override;
				virtual String^ ToString() override;

				static bool operator==(Range left, Range right);
				static bool operator!=(Range left, Range right);
			};
		}
	}
}