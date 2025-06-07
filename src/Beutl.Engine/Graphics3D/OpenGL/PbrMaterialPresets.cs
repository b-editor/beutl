using System.Numerics;

namespace Beutl.Graphics.Rendering.OpenGL;

/// <summary>
/// PBRマテリアルプリセット
/// </summary>
public static class PbrMaterialPresets
{
    /// <summary>
    /// 一般的なマテリアルのPBRパラメータ
    /// </summary>
    public static readonly Dictionary<string, (Vector3 albedo, float metallic, float roughness)> Materials = new()
    {
        ["Water"] = (new Vector3(0.4f, 0.55f, 0.75f), 0.0f, 0.1f),
        ["Ice"] = (new Vector3(0.7f, 0.9f, 1.0f), 0.0f, 0.05f),
        ["Snow"] = (new Vector3(0.9f, 0.9f, 0.9f), 0.0f, 0.9f),
        ["Sand"] = (new Vector3(0.76f, 0.70f, 0.50f), 0.0f, 0.95f),
        ["Concrete"] = (new Vector3(0.6f, 0.6f, 0.6f), 0.0f, 0.9f),
        ["Brick"] = (new Vector3(0.7f, 0.31f, 0.23f), 0.0f, 0.85f),
        ["Wood"] = (new Vector3(0.48f, 0.32f, 0.18f), 0.0f, 0.8f),
        ["Leather"] = (new Vector3(0.4f, 0.25f, 0.15f), 0.0f, 0.7f),
        ["Fabric"] = (new Vector3(0.6f, 0.6f, 0.6f), 0.0f, 0.9f),
        ["Chrome"] = (new Vector3(0.55f, 0.55f, 0.55f), 1.0f, 0.05f),
        ["Aluminum"] = (new Vector3(0.91f, 0.92f, 0.92f), 1.0f, 0.1f),
        ["Titanium"] = (new Vector3(0.54f, 0.50f, 0.45f), 1.0f, 0.2f),
    };

    /// <summary>
    /// 名前からマテリアルを作成
    /// </summary>
    public static BasicMaterial CreateMaterial(string name)
    {
        if (Materials.TryGetValue(name, out var parameters))
        {
            return new BasicMaterial(parameters.albedo, parameters.metallic, parameters.roughness);
        }
        throw new ArgumentException($"Unknown material: {name}");
    }

    /// <summary>
    /// 利用可能なマテリアル名の一覧を取得
    /// </summary>
    public static IReadOnlyList<string> GetAvailableMaterials()
    {
        return Materials.Keys.ToList();
    }
}
