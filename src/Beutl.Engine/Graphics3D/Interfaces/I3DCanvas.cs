using System.Numerics;
using Beutl.Graphics.Rendering;
using Beutl.Media;

namespace Beutl.Graphics.Rendering;

/// <summary>
/// 3D描画操作を定義するインターフェース
/// </summary>
public interface I3DCanvas : IDisposable, IPopable
{
    /// <summary>
    /// キャンバスのサイズ
    /// </summary>
    PixelSize Size { get; }

    /// <summary>
    /// 破棄されているかどうか
    /// </summary>
    bool IsDisposed { get; }

    /// <summary>
    /// 画面をクリア
    /// </summary>
    void Clear();

    /// <summary>
    /// 指定された色で画面をクリア
    /// </summary>
    void Clear(Color color);

    /// <summary>
    /// 3Dメッシュを描画
    /// </summary>
    void DrawMesh(I3DMesh mesh, I3DMaterial material, Matrix4x4 transform);

    /// <summary>
    /// プリミティブ形状を描画
    /// </summary>
    void DrawCube(Vector3 position, Vector3 scale, I3DMaterial material);
    void DrawSphere(Vector3 position, float radius, I3DMaterial material);
    void DrawPlane(Vector3 position, Vector2 size, I3DMaterial material);

    /// <summary>
    /// ライトを追加
    /// </summary>
    void AddDirectionalLight(DirectionalLight light);
    void AddPointLight(PointLight light);
    void AddSpotLight(SpotLight light);

    /// <summary>
    /// カメラを設定
    /// </summary>
    void SetCamera(I3DCamera camera);

    /// <summary>
    /// 環境マッピングを設定
    /// </summary>
    void SetEnvironmentMap(IEnvironmentMap environmentMap);

    /// <summary>
    /// 3D描画状態をプッシュ
    /// </summary>
    I3DPushedState Push3D();

    /// <summary>
    /// 3D変換をプッシュ
    /// </summary>
    I3DPushedState Push3DTransform(Matrix4x4 transform);

    /// <summary>
    /// 3Dクリッピングをプッシュ
    /// </summary>
    I3DPushedState Push3DClip(I3DClipVolume clipVolume);

    /// <summary>
    /// レンダーターゲットをプッシュ
    /// </summary>
    I3DPushedState Push3DRenderTarget(I3DRenderTarget renderTarget);

    /// <summary>
    /// 変換行列を設定
    /// </summary>
    void PushTransform(Matrix4x4 transform);

    /// <summary>
    /// 変換行列を復元
    /// </summary>
    void PopTransform();

    /// <summary>
    /// メッシュリソースとマテリアルリソースで描画
    /// </summary>
    void DrawMesh(I3DMeshResource meshResource, I3DMaterialResource? materialResource);

    /// <summary>
    /// 環境光を設定
    /// </summary>
    void SetAmbientLight(Vector3 ambientLight);

    /// <summary>
    /// フォグを設定
    /// </summary>
    void SetFog(Vector3 fogColor, float fogDensity);

    /// <summary>
    /// ライトを設定
    /// </summary>
    void SetLights(IList<ILight> lights);

    /// <summary>
    /// 不透明度をプッシュ
    /// </summary>
    void PushOpacity(float opacity);

    /// <summary>
    /// 不透明度をポップ
    /// </summary>
    void PopOpacity();
}
