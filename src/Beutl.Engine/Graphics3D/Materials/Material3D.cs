using System;
using Beutl.Engine;
using Beutl.Media;

namespace Beutl.Graphics3D.Materials;

/// <summary>
/// Base class for 3D materials.
/// </summary>
public abstract partial class Material3D : EngineObject
{
    public Material3D()
    {
        ScanProperties<Material3D>();
    }
}
