using Beutl.Collections;
using Beutl.Media;

namespace Beutl.Graphics.Rendering;

/// <summary>
/// Light3Dオブジェクトのコレクション
/// </summary>
public class Light3Ds : AffectsRenders<Light3D>
{
    /// <summary>
    /// 表示可能で有効なライトのみを取得
    /// </summary>
    public IEnumerable<Light3D> GetVisible()
    {
        return this.Where(light => light.IsVisible && light.Enabled);
    }

    /// <summary>
    /// 指定した型のライトを取得
    /// </summary>
    public IEnumerable<T> OfType<T>() where T : Light3D
    {
        return this.Where(light => light is T).Cast<T>();
    }
}
