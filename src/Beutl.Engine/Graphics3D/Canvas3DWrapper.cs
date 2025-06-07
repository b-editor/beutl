using Beutl.Graphics.Effects;
using Beutl.Media;

namespace Beutl.Graphics.Rendering;

/// <summary>
/// 2Dキャンバスをラップした3Dキャンバス実装（簡略版）
/// </summary>
internal class Canvas3DWrapper : I3DCanvas
{
    private readonly ICanvas _canvas2D;
    private readonly Rendering3DManager _manager;
    private readonly I3DRenderTarget _renderTarget;
    private bool _disposed;
    private I3DCanvas _3dCanvasImplementation;

    public PixelSize Size { get; }
    public bool IsDisposed => _disposed;

    public Canvas3DWrapper(ICanvas canvas2D, Rendering3DManager manager, int width, int height)
    {
        _canvas2D = canvas2D;
        _manager = manager;
        Size = new PixelSize(width, height);
        
        // 3D用のレンダーターゲットを作成
        _renderTarget = _manager.CreateRenderTarget(width, height) 
                        ?? throw new InvalidOperationException("Failed to create 3D render target");
    }

    public void Clear() => _canvas2D.Clear();
    public void Clear(Color color) => _canvas2D.Clear(color);

    public void DrawMesh(I3DMesh mesh, I3DMaterial material, System.Numerics.Matrix4x4 transform)
    {
        // 3Dメッシュ描画の実装
        // 実際の実装では、3Dメッシュを現在のシーンに追加
    }

    public void DrawCube(System.Numerics.Vector3 position, System.Numerics.Vector3 scale, I3DMaterial material)
    {
        var mesh = BasicMesh.CreateCube();
        var transform = System.Numerics.Matrix4x4.CreateScale(scale) * System.Numerics.Matrix4x4.CreateTranslation(position);
        DrawMesh(mesh, material, transform);
    }

    public void DrawSphere(System.Numerics.Vector3 position, float radius, I3DMaterial material)
    {
        var mesh = BasicMesh.CreateSphere(radius);
        var transform = System.Numerics.Matrix4x4.CreateTranslation(position);
        DrawMesh(mesh, material, transform);
    }

    public void DrawPlane(System.Numerics.Vector3 position, System.Numerics.Vector2 size, I3DMaterial material)
    {
        var mesh = BasicMesh.CreatePlane(size);
        var transform = System.Numerics.Matrix4x4.CreateTranslation(position);
        DrawMesh(mesh, material, transform);
    }

    public void AddDirectionalLight(DirectionalLight light) { /* 実装 */ }
    public void AddPointLight(PointLight light) { /* 実装 */ }
    public void AddSpotLight(SpotLight light) { /* 実装 */ }
    public void SetCamera(I3DCamera camera) { /* 実装 */ }
    public void SetEnvironmentMap(IEnvironmentMap environmentMap) { /* 実装 */ }

    public I3DPushedState Push3D() => new DummyPushedState();
    public I3DPushedState Push3DTransform(System.Numerics.Matrix4x4 transform) => new DummyPushedState();
    public I3DPushedState Push3DClip(I3DClipVolume clipVolume) => new DummyPushedState();
    public I3DPushedState Push3DRenderTarget(I3DRenderTarget renderTarget) => new DummyPushedState();

    public PushedState Push() => _canvas2D.Push();
    public PushedState PushLayer(Rect limit = default) => _canvas2D.PushLayer(limit);
    public PushedState PushClip(Rect clip, ClipOperation operation = ClipOperation.Intersect) => _canvas2D.PushClip(clip, operation);
    public PushedState PushClip(Geometry geometry, ClipOperation operation = ClipOperation.Intersect) => _canvas2D.PushClip(geometry, operation);
    public PushedState PushOpacity(float opacity) => _canvas2D.PushOpacity(opacity);
    public PushedState PushOpacityMask(IBrush mask, Rect bounds, bool invert = false) => _canvas2D.PushOpacityMask(mask, bounds, invert);
    public PushedState PushFilterEffect(FilterEffect effect) => _canvas2D.PushFilterEffect(effect);
    public PushedState PushBlendMode(BlendMode blendMode) => _canvas2D.PushBlendMode(blendMode);
    public PushedState PushTransform(Matrix matrix, TransformOperator transformOperator = TransformOperator.Prepend) => _canvas2D.PushTransform(matrix, transformOperator);
    public void Pop(int count) => _canvas2D.Pop(count);

    public void Dispose()
    {
        if (_disposed)
            return;

        _renderTarget?.Dispose();
        _disposed = true;
    }

    private class DummyPushedState : I3DPushedState
    {
        public void Dispose() { }
    }
}
