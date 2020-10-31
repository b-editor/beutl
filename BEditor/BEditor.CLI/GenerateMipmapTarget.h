#pragma once

namespace BEditor {
	namespace CLI {
		namespace Graphics {
			public enum class GenerateMipmapTarget {
				Texture1D = 3552,
				Texture2D = 3553,
				Texture3D = 32879,
				TextureCubeMap = 34067,
				Texture1DArray = 35864,
				Texture2DArray = 35866,
				TextureCubeMapArray = 36873,
				Texture2DMultisample = 37120,
				Texture2DMultisampleArray = 37122
			};
		}
	}
}