namespace Beutl.Graphics.Rendering;

/// <summary>
/// ライティング環境の総合管理
/// </summary>
public class LightingEnvironment
{
    private readonly List<ILight> _lights = [];

    /// <summary>
    /// 全てのライト
    /// </summary>
    public IReadOnlyList<ILight> Lights => _lights;

    /// <summary>
    /// 環境光設定
    /// </summary>
    public AmbientLight AmbientLight { get; set; } = new();

    /// <summary>
    /// イメージベースドライティング設定
    /// </summary>
    public ImageBasedLighting ImageBasedLighting { get; set; } = new();

    /// <summary>
    /// ライトを追加
    /// </summary>
    public void AddLight(ILight light)
    {
        _lights.Add(light);
    }

    /// <summary>
    /// ライトを削除
    /// </summary>
    public bool RemoveLight(ILight light)
    {
        return _lights.Remove(light);
    }

    /// <summary>
    /// 全てのライトをクリア
    /// </summary>
    public void ClearLights()
    {
        _lights.Clear();
    }

    /// <summary>
    /// 有効なライトのみを取得
    /// </summary>
    public IEnumerable<ILight> GetActiveLights()
    {
        return _lights.Where(light => light.Enabled);
    }

    /// <summary>
    /// 指定された種類のライトを取得
    /// </summary>
    public IEnumerable<T> GetLightsOfType<T>() where T : class, ILight
    {
        return _lights.OfType<T>();
    }
}
