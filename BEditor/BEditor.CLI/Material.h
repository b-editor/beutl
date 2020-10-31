#pragma once

namespace BEditor {
	namespace CLI {
		namespace Graphics {
			public enum class MaterialFace {
				Front = 1028,
				Back = 1029,
				FrontAndBack = 1032
			};

			public enum class MaterialParameter {
				Ambient = 4608,
				Diffuse = 4609,
				Specular = 4610,
				Emission = 5632,
				Shininess = 5633,
				AmbientAndDiffuse = 5634,
				ColorIndexes = 5635
			};
		}
	}
}