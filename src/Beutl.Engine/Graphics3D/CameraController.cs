namespace Beutl.Graphics.Rendering;

/// <summary>
/// カメラコントローラーの基底クラス
/// </summary>
public abstract class CameraController
{
    protected Camera3D _camera;

    protected CameraController(Camera3D camera)
    {
        _camera = camera;
    }

    public abstract void Update(float deltaTime);
    public abstract void HandleInput(InputState input);
}
