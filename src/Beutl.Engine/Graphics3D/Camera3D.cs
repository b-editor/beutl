using System.Numerics;
using Beutl.Utilities;

namespace Beutl.Graphics.Rendering;

/// <summary>
/// 3Dカメラの実装
/// </summary>
public class Camera3D : I3DCamera
{
    private Vector3 _position = new(0, 0, 5);
    private Vector3 _target = Vector3.Zero;
    private Vector3 _up = Vector3.UnitY;
    private float _fieldOfView = MathF.PI / 3; // 60度
    private float _aspectRatio = 16f / 9f;
    private float _nearClip = 0.1f;
    private float _farClip = 1000f;

    private Matrix4x4 _viewMatrix;
    private Matrix4x4 _projectionMatrix;
    private bool _viewMatrixDirty = true;
    private bool _projectionMatrixDirty = true;

    public Vector3 Position
    {
        get => _position;
        set
        {
            _position = value;
            _viewMatrixDirty = true;
        }
    }

    public Vector3 Target
    {
        get => _target;
        set
        {
            _target = value;
            _viewMatrixDirty = true;
        }
    }

    public Vector3 Up
    {
        get => _up;
        set
        {
            _up = value;
            _viewMatrixDirty = true;
        }
    }

    public float FieldOfView
    {
        get => _fieldOfView;
        set
        {
            _fieldOfView = value;
            _projectionMatrixDirty = true;
        }
    }

    public float AspectRatio
    {
        get => _aspectRatio;
        set
        {
            _aspectRatio = value;
            _projectionMatrixDirty = true;
        }
    }

    public float NearClip
    {
        get => _nearClip;
        set
        {
            _nearClip = value;
            _projectionMatrixDirty = true;
        }
    }

    public float FarClip
    {
        get => _farClip;
        set
        {
            _farClip = value;
            _projectionMatrixDirty = true;
        }
    }

    public Matrix4x4 ViewMatrix
    {
        get
        {
            if (_viewMatrixDirty)
            {
                UpdateViewMatrix();
                _viewMatrixDirty = false;
            }
            return _viewMatrix;
        }
    }

    public Matrix4x4 ProjectionMatrix
    {
        get
        {
            if (_projectionMatrixDirty)
            {
                UpdateProjectionMatrix();
                _projectionMatrixDirty = false;
            }
            return _projectionMatrix;
        }
    }

    /// <summary>
    /// カメラの前方向ベクトル
    /// </summary>
    public Vector3 Forward => Vector3.Normalize(_target - _position);

    /// <summary>
    /// カメラの右方向ベクトル
    /// </summary>
    public Vector3 Right => Vector3.Normalize(Vector3.Cross(Forward, _up));

    /// <summary>
    /// カメラを指定された方向に移動
    /// </summary>
    public void Translate(Vector3 translation)
    {
        Position += translation;
        Target += translation;
    }

    /// <summary>
    /// カメラを前方/後方に移動
    /// </summary>
    public void MoveForward(float distance)
    {
        Vector3 forward = Forward;
        Position += forward * distance;
        Target += forward * distance;
    }

    /// <summary>
    /// カメラを左右に移動
    /// </summary>
    public void MoveRight(float distance)
    {
        Vector3 right = Right;
        Position += right * distance;
        Target += right * distance;
    }

    /// <summary>
    /// カメラを上下に移動
    /// </summary>
    public void MoveUp(float distance)
    {
        Position += _up * distance;
        Target += _up * distance;
    }

    /// <summary>
    /// ターゲットを中心にカメラを回転（軌道運動）
    /// </summary>
    public void OrbitAroundTarget(float yawDelta, float pitchDelta)
    {
        Vector3 offset = _position - _target;
        float radius = offset.Length();

        // 現在の球面座標を計算
        float currentYaw = MathF.Atan2(offset.X, offset.Z);
        float currentPitch = MathF.Asin(offset.Y / radius);

        // 新しい角度を計算
        float newYaw = currentYaw + yawDelta;
        float newPitch = MathF.Max(-MathF.PI / 2 + 0.01f,
                                   MathF.Min(MathF.PI / 2 - 0.01f, currentPitch + pitchDelta));

        // 新しい位置を計算
        Vector3 newOffset = new(
            radius * MathF.Cos(newPitch) * MathF.Sin(newYaw),
            radius * MathF.Sin(newPitch),
            radius * MathF.Cos(newPitch) * MathF.Cos(newYaw)
        );

        Position = _target + newOffset;
    }

    /// <summary>
    /// カメラの向きを回転（自由視点）
    /// </summary>
    public void RotateView(float yawDelta, float pitchDelta)
    {
        Vector3 forward = Forward;
        Vector3 right = Right;

        // ヨー回転（Y軸周り）
        Matrix4x4 yawRotation = Matrix4x4.CreateRotationY(yawDelta);
        forward = Vector3.TransformNormal(forward, yawRotation);

        // ピッチ回転（カメラの右軸周り）
        Matrix4x4 pitchRotation = Matrix4x4.CreateFromAxisAngle(right, pitchDelta);
        forward = Vector3.TransformNormal(forward, pitchRotation);

        Target = Position + forward * Vector3.Distance(Position, Target);
    }

    /// <summary>
    /// ズーム（FOVを変更）
    /// </summary>
    public void Zoom(float deltaFov)
    {
        FieldOfView = MathF.Max(0.1f, MathF.Min(MathF.PI - 0.1f, FieldOfView + deltaFov));
    }

    /// <summary>
    /// 指定された位置を見る
    /// </summary>
    public void LookAt(Vector3 target, Vector3 up)
    {
        Target = target;
        Up = up;
    }

    /// <summary>
    /// カメラを指定された位置と向きに設定
    /// </summary>
    public void SetTransform(Vector3 position, Vector3 target, Vector3 up)
    {
        Position = position;
        Target = target;
        Up = up;
    }

    /// <summary>
    /// スクリーン座標をワールド座標のレイに変換
    /// </summary>
    public Ray ScreenPointToRay(Vector2 screenPoint, Vector2 screenSize)
    {
        // NDC座標に変換 (-1 to 1)
        float ndcX = (2.0f * screenPoint.X) / screenSize.X - 1.0f;
        float ndcY = 1.0f - (2.0f * screenPoint.Y) / screenSize.Y;

        // クリップ空間の座標
        Vector4 clipCoords = new(ndcX, ndcY, -1.0f, 1.0f);

        // ビュー空間に変換
        Matrix4x4.Invert(ProjectionMatrix, out Matrix4x4 invProjection);
        Vector4 eyeCoords = Vector4.Transform(clipCoords, invProjection);
        eyeCoords = new Vector4(eyeCoords.X, eyeCoords.Y, -1.0f, 0.0f);

        // ワールド空間に変換
        Matrix4x4.Invert(ViewMatrix, out Matrix4x4 invView);
        Vector4 worldCoords = Vector4.Transform(eyeCoords, invView);
        Vector3 rayDirection = Vector3.Normalize(new Vector3(worldCoords.X, worldCoords.Y, worldCoords.Z));

        return new Ray(Position, rayDirection);
    }

    private void UpdateViewMatrix()
    {
        _viewMatrix = Matrix4x4.CreateLookAt(_position, _target, _up);
    }

    private void UpdateProjectionMatrix()
    {
        _projectionMatrix = Matrix4x4.CreatePerspectiveFieldOfView(_fieldOfView, _aspectRatio, _nearClip, _farClip);
    }
}
