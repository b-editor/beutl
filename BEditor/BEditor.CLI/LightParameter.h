#pragma once

namespace BEditor {
	namespace CLI {
		namespace Graphics {
            public enum class LightParameter {
                Ambient = 4608,
                Diffuse = 4609,
                Specular = 4610,
                Position = 4611,
                SpotDirection = 4612,
                SpotExponent = 4613,
                SpotCutoff = 4614,
                ConstantAttenuation = 4615,
                LinearAttenuation = 4616,
                QuadraticAttenuation = 4617
            };
		}
	}
}