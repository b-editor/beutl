using System.Collections.Concurrent;
using System.Numerics;
using System.Reflection;

namespace Beutl.Animation.Easings;

public static class SplineEasingHelper
{
    private const float Eps = 1e-9f;

    private static (float x, float y) Lerp((float x, float y) a, (float x, float y) b, float t)
        => (a.x + (b.x - a.x) * t, a.y + (b.y - a.y) * t);

    private static float Bezier(float t, float p0, float p1, float p2, float p3)
    {
        float u = 1 - t;
        return u * u * u * p0 + 3 * u * u * t * p1 + 3 * u * t * t * p2 + t * t * t * p3;
    }

    public static (SplineEasing Left, SplineEasing Right) SplitByT(SplineEasing src, float t)
    {
        t = Math.Clamp(t, 0.0f, 1.0f);

        // x(tau) = t を満たすパラメータ s を2分探索で求める
        float s0 = 0, s1 = 1;
        float s = 0.5f;
        for (int i = 0; i < 40; i++)
        {
            s = 0.5f * (s0 + s1);
            float x = Bezier(s, 0, src.X1, src.X2, 1);
            if (x < t) s0 = s;
            else s1 = s;
        }

        return SplitByS(src, s);
    }

    private static (SplineEasing left, SplineEasing right) SplitByS(SplineEasing src, float s)
    {
        var P0 = (x: 0.0f, y: 0.0f);
        var P1 = (src.X1, src.Y1);
        var P2 = (src.X2, src.Y2);
        var P3 = (x: 1.0f, y: 1.0f);

        // de Casteljau
        var P01 = Lerp(P0, P1, s);
        var P12 = Lerp(P1, P2, s);
        var P23 = Lerp(P2, P3, s);
        var P012 = Lerp(P01, P12, s);
        var P123 = Lerp(P12, P23, s);
        var P0123 = Lerp(P012, P123, s);

        float xs = P0123.x;
        float ys = P0123.y;

        // 左側スケーリング
        float sxL = Math.Abs(xs) < Eps ? 1.0f : (1.0f / xs);
        float syL = Math.Abs(ys) < Eps ? 1.0f : (1.0f / ys);
        var L1 = (x: P01.x * sxL, y: P01.y * syL);
        var L2 = (x: P012.x * sxL, y: P012.y * syL);

        // 右側スケーリング
        float dxR = 1.0f - xs;
        float dyR = 1.0f - ys;
        float sxR = Math.Abs(dxR) < Eps ? 1.0f : (1.0f / dxR);
        float syR = Math.Abs(dyR) < Eps ? 1.0f : (1.0f / dyR);
        var R1 = (x: (P123.x - xs) * sxR, y: (P123.y - ys) * syR);
        var R2 = (x: (P23.x - xs) * sxR, y: (P23.y - ys) * syR);

        static float Clamp01(float v) =>
            float.IsNaN(v) ? 0 : (MathF.Abs(v) < Eps ? 0 : v);

        var left = new SplineEasing(
            Clamp01(L1.x), Clamp01(L1.y),
            Clamp01(L2.x), Clamp01(L2.y));

        var right = new SplineEasing(
            Clamp01(R1.x), Clamp01(R1.y),
            Clamp01(R2.x), Clamp01(R2.y));

        return (left, right);
    }

    class InterpolationInfo<T>
        where T : struct, INumber<T>
    {
        public bool Decrease;
        public float Height;
        public float Width;
        public (float X, float Y) P1;
        public (float X, float Y) CP1;
        public (float X, float Y) CP2;
        public (float X, float Y) P2;
        public SplineEasing Easing;

        public InterpolationInfo(SplineEasing easing, KeyFrame<T> keyframe, KeyFrame<T> prevKeyFrame)
        {
            Decrease = keyframe.Value < prevKeyFrame.Value;
            Height = float.CreateChecked(T.Abs(keyframe.Value - prevKeyFrame.Value));
            Width = (float)(keyframe.KeyTime - prevKeyFrame.KeyTime).TotalSeconds;
            P1 = (X: 0f, Y: Decrease ? Height : 0f);
            P2 = (X: Width, Y: Decrease ? 0f : Height);

            CP1 = (X: easing.X1 * Width, Y: Decrease ? (1 - easing.Y1) * Height : easing.Y1 * Height);
            CP2 = (X: easing.X2 * Width, Y: Decrease ? (1 - easing.Y2) * Height : easing.Y2 * Height);
            Easing = easing;
        }

        public void Update(KeyFrame<T> keyframe, KeyFrame<T> prevKeyFrame)
        {
            Decrease = keyframe.Value < prevKeyFrame.Value;
            Height = float.CreateChecked(T.Abs(keyframe.Value - prevKeyFrame.Value));
            Width = (float)(keyframe.KeyTime - prevKeyFrame.KeyTime).TotalSeconds;
            P1 = (X: 0f, Y: Decrease ? Height : 0f);
            P2 = (X: Width, Y: Decrease ? 0f : Height);
        }

        // CP1からP1へのベクトル
        public (float X, float Y) ControlPoint1Vector()
        {
            return (X: P1.X - CP1.X, Y: P1.Y - CP1.Y);
        }

        // CP2からP2へのベクトル
        public (float X, float Y) ControlPoint2Vector()
        {
            return (X: P2.X - CP2.X, Y: P2.Y - CP2.Y);
        }

        public void MakeControlPoint1Symmetry(InterpolationInfo<T> prevInfo)
        {
            var vector = prevInfo.ControlPoint2Vector();

            // ベクトルの角度を取得
            float radians = MathF.Atan2(vector.Y, vector.X);

            var length = Length(vector.X, vector.Y);
            var newPoint = CalculatePoint(radians, length);
            newPoint.X += P1.X;
            newPoint.Y += P1.Y;

            newPoint.X = Math.Clamp(newPoint.X / Width, 0, 1);
            if (Decrease)
            {
                newPoint.Y = 1 - (newPoint.Y / Height);
            }
            else
            {
                newPoint.Y = newPoint.Y / Height;
            }

            if (!double.IsFinite(newPoint.X)) newPoint.X = 0;
            if (!double.IsFinite(newPoint.Y)) newPoint.Y = 0;

            // Update CP1
            Easing.X1 = newPoint.X;
            Easing.Y1 = newPoint.Y;
        }

        public void MakeControlPoint2Symmetry(InterpolationInfo<T> nextInfo)
        {
            var vector = nextInfo.ControlPoint1Vector();

            // ベクトルの角度を取得
            float radians = MathF.Atan2(vector.Y, vector.X);

            var length = Length(vector.X, vector.Y);
            var newPoint = CalculatePoint(radians, length);
            newPoint.X += P2.X;
            newPoint.Y += P2.Y;

            newPoint.X = Math.Clamp(newPoint.X / Width, 0, 1);
            if (Decrease)
            {
                newPoint.Y = 1 - (newPoint.Y / Height);
            }
            else
            {
                newPoint.Y = newPoint.Y / Height;
            }

            if (!double.IsFinite(newPoint.X)) newPoint.X = 1;
            if (!double.IsFinite(newPoint.Y)) newPoint.Y = 1;

            // Update CP2
            Easing.X2 = newPoint.X;
            Easing.Y2 = newPoint.Y;
        }

        public IRecordableCommand UpdateControlPoint1((float X, float Y) vector)
        {
            vector = (-vector.X, -vector.Y);
            // ベクトルの角度を取得
            float radians = MathF.Atan2(vector.Y, vector.X);

            var length = Length(vector.X, vector.Y);
            var newPoint = CalculatePoint(radians, length);
            newPoint.X += P1.X;
            newPoint.Y += P1.Y;

            newPoint.X = Math.Clamp(newPoint.X / Width, 0, 1);
            if (Decrease)
            {
                newPoint.Y = 1 - (newPoint.Y / Height);
            }
            else
            {
                newPoint.Y = newPoint.Y / Height;
            }

            if (!double.IsFinite(newPoint.X)) newPoint.X = 0;
            if (!double.IsFinite(newPoint.Y)) newPoint.Y = 0;

            // Update CP1
            var easing = Easing;
            var oldPoint = (easing.X1, easing.Y1);
            return RecordableCommands.Create()
                .OnDo(() =>
                {
                    easing.X1 = newPoint.X;
                    easing.Y1 = newPoint.Y;
                })
                .OnUndo(() =>
                {
                    easing.X1 = oldPoint.X1;
                    easing.Y1 = oldPoint.Y1;
                })
                .ToCommand();
        }

        public IRecordableCommand UpdateControlPoint2((float X, float Y) vector)
        {
            vector = (-vector.X, -vector.Y);
            // ベクトルの角度を取得
            float radians = MathF.Atan2(vector.Y, vector.X);

            var length = Length(vector.X, vector.Y);
            var newPoint = CalculatePoint(radians, length);
            newPoint.X += P2.X;
            newPoint.Y += P2.Y;

            newPoint.X = Math.Clamp(newPoint.X / Width, 0, 1);
            if (Decrease)
            {
                newPoint.Y = 1 - (newPoint.Y / Height);
            }
            else
            {
                newPoint.Y = newPoint.Y / Height;
            }

            if (!double.IsFinite(newPoint.X)) newPoint.X = 1;
            if (!double.IsFinite(newPoint.Y)) newPoint.Y = 1;

            // Update CP1
            var easing = Easing;
            var oldPoint = (easing.X2, easing.Y2);
            return RecordableCommands.Create()
                .OnDo(() =>
                {
                    easing.X2 = newPoint.X;
                    easing.Y2 = newPoint.Y;
                })
                .OnUndo(() =>
                {
                    easing.X2 = oldPoint.X2;
                    easing.Y2 = oldPoint.Y2;
                })
                .ToCommand();
        }

        // X1 > X2になる場合調整を行う
        public void PostProcess()
        {
            if (Easing.X1 > Easing.X2)
            {
                // X1が0.5より大きい場合、方向ベクトルを維持したまま短くする
                if (Easing.X1 > 0.5f)
                {
                    float deltaX = Easing.X1 - 0.5f;
                    float deltaY = (Easing.Y1 - (Decrease ? 1 : 0)) * (deltaX / (Easing.X1 - 0));
                    Easing.X1 = 0.5f;
                    Easing.Y1 -= deltaY;
                }

                if (Easing.X2 < 0.5f) // X2が0.5未満の場合、方向ベクトルを維持したまま短くする
                {
                    float deltaX = 0.5f - Easing.X2;
                    float deltaY = (Easing.Y2 - (Decrease ? 0 : 1)) * (deltaX / (1 - Easing.X2));
                    Easing.X2 = 0.5f;
                    Easing.Y2 -= deltaY;
                }
            }
        }
    }

    private static float Length(float x, float y)
    {
        return MathF.Sqrt((x * x) + (y * y));
    }

    private static (float X, float Y) CalculatePoint(float radians, float radius)
    {
        float x = MathF.Cos(radians) * radius;
        float y = MathF.Sin(radians) * radius;
        // Y座標は反転
        return (x, y);
    }

    private static readonly ConcurrentDictionary<Type, Optional<MethodInfo>> s_cachedRemoveGenericMethods = new();
    private static readonly ConcurrentDictionary<Type, Optional<MethodInfo>> s_cachedMoveGenericMethods = new();

    public static void Remove(IKeyFrameAnimation animation, int index)
    {
        var type = animation.Property.PropertyType;
        if (!s_cachedRemoveGenericMethods.TryGetValue(type, out var method))
        {
            s_cachedRemoveGenericMethods[type] = method = default;
            Type[] interfaces = type.GetInterfaces();
            if (type.IsValueType &&
                interfaces.Any(x => x.IsGenericType && x.GetGenericTypeDefinition() == typeof(INumber<>)))
            {
                Type methodType = typeof(SplineEasingHelper);
                var methodInfo = methodType.GetMethod(nameof(RemoveGeneric), BindingFlags.Public | BindingFlags.Static);
                if (methodInfo != null)
                {
                    var genericMethod = methodInfo.MakeGenericMethod(type);
                    s_cachedRemoveGenericMethods[type] = method = genericMethod;
                }
            }
        }

        if (method.HasValue)
        {
            method.Value.Invoke(null, [animation, index]);
        }
        else
        {
            animation.KeyFrames.RemoveAt(index);
        }
    }

    public static IRecordableCommand Move(
        IKeyFrameAnimation animation, IKeyFrame keyFrame, TimeSpan keyTime)
    {
        var type = animation.Property.PropertyType;
        if (!s_cachedMoveGenericMethods.TryGetValue(type, out var method))
        {
            s_cachedMoveGenericMethods[type] = method = default;
            Type[] interfaces = type.GetInterfaces();
            if (type.IsValueType &&
                interfaces.Any(x => x.IsGenericType && x.GetGenericTypeDefinition() == typeof(INumber<>)))
            {
                Type methodType = typeof(SplineEasingHelper);
                var methodInfo = methodType.GetMethod(nameof(MoveGeneric), BindingFlags.Public | BindingFlags.Static);
                if (methodInfo != null)
                {
                    var genericMethod = methodInfo.MakeGenericMethod(type);
                    s_cachedMoveGenericMethods[type] = method = genericMethod;
                }
            }
        }

        if (method.HasValue)
        {
            return (IRecordableCommand?)method.Value.Invoke(null, [animation, keyFrame, keyTime]);
        }
        else
        {
            var oldKeyTime = keyFrame.KeyTime;
            return RecordableCommands.Create()
                .OnDo(() => keyFrame.KeyTime = keyTime)
                .OnUndo(() => keyFrame.KeyTime = oldKeyTime)
                .ToCommand();
        }
    }

    public static void RemoveGeneric<T>(KeyFrameAnimation<T> animation, int index)
        where T : struct, INumber<T>
    {
        // 削除するキーフレームの一つ前と後のキーフレームを取得
        if (index >= animation.KeyFrames.Count)
            return;

        if (0 <= index - 1 && index + 1 < animation.KeyFrames.Count
                           && animation.KeyFrames[index + 1] is KeyFrame<T> { Easing: SplineEasing } nextKeyFrame
                           && animation.KeyFrames[index - 1] is KeyFrame<T>
                           {
                               Easing: SplineEasing prevSplineEasing
                           } prevKeyFrame)
        {
            nextKeyFrame.Easing = new SplineEasing();
            var nextSplineEasing = (SplineEasing)nextKeyFrame.Easing;
            var nextInfo = new InterpolationInfo<T>(nextSplineEasing, nextKeyFrame, prevKeyFrame);

            if (0 <= index - 2
                && animation.KeyFrames[index - 2] is KeyFrame<T> prevPrevKeyFrame)
            {
                var prevInfo = new InterpolationInfo<T>(prevSplineEasing, prevKeyFrame, prevPrevKeyFrame);
                nextInfo.MakeControlPoint1Symmetry(prevInfo);
            }

            if (index + 2 < animation.KeyFrames.Count && animation.KeyFrames[index + 2] is KeyFrame<T>
                {
                    Easing: SplineEasing nextNextSplineEasing
                } nextNextKeyFrame)
            {
                var nextNextInfo = new InterpolationInfo<T>(nextNextSplineEasing, nextNextKeyFrame, nextKeyFrame);
                nextInfo.MakeControlPoint2Symmetry(nextNextInfo);
            }

            nextInfo.PostProcess();
        }

        animation.KeyFrames.RemoveAt(index);
    }


    public static IRecordableCommand MoveGeneric<T>(
        KeyFrameAnimation<T> animation, KeyFrame<T> keyFrame, TimeSpan keyTime)
        where T : struct, INumber<T>
    {
        var oldKeyTime = keyFrame.KeyTime;
        var command = RecordableCommands.Create()
            .OnDo(() => keyFrame.KeyTime = keyTime)
            .OnUndo(() => keyFrame.KeyTime = oldKeyTime)
            .ToCommand();

        int oldIndex = animation.KeyFrames.IndexOf(keyFrame);
        if (0 <= oldIndex - 1)
        {
            var oldNextKeyFrame = oldIndex + 1 < animation.KeyFrames.Count
                ? (KeyFrame<T>)animation.KeyFrames[oldIndex + 1]
                : null;
            var oldPrevKeyFrame = (KeyFrame<T>)animation.KeyFrames[oldIndex - 1];
            var oldNextInfo = oldNextKeyFrame?.Easing is SplineEasing oldNextEasing
                ? new InterpolationInfo<T>(oldNextEasing, oldNextKeyFrame, keyFrame)
                : null;
            var currentInfo = keyFrame.Easing is SplineEasing currentEasing
                ? new InterpolationInfo<T>(currentEasing, keyFrame, oldPrevKeyFrame)
                : null;
            var oldNextCPV1 = oldNextInfo?.ControlPoint1Vector();
            var oldNextCPV2 = oldNextInfo?.ControlPoint2Vector();
            var currentCPV1 = currentInfo?.ControlPoint1Vector();
            var currentCPV2 = currentInfo?.ControlPoint2Vector();

            var newIndex = animation.KeyFrames.IndexAtOrCount(keyTime);
            // ここで移動前後で区間が変わらないことを確認

            if (oldPrevKeyFrame.KeyTime < keyTime && oldNextKeyFrame?.KeyTime > keyTime)
            {
                keyFrame.KeyTime = keyTime;

                oldNextInfo?.Update(oldNextKeyFrame, keyFrame);
                currentInfo?.Update(keyFrame, oldPrevKeyFrame);

                return command
                    .Append(oldNextCPV1.HasValue ? oldNextInfo?.UpdateControlPoint1(oldNextCPV1.Value) : null)
                    .Append(oldNextCPV2.HasValue ? oldNextInfo?.UpdateControlPoint2(oldNextCPV2.Value) : null)
                    .Append(currentCPV1.HasValue ? currentInfo?.UpdateControlPoint1(currentCPV1.Value) : null)
                    .Append(currentCPV2.HasValue ? currentInfo?.UpdateControlPoint2(currentCPV2.Value) : null);
            }
            else if (0 <= newIndex - 1 && newIndex <= animation.KeyFrames.Count)
            {
                var newNextKeyFrame = newIndex < animation.KeyFrames.Count
                    ? (KeyFrame<T>)animation.KeyFrames[newIndex]
                    : null;
                var newPrevKeyFrame = (KeyFrame<T>)animation.KeyFrames[newIndex - 1];
                var newNextInfo = newNextKeyFrame?.Easing is SplineEasing newNextEasing
                    ? new InterpolationInfo<T>(newNextEasing, newNextKeyFrame, newPrevKeyFrame)
                    : null;

                var newNextCPV1 = newNextInfo?.ControlPoint1Vector();
                var newNextCPV2 = newNextInfo?.ControlPoint2Vector();

                keyFrame.KeyTime = keyTime;

                oldNextInfo?.Update(oldNextKeyFrame!, oldPrevKeyFrame);
                newNextInfo?.Update(newNextKeyFrame!, keyFrame);
                currentInfo?.Update(keyFrame, newPrevKeyFrame);

                return command
                    .Append(oldNextCPV2.HasValue ? oldNextInfo?.UpdateControlPoint2(oldNextCPV2.Value) : null)
                    .Append(currentCPV1.HasValue ? oldNextInfo?.UpdateControlPoint1(currentCPV1.Value) : null)
                    .Append(newNextCPV2.HasValue ? newNextInfo?.UpdateControlPoint2(newNextCPV2.Value) : null)
                    .Append(oldNextCPV1.HasValue ? newNextInfo?.UpdateControlPoint1(oldNextCPV1.Value) : null)
                    .Append(currentCPV2.HasValue ? currentInfo?.UpdateControlPoint2(currentCPV2.Value) : null)
                    .Append(newNextCPV1.HasValue ? currentInfo?.UpdateControlPoint1(newNextCPV1.Value) : null);
            }
        }

        return command;
    }
}
