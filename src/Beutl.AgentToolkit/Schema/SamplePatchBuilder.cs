using System.Text.Json.Nodes;
using Beutl.AgentToolkit.Common;
using Beutl.AgentToolkit.Reconciliation;
using Beutl.Animation;
using Beutl.Graphics;
using Beutl.Graphics.Effects;
using Beutl.Graphics.Transformation;
using Beutl.Media;
using Beutl.ProjectSystem;
using Beutl.Serialization;

namespace Beutl.AgentToolkit.Schema;

internal static class SamplePatchBuilder
{
    public static LinearGradientBrush CreateLinearGradient(string startColor, string endColor)
    {
        return new LinearGradientBrush
        {
            StartPoint = { CurrentValue = new RelativePoint(0, 0.5f, RelativeUnit.Relative) },
            EndPoint = { CurrentValue = new RelativePoint(1, 0.5f, RelativeUnit.Relative) },
            GradientStops =
            {
                new GradientStop(Color.Parse(startColor), 0),
                new GradientStop(Color.Parse(endColor), 1)
            }
        };
    }

    public static Pen CreatePen(string color, float thickness)
    {
        return new Pen
        {
            Brush = { CurrentValue = new SolidColorBrush(Color.Parse(color)) },
            Thickness = { CurrentValue = thickness }
        };
    }

    public static Blur CreateBlur(float sigma)
    {
        var blur = new Blur();
        blur.Sigma.CurrentValue = new Size(sigma, sigma);
        return blur;
    }

    public static Brightness CreateBrightness(float amount)
    {
        var brightness = new Brightness();
        brightness.Amount.CurrentValue = amount;
        return brightness;
    }

    public static DropShadow CreateDropShadow(float x, float y, float sigma, string color)
    {
        var dropShadow = new DropShadow();
        dropShadow.Position.CurrentValue = new Point(x, y);
        dropShadow.Sigma.CurrentValue = new Size(sigma, sigma);
        dropShadow.Color.CurrentValue = Color.Parse(color);
        return dropShadow;
    }

    public static Saturate CreateSaturate(float amount)
    {
        var saturate = new Saturate();
        saturate.Amount.CurrentValue = amount;
        return saturate;
    }

    public static HueRotate CreateHueRotate(float angle)
    {
        var hueRotate = new HueRotate();
        hueRotate.Angle.CurrentValue = angle;
        return hueRotate;
    }

    public static HighContrast CreateHighContrast(float contrast)
    {
        var highContrast = new HighContrast();
        highContrast.Contrast.CurrentValue = contrast;
        return highContrast;
    }

    public static MosaicEffect CreateMosaic(float tileSize)
    {
        var mosaic = new MosaicEffect();
        mosaic.TileSize.CurrentValue = new Size(tileSize, tileSize);
        return mosaic;
    }

    public static JsonObject SerializeWithoutIds(ICoreSerializable value)
    {
        JsonObject json = CoreSerializer.SerializeToJsonObject(value);
        CollectionReconciler.RemoveIds(json);
        return json;
    }

    public static JsonObject GetFirstObjectJson(JsonObject element)
    {
        return (JsonObject)((JsonArray)element[nameof(Element.Objects)]!)[0]!;
    }

    public static JsonObject GetTransformChildJson(JsonObject drawable, Type transformType)
    {
        string discriminator = IdentityHelper.WriteDiscriminator(transformType);
        JsonArray children = (JsonArray)drawable[nameof(Drawable.Transform)]![nameof(TransformGroup.Children)]!;
        return children
            .OfType<JsonObject>()
            .Single(child => string.Equals(child["$type"]?.GetValue<string>(), discriminator, StringComparison.Ordinal));
    }

    public static void AddFloatAnimation(JsonObject target, string property, params (double Seconds, float Value, Type Easing)[] keyframes)
    {
        JsonObject animations = target["Animations"] as JsonObject ?? [];
        animations[property] = CreateFloatAnimation(keyframes);
        target["Animations"] = animations;
    }

    public static JsonObject CreateFloatAnimation(params (double Seconds, float Value, Type Easing)[] keyframes)
    {
        string animationType = IdentityHelper.WriteDiscriminator(typeof(KeyFrameAnimation<float>));
        string keyFrameType = IdentityHelper.WriteDiscriminator(typeof(KeyFrame<float>));
        return new JsonObject
        {
            ["$type"] = animationType,
            [nameof(KeyFrameAnimation.KeyFrames)] = new JsonArray(keyframes
                .Select(keyframe => new JsonObject
                {
                    ["$type"] = keyFrameType,
                    [nameof(KeyFrame.KeyTime)] = TimeSpan.FromSeconds(Math.Max(0, keyframe.Seconds)).ToString("c"),
                    [nameof(KeyFrame<float>.Value)] = keyframe.Value,
                    [nameof(KeyFrame.Easing)] = IdentityHelper.WriteDiscriminator(keyframe.Easing)
                })
                .ToArray<JsonNode?>())
        };
    }

    public static string[] SearchTokens(string? query)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return [];
        }

        return query
            .Split([' ', '-', '_', '/', ',', ';', ':', '\t', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(token => token.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }
}
