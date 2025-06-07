namespace Beutl.Graphics.Rendering;

/// <summary>
/// 自由視点カメラコントローラー（FPS風）
/// </summary>
public class FreeCameraController : CameraController
{
    public float RotationSpeed { get; set; } = 2.0f;
    public float MovementSpeed { get; set; } = 5.0f;

    public FreeCameraController(Camera3D camera) : base(camera) { }

    public override void Update(float deltaTime)
    {
        // 自動更新ロジックがあればここに実装
    }

    public override void HandleInput(InputState input)
    {
        if (input.IsMouseButtonDown(MouseButton.Right))
        {
            float yawDelta = input.MouseDelta.X * RotationSpeed * 0.01f;
            float pitchDelta = -input.MouseDelta.Y * RotationSpeed * 0.01f;
            _camera.RotateView(yawDelta, pitchDelta);
        }

        float deltaTime = 1f / 60f; // 仮の値、実際は外部から取得

        if (input.IsKeyDown(Key.W))
            _camera.MoveForward(MovementSpeed * deltaTime);
        if (input.IsKeyDown(Key.S))
            _camera.MoveForward(-MovementSpeed * deltaTime);
        if (input.IsKeyDown(Key.D))
            _camera.MoveRight(MovementSpeed * deltaTime);
        if (input.IsKeyDown(Key.A))
            _camera.MoveRight(-MovementSpeed * deltaTime);
        if (input.IsKeyDown(Key.E))
            _camera.MoveUp(MovementSpeed * deltaTime);
        if (input.IsKeyDown(Key.Q))
            _camera.MoveUp(-MovementSpeed * deltaTime);
    }
}
