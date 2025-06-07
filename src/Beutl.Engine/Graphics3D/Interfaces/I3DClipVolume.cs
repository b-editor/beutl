using System.Numerics;

namespace Beutl.Graphics.Rendering;

/// <summary>
/// 3Dクリッピングボリューム
/// </summary>
public interface I3DClipVolume
{
    bool Contains(Vector3 point);
}
