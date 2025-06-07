namespace Beutl.Graphics.Rendering;

/// <summary>
/// 3Dダミーオブジェクト（無効なDrawable3D用）
/// </summary>
public class DummyDrawable3D : Drawable3D
{
    private static readonly I3DMeshResource s_dummyMesh = CreateDummyMesh();

    public override I3DMeshResource Mesh => s_dummyMesh;

    protected override void RenderCore3D(I3DCanvas canvas)
    {
        // 何もレンダリングしない
    }

    private static I3DMeshResource CreateDummyMesh()
    {
        // 最小限のダミーメッシュを作成
        var mesh = BasicMesh.CreateCube(0.1f);
        var renderer = Scene3DManager.Current?.Renderer;
        return renderer?.CreateMesh(mesh) ?? throw new InvalidOperationException("3D renderer is not available");
    }
}
