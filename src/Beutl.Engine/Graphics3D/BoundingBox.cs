using System.Numerics;

namespace Beutl.Graphics.Rendering;

/// <summary>
/// 3Dバウンディングボックス
/// 最小点と最大点で定義される軸平行な直方体
/// </summary>
public struct BoundingBox : IEquatable<BoundingBox>
{
    /// <summary>
    /// 最小点
    /// </summary>
    public Vector3 Min { get; set; }

    /// <summary>
    /// 最大点
    /// </summary>
    public Vector3 Max { get; set; }

    /// <summary>
    /// バウンディングボックスのコンストラクタ
    /// </summary>
    public BoundingBox(Vector3 min, Vector3 max)
    {
        Min = Vector3.Min(min, max);
        Max = Vector3.Max(min, max);
    }

    /// <summary>
    /// 中心点とサイズからバウンディングボックスを作成
    /// </summary>
    public static BoundingBox FromCenterAndSize(Vector3 center, Vector3 size)
    {
        Vector3 halfSize = size * 0.5f;
        return new BoundingBox(center - halfSize, center + halfSize);
    }

    /// <summary>
    /// 点群からバウンディングボックスを作成
    /// </summary>
    public static BoundingBox FromPoints(IEnumerable<Vector3> points)
    {
        if (!points.Any())
            return Empty;

        Vector3 min = new Vector3(float.MaxValue);
        Vector3 max = new Vector3(float.MinValue);

        foreach (Vector3 point in points)
        {
            min = Vector3.Min(min, point);
            max = Vector3.Max(max, point);
        }

        return new BoundingBox(min, max);
    }

    /// <summary>
    /// 空のバウンディングボックス
    /// </summary>
    public static BoundingBox Empty => new BoundingBox(Vector3.Zero, Vector3.Zero);

    /// <summary>
    /// 無限大のバウンディングボックス
    /// </summary>
    public static BoundingBox Infinite => new BoundingBox(
        new Vector3(float.NegativeInfinity),
        new Vector3(float.PositiveInfinity));

    /// <summary>
    /// 中心点
    /// </summary>
    public Vector3 Center => (Min + Max) * 0.5f;

    /// <summary>
    /// サイズ
    /// </summary>
    public Vector3 Size => Max - Min;

    /// <summary>
    /// 幅
    /// </summary>
    public float Width => Max.X - Min.X;

    /// <summary>
    /// 高さ
    /// </summary>
    public float Height => Max.Y - Min.Y;

    /// <summary>
    /// 奥行き
    /// </summary>
    public float Depth => Max.Z - Min.Z;

    /// <summary>
    /// 体積
    /// </summary>
    public float Volume
    {
        get
        {
            Vector3 size = Size;
            return size.X * size.Y * size.Z;
        }
    }

    /// <summary>
    /// 表面積
    /// </summary>
    public float SurfaceArea
    {
        get
        {
            Vector3 size = Size;
            return 2.0f * (size.X * size.Y + size.Y * size.Z + size.Z * size.X);
        }
    }

    /// <summary>
    /// 空かどうか
    /// </summary>
    public bool IsEmpty => Min == Max;

    /// <summary>
    /// 有効かどうか
    /// </summary>
    public bool IsValid => Min.X <= Max.X && Min.Y <= Max.Y && Min.Z <= Max.Z;

    /// <summary>
    /// 点が含まれるかチェック
    /// </summary>
    public bool Contains(Vector3 point)
    {
        return point.X >= Min.X && point.X <= Max.X &&
               point.Y >= Min.Y && point.Y <= Max.Y &&
               point.Z >= Min.Z && point.Z <= Max.Z;
    }

    /// <summary>
    /// 他のバウンディングボックスが含まれるかチェック
    /// </summary>
    public bool Contains(BoundingBox other)
    {
        return Contains(other.Min) && Contains(other.Max);
    }

    /// <summary>
    /// 他のバウンディングボックスと交差するかチェック
    /// </summary>
    public bool Intersects(BoundingBox other)
    {
        return Max.X >= other.Min.X && Min.X <= other.Max.X &&
               Max.Y >= other.Min.Y && Min.Y <= other.Max.Y &&
               Max.Z >= other.Min.Z && Min.Z <= other.Max.Z;
    }

    /// <summary>
    /// レイとの交差判定
    /// </summary>
    public bool Intersects(Ray ray, out float distance)
    {
        distance = 0.0f;

        float tMin = float.NegativeInfinity;
        float tMax = float.PositiveInfinity;

        // X軸
        if (Math.Abs(ray.Direction.X) < float.Epsilon)
        {
            if (ray.Origin.X < Min.X || ray.Origin.X > Max.X)
                return false;
        }
        else
        {
            float invDirX = 1.0f / ray.Direction.X;
            float t1 = (Min.X - ray.Origin.X) * invDirX;
            float t2 = (Max.X - ray.Origin.X) * invDirX;

            if (t1 > t2) (t1, t2) = (t2, t1);

            tMin = Math.Max(tMin, t1);
            tMax = Math.Min(tMax, t2);

            if (tMin > tMax) return false;
        }

        // Y軸
        if (Math.Abs(ray.Direction.Y) < float.Epsilon)
        {
            if (ray.Origin.Y < Min.Y || ray.Origin.Y > Max.Y)
                return false;
        }
        else
        {
            float invDirY = 1.0f / ray.Direction.Y;
            float t1 = (Min.Y - ray.Origin.Y) * invDirY;
            float t2 = (Max.Y - ray.Origin.Y) * invDirY;

            if (t1 > t2) (t1, t2) = (t2, t1);

            tMin = Math.Max(tMin, t1);
            tMax = Math.Min(tMax, t2);

            if (tMin > tMax) return false;
        }

        // Z軸
        if (Math.Abs(ray.Direction.Z) < float.Epsilon)
        {
            if (ray.Origin.Z < Min.Z || ray.Origin.Z > Max.Z)
                return false;
        }
        else
        {
            float invDirZ = 1.0f / ray.Direction.Z;
            float t1 = (Min.Z - ray.Origin.Z) * invDirZ;
            float t2 = (Max.Z - ray.Origin.Z) * invDirZ;

            if (t1 > t2) (t1, t2) = (t2, t1);

            tMin = Math.Max(tMin, t1);
            tMax = Math.Min(tMax, t2);

            if (tMin > tMax) return false;
        }

        distance = tMin >= 0 ? tMin : tMax;
        return distance >= 0;
    }

    /// <summary>
    /// 他のバウンディングボックスとの合成
    /// </summary>
    public BoundingBox Union(BoundingBox other)
    {
        return new BoundingBox(
            Vector3.Min(Min, other.Min),
            Vector3.Max(Max, other.Max));
    }

    /// <summary>
    /// 点との合成
    /// </summary>
    public BoundingBox Union(Vector3 point)
    {
        return new BoundingBox(
            Vector3.Min(Min, point),
            Vector3.Max(Max, point));
    }

    /// <summary>
    /// 他のバウンディングボックスとの交差部分
    /// </summary>
    public BoundingBox Intersection(BoundingBox other)
    {
        Vector3 min = Vector3.Max(Min, other.Min);
        Vector3 max = Vector3.Min(Max, other.Max);

        if (min.X > max.X || min.Y > max.Y || min.Z > max.Z)
            return Empty;

        return new BoundingBox(min, max);
    }

    /// <summary>
    /// 変換行列を適用
    /// </summary>
    public BoundingBox Transform(Matrix4x4 matrix)
    {
        if (IsEmpty)
            return Empty;

        // 8つの頂点を変換
        Vector3[] corners = GetCorners();
        Vector3[] transformedCorners = new Vector3[8];

        for (int i = 0; i < 8; i++)
        {
            transformedCorners[i] = Vector3.Transform(corners[i], matrix);
        }

        return FromPoints(transformedCorners);
    }

    /// <summary>
    /// 8つの頂点を取得
    /// </summary>
    public Vector3[] GetCorners()
    {
        return new Vector3[]
        {
            new Vector3(Min.X, Min.Y, Min.Z), // 000
            new Vector3(Max.X, Min.Y, Min.Z), // 100
            new Vector3(Min.X, Max.Y, Min.Z), // 010
            new Vector3(Max.X, Max.Y, Min.Z), // 110
            new Vector3(Min.X, Min.Y, Max.Z), // 001
            new Vector3(Max.X, Min.Y, Max.Z), // 101
            new Vector3(Min.X, Max.Y, Max.Z), // 011
            new Vector3(Max.X, Max.Y, Max.Z), // 111
        };
    }

    /// <summary>
    /// 指定した軸に沿って拡張
    /// </summary>
    public BoundingBox Expand(float amount)
    {
        Vector3 expansion = new Vector3(amount);
        return new BoundingBox(Min - expansion, Max + expansion);
    }

    /// <summary>
    /// 指定したベクトル分拡張
    /// </summary>
    public BoundingBox Expand(Vector3 amount)
    {
        return new BoundingBox(Min - amount, Max + amount);
    }

    /// <summary>
    /// 点との最短距離
    /// </summary>
    public float DistanceTo(Vector3 point)
    {
        Vector3 clampedPoint = Vector3.Clamp(point, Min, Max);
        return Vector3.Distance(point, clampedPoint);
    }

    /// <summary>
    /// 点の最も近い点を取得
    /// </summary>
    public Vector3 ClosestPoint(Vector3 point)
    {
        return Vector3.Clamp(point, Min, Max);
    }

    public bool Equals(BoundingBox other)
    {
        return Min.Equals(other.Min) && Max.Equals(other.Max);
    }

    public override bool Equals(object? obj)
    {
        return obj is BoundingBox other && Equals(other);
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(Min, Max);
    }

    public static bool operator ==(BoundingBox left, BoundingBox right)
    {
        return left.Equals(right);
    }

    public static bool operator !=(BoundingBox left, BoundingBox right)
    {
        return !left.Equals(right);
    }

    public override string ToString()
    {
        return $"BoundingBox(Min:{Min}, Max:{Max})";
    }
}
