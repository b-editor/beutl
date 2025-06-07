using System.ComponentModel.DataAnnotations;
using System.Numerics;
using Beutl.Animation;
using Beutl.Language;
using Beutl.Media;

namespace Beutl.Graphics.Rendering;

/// <summary>
/// 3D変換を表すクラス
/// 回転、スケール、平行移動、カスタム行列をサポート
/// </summary>
public class Transform3D : Animatable, IAffectsRender
{
    public static readonly CoreProperty<Vector3> TranslationProperty;
    public static readonly CoreProperty<Vector3> RotationProperty;
    public static readonly CoreProperty<Vector3> ScaleProperty;
    public static readonly CoreProperty<Matrix4x4> MatrixProperty;
    public static readonly CoreProperty<bool> IsEnabledProperty;

    private Vector3 _translation = Vector3.Zero;
    private Vector3 _rotation = Vector3.Zero;
    private Vector3 _scale = Vector3.One;
    private Matrix4x4 _matrix = Matrix4x4.Identity;
    private bool _isEnabled = true;

    public event EventHandler<RenderInvalidatedEventArgs>? Invalidated;

    static Transform3D()
    {
        TranslationProperty = ConfigureProperty<Vector3, Transform3D>(nameof(Translation))
            .Accessor(o => o.Translation, (o, v) => o.Translation = v)
            .DefaultValue(Vector3.Zero)
            .Register();

        RotationProperty = ConfigureProperty<Vector3, Transform3D>(nameof(Rotation))
            .Accessor(o => o.Rotation, (o, v) => o.Rotation = v)
            .DefaultValue(Vector3.Zero)
            .Register();

        ScaleProperty = ConfigureProperty<Vector3, Transform3D>(nameof(Scale))
            .Accessor(o => o.Scale, (o, v) => o.Scale = v)
            .DefaultValue(Vector3.One)
            .Register();

        MatrixProperty = ConfigureProperty<Matrix4x4, Transform3D>(nameof(Matrix))
            .Accessor(o => o.Matrix, (o, v) => o.Matrix = v)
            .DefaultValue(Matrix4x4.Identity)
            .Register();

        IsEnabledProperty = ConfigureProperty<bool, Transform3D>(nameof(IsEnabled))
            .Accessor(o => o.IsEnabled, (o, v) => o.IsEnabled = v)
            .DefaultValue(true)
            .Register();

        AffectsRender(TranslationProperty, RotationProperty, ScaleProperty, MatrixProperty, IsEnabledProperty);
    }

    /// <summary>
    /// 平行移動
    /// </summary>
    [Display(Name = "Translation")]
    public Vector3 Translation
    {
        get => _translation;
        set => SetAndRaise(TranslationProperty, ref _translation, value);
    }

    /// <summary>
    /// 回転（ラジアン、Yaw-Pitch-Roll順）
    /// </summary>
    [Display(Name = nameof(Strings.Rotation), ResourceType = typeof(Strings))]
    public Vector3 Rotation
    {
        get => _rotation;
        set => SetAndRaise(RotationProperty, ref _rotation, value);
    }

    /// <summary>
    /// スケール
    /// </summary>
    [Display(Name = nameof(Strings.Scale), ResourceType = typeof(Strings))]
    public Vector3 Scale
    {
        get => _scale;
        set => SetAndRaise(ScaleProperty, ref _scale, value);
    }

    /// <summary>
    /// カスタム変換行列
    /// </summary>
    [Display(Name = "Matrix")]
    public Matrix4x4 Matrix
    {
        get => _matrix;
        set => SetAndRaise(MatrixProperty, ref _matrix, value);
    }

    /// <summary>
    /// 変換が有効かどうか
    /// </summary>
    [Display(Name = "IsEnabled")]
    public bool IsEnabled
    {
        get => _isEnabled;
        set => SetAndRaise(IsEnabledProperty, ref _isEnabled, value);
    }

    /// <summary>
    /// 最終的な変換行列を取得
    /// </summary>
    public Matrix4x4 Value => CalculateMatrix();

    /// <summary>
    /// 変換行列を計算
    /// </summary>
    private Matrix4x4 CalculateMatrix()
    {
        if (!_isEnabled)
            return Matrix4x4.Identity;

        // TRS変換（Translation, Rotation, Scale）
        var scaleMatrix = Matrix4x4.CreateScale(_scale);
        var rotationMatrix = Matrix4x4.CreateFromYawPitchRoll(_rotation.Y, _rotation.X, _rotation.Z);
        var translationMatrix = Matrix4x4.CreateTranslation(_translation);

        // カスタム行列が単位行列でない場合は組み合わせる
        if (_matrix != Matrix4x4.Identity)
        {
            return scaleMatrix * rotationMatrix * translationMatrix * _matrix;
        }
        else
        {
            return scaleMatrix * rotationMatrix * translationMatrix;
        }
    }

    /// <summary>
    /// レンダリング無効化処理
    /// </summary>
    private static void AffectsRender(params CoreProperty[] properties)
    {
        foreach (var property in properties)
        {
            property.Changed.Subscribe(e =>
            {
                if (e.Sender is Transform3D transform)
                {
                    transform.RaiseInvalidated();
                }
            });
        }
    }

    private void RaiseInvalidated()
    {
        Invalidated?.Invoke(this, new RenderInvalidatedEventArgs(this));
    }

    /// <summary>
    /// 恒等変換を作成
    /// </summary>
    public static Transform3D Identity => new Transform3D();

    /// <summary>
    /// 平行移動変換を作成
    /// </summary>
    public static Transform3D CreateTranslation(Vector3 translation)
    {
        return new Transform3D { Translation = translation };
    }

    /// <summary>
    /// 平行移動変換を作成
    /// </summary>
    public static Transform3D CreateTranslation(float x, float y, float z)
    {
        return CreateTranslation(new Vector3(x, y, z));
    }

    /// <summary>
    /// 回転変換を作成
    /// </summary>
    public static Transform3D CreateRotation(Vector3 rotation)
    {
        return new Transform3D { Rotation = rotation };
    }

    /// <summary>
    /// 回転変換を作成（Yaw-Pitch-Roll）
    /// </summary>
    public static Transform3D CreateRotation(float yaw, float pitch, float roll)
    {
        return CreateRotation(new Vector3(pitch, yaw, roll));
    }

    /// <summary>
    /// スケール変換を作成
    /// </summary>
    public static Transform3D CreateScale(Vector3 scale)
    {
        return new Transform3D { Scale = scale };
    }

    /// <summary>
    /// 等比スケール変換を作成
    /// </summary>
    public static Transform3D CreateScale(float scale)
    {
        return CreateScale(new Vector3(scale));
    }

    /// <summary>
    /// カスタム行列変換を作成
    /// </summary>
    public static Transform3D CreateMatrix(Matrix4x4 matrix)
    {
        return new Transform3D { Matrix = matrix };
    }

    /// <summary>
    /// 複合変換を作成
    /// </summary>
    public static Transform3D Create(Vector3? translation = null, Vector3? rotation = null, Vector3? scale = null, Matrix4x4? matrix = null)
    {
        return new Transform3D
        {
            Translation = translation ?? Vector3.Zero,
            Rotation = rotation ?? Vector3.Zero,
            Scale = scale ?? Vector3.One,
            Matrix = matrix ?? Matrix4x4.Identity
        };
    }

    /// <summary>
    /// 変換を組み合わせる
    /// </summary>
    public Transform3D Combine(Transform3D other)
    {
        return new Transform3D
        {
            Translation = _translation + other._translation,
            Rotation = _rotation + other._rotation,
            Scale = Vector3.Multiply(_scale, other._scale),
            Matrix = _matrix * other._matrix
        };
    }

    /// <summary>
    /// 逆変換を取得
    /// </summary>
    public Transform3D Inverse()
    {
        if (Matrix4x4.Invert(Value, out Matrix4x4 inverted))
        {
            return CreateMatrix(inverted);
        }
        else
        {
            throw new InvalidOperationException("Transform matrix is not invertible");
        }
    }

    public override string ToString()
    {
        return $"Transform3D(T:{Translation}, R:{Rotation}, S:{Scale})";
    }
}
