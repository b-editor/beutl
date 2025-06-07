namespace Beutl.Graphics.Rendering;

/// <summary>
/// 3Dシーンの情報
/// </summary>
public interface I3DScene
{
    IReadOnlyList<I3DRenderableObject> Objects { get; }
    IReadOnlyList<ILight> Lights { get; }
    IEnvironmentMap? EnvironmentMap { get; }
}
