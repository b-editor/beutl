using System;
using System.Collections.Generic;
using System.Text;

namespace BEditor.Graphics {
    public enum MaterialFace {
        Front = 1028,
        Back = 1029,
        FrontAndBack = 1032
    }

    public enum MaterialParameter {
        Ambient = 4608,
        Diffuse = 4609,
        Specular = 4610,
        Emission = 5632,
        Shininess = 5633,
        AmbientAndDiffuse = 5634,
        ColorIndexes = 5635
    }
}
