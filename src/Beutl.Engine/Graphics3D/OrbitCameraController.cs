using System.Numerics;

namespace Beutl.Graphics.Rendering;

/// <summary>
/// 軌道カメラコントローラー（ターゲット中心の回転）
/// </summary>
public class OrbitCameraController : CameraController
{
    public float RotationSpeed { get; set; } = 2.0f;
    public float ZoomSpeed { get; set; } = 1.0f;
    public float PanSpeed { get; set; } = 1.0f;

    public OrbitCameraController(Camera3D camera) : base(camera) { }

    public override void Update(float deltaTime)
    {
        // 自動更新ロジックがあればここに実装
    }

    public override void HandleInput(InputState input)
    {
        if (input.IsMouseButtonDown(MouseButton.Left))
        {
            float yawDelta = input.MouseDelta.X * RotationSpeed * 0.01f;
            float pitchDelta = -input.MouseDelta.Y * RotationSpeed * 0.01f;
            _camera.OrbitAroundTarget(yawDelta, pitchDelta);
        }

        if (input.IsMouseButtonDown(MouseButton.Middle))
        {
            Vector3 right = _camera.Right;
            Vector3 up = _camera.Up;
            Vector3 panDelta = (-right * input.MouseDelta.X + up * input.MouseDelta.Y) * PanSpeed * 0.01f;
            _camera.Translate(panDelta);
        }

        if (MathF.Abs(input.ScrollDelta) > 0.01f)
        {
            _camera.Zoom(-input.ScrollDelta * ZoomSpeed * 0.1f);
        }
    }
}
