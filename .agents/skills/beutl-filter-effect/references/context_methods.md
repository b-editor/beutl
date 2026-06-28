# FilterEffectContext method reference

## Table of contents
1. [Blur and shadow](#blur-and-shadow)
2. [Color correction](#color-correction)
3. [Morphology](#morphology)
4. [Transform](#transform)
5. [Custom effects](#custom-effects)
6. [Low-level API](#low-level-api)
7. [Shaders (SKSL / GLSL)](#shaders-sksl--glsl)

---

## Blur and shadow

### Blur
```csharp
void Blur(Size sigma)
```
Apply a Gaussian blur. Bounds expand by `sigma * 3`.

### DropShadow / DropShadowOnly
```csharp
void DropShadow(Point position, Size sigma, Color color)
void DropShadowOnly(Point position, Size sigma, Color color)
```
- `DropShadow`: draws a shadow beneath the source image.
- `DropShadowOnly`: shadow only (no source image).

### InnerShadow / InnerShadowOnly
```csharp
void InnerShadow(Point position, Size sigma, Color color)
void InnerShadowOnly(Point position, Size sigma, Color color)
```
Draws a shadow on the inside. Implemented via `CustomEffect`.

---

## Color correction

### ColorMatrix
```csharp
void ColorMatrix(in ColorMatrix matrix)
void ColorMatrix<T>(T data, Func<T, ColorMatrix> factory)
```
Color transformation through a 5x4 color matrix.

### Saturate
```csharp
void Saturate(float amount)
```
Saturation adjustment. `1.0` keeps the original saturation, `0.0` produces grayscale.

### HueRotate
```csharp
void HueRotate(float degrees)
```
Hue rotation (in degrees).

### Brightness
```csharp
void Brightness(float amount)
```
Brightness adjustment.

### HighContrast
```csharp
void HighContrast(bool grayscale, HighContrastInvertStyle invertStyle, float contrast)
```
High-contrast processing.

### Lighting
```csharp
void Lighting(Color multiply, Color add)
```
Lighting composed of a multiply and an additive color.

### LumaColor
```csharp
void LumaColor()
```
Builds a color filter from luminance.

### LuminanceToAlpha
```csharp
void LuminanceToAlpha()
```
Convert luminance into the alpha channel.

### BlendMode
```csharp
void BlendMode(Color color, BlendMode blendMode)
void BlendMode(Brush.Resource? brush, BlendMode blendMode)
```
Apply a blend mode.

---

## Morphology

### Dilate
```csharp
void Dilate(float radiusX, float radiusY)
```
Dilation. Bounds expand by `radius`.

### Erode
```csharp
void Erode(float radiusX, float radiusY)
```
Erosion. Bounds are unchanged.

---

## Transform

### Transform
```csharp
void Transform(Matrix matrix, BitmapInterpolationMode bitmapInterpolationMode)
```
Affine transform. Bounds are converted to the AABB.

### MatrixConvolution
```csharp
void MatrixConvolution(
    PixelSize kernelSize,
    float[] kernel,
    float gain,
    float bias,
    PixelPoint kernelOffset,
    GradientSpreadMethod spreadMethod,
    bool convolveAlpha)
```
Convolution filter.

---

## Custom effects

### CustomEffect
```csharp
void CustomEffect<T>(
    T data,
    Action<T, CustomFilterEffectContext> action,
    Func<T, Rect, Rect> transformBounds)

void CustomEffect<T>(
    T data,
    Action<T, CustomFilterEffectContext> action)  // transformBounds defaults to Rect.Invalid
```

Use this for low-level custom processing. Via `CustomFilterEffectContext`:

- `Targets`: list of render targets (`EffectTarget`).
- `CreateTarget(Rect bounds)`: create a new target.
- `Open(EffectTarget target)`: obtain an `ImmediateCanvas`.

**Basic pattern:**
```csharp
context.CustomEffect(
    data,
    (data, c) => {
        for (int i = 0; i < c.Targets.Count; i++)
        {
            var target = c.Targets[i];
            var newTarget = c.CreateTarget(target.Bounds);
            using (var canvas = c.Open(newTarget))
            {
                // draw here
            }
            target.Dispose();
            c.Targets[i] = newTarget;
        }
    },
    (_, bounds) => bounds);
```

---

## Low-level API

### AppendSkiaFilter
```csharp
void AppendSkiaFilter<T>(
    T data,
    Func<T, SKImageFilter?, FilterEffectActivator, SKImageFilter?> factory,
    Func<T, Rect, Rect> transformBounds)
```
Append a SkiaSharp `SKImageFilter` directly.

### AppendSKColorFilter
```csharp
void AppendSKColorFilter<T>(
    T data,
    Func<T, FilterEffectActivator, SKColorFilter?> factory)
```
Append a SkiaSharp `SKColorFilter` directly (no bounds transformation).

---

## Shaders (SKSL / GLSL)

### SKSL (SkiaShaderLanguage)

Author a custom shader with SkiaSharp's `SKRuntimeEffect`.

**Compile:**
```csharp
SKRuntimeEffect? effect = SKRuntimeEffect.CreateShader(skslCode, out string? errorText);
```

**Built-in uniforms:**
```glsl
uniform shader src;          // input image
uniform float progress;      // 0.0 - 1.0 (effect progress)
uniform float duration;      // seconds (effect length)
uniform float time;          // seconds (current time)
uniform float width;         // render-target width
uniform float height;        // render-target height
uniform float2 iResolution;  // (width, height)
uniform float iTime;         // alias for time
```

**Applying a shader:**
```csharp
using var image = target.RenderTarget!.Value.Snapshot();
using var baseShader = SKShader.CreateImage(image);

var builder = new SKRuntimeShaderBuilder(s_runtimeEffect);
builder.Children["src"] = baseShader;
builder.Uniforms["myParam"] = value;

using SKShader shader = builder.Build();
using var paint = new SKPaint { Shader = shader };
canvas.Canvas.DrawRect(rect, paint);
```

**SKSL basics:**
```glsl
uniform shader src;
uniform float2 tileSize;

half4 main(float2 fragCoord) {
    // fragCoord: pixel coordinate
    // src.eval(coord): sample the input image
    half4 color = src.eval(fragCoord);
    return color;
}
```

### GLSL (Vulkan)

Fragment shaders on Vulkan-capable environments via `GLSLFilterPipeline`.

**Basic structure:**
```glsl
#version 450

layout(location = 0) in vec2 fragCoord;  // 0.0 - 1.0 (normalized coordinate)
layout(location = 0) out vec4 outColor;

layout(set = 0, binding = 0) uniform sampler2D srcTexture;

layout(push_constant) uniform PushConstants {
    float progress;
    float duration;
    float time;
    float width;
    float height;
} pc;

void main() {
    vec4 color = texture(srcTexture, fragCoord);
    outColor = color;
}
```

**PushConstants layout:**
```csharp
[StructLayout(LayoutKind.Sequential)]
private struct PushConstants
{
    public float Progress;
    public float Duration;
    public float Time;
    public float Width;
    public float Height;
}
```

**Compile and execute:**
```csharp
IGraphicsContext context = GraphicsContextFactory.SharedContext;
GLSLFilterPipeline pipeline = GLSLFilterPipeline.Create(context, fragmentShaderCode);
pipeline.Execute(sourceTexture, destinationTexture, depthTexture, pushConstants);
```

---

## About bounds transformation

The `transformBounds` function tells the effect how its output bounds change.

- **Expand**: `bounds.Inflate(new Thickness(amount))`
- **No change**: `bounds`
- **Invalidate**: `Rect.Invalid` (recomputed at runtime)

Accurate bounds tracking lets the renderer skip unnecessary regions, improving performance.

---

## EffectTarget

The render target used inside a `CustomEffect`.

**Properties:**
- `RenderTarget`: a render target wrapping an `SKSurface`.
- `Bounds`: the target's bounds (`Rect`).

**Operations:**
```csharp
// Snapshot
using var image = target.RenderTarget!.Value.Snapshot();

// Create a new target
EffectTarget newTarget = context.CreateTarget(target.Bounds);

// Get a canvas
using ImmediateCanvas canvas = context.Open(newTarget);

// Dispose (important: always dispose after use)
target.Dispose();
```
