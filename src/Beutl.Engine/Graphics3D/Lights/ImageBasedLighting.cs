namespace Beutl.Graphics.Rendering;

/// <summary>
/// イメージベースドライティング（IBL）設定
/// </summary>
public class ImageBasedLighting
{
    /// <summary>
    /// 環境マップテクスチャ
    /// </summary>
    public ITexture? EnvironmentMap { get; set; }

    /// <summary>
    /// 事前計算されたイラディアンステクスチャ
    /// </summary>
    public ITexture? IrradianceMap { get; set; }

    /// <summary>
    /// 事前フィルタされたキューブマップ
    /// </summary>
    public ITexture? PrefilterMap { get; set; }

    /// <summary>
    /// BRDF統合ルックアップテクスチャ
    /// </summary>
    public ITexture? BrdfLut { get; set; }

    /// <summary>
    /// IBLの強度
    /// </summary>
    public float Intensity { get; set; } = 1.0f;

    /// <summary>
    /// IBLが有効かどうか
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// IBL用の事前フィルタ処理レベル数
    /// </summary>
    public int PrefilterLevels { get; set; } = 5;
}
