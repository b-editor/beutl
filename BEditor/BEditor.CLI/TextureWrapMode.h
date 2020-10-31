#pragma once

namespace BEditor {
	namespace CLI {
		namespace Graphics {
			public enum class TextureWrapMode {
				Clamp = 10496,
				Repeat = 10497,
				ClampToBorder = 33069,
				ClampToBorderArb = 33069,
				ClampToBorderNv = 33069,
				ClampToBorderSgis = 33069,
				ClampToEdge = 33071,
				ClampToEdgeSgis = 33071,
				MirroredRepeat = 33648
			};
		}
	}
}