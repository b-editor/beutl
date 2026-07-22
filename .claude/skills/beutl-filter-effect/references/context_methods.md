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

## Deferred Shader and Geometry authoring

`ApplyTo` records descriptions only. Do not compile a native program, allocate a target, snapshot,
read back, flush, or draw while recording.

### Current-pixel SkSL

Use `CurrentPixel` for a restricted color transform. Its source defines exactly one
`half4 apply(half4 color)` function; compatible adjacent stages may be fused.

```csharp
context.Shader(ShaderDescription.CurrentPixel(
    "uniform float amount; half4 apply(half4 color) { return color * amount; }",
    bindings => bindings.Uniform("amount", amount)));
```

### Whole-source SkSL

Use `WholeSource` when the shader samples coordinates. Declare the implicit upstream input as
`uniform shader src;` and provide an explicit bounds contract. This is a materialization boundary.

```csharp
context.Shader(ShaderDescription.WholeSource(
    """
    uniform shader src;
    half4 main(float2 coord) { return src.eval(coord); }
    """,
    RenderBoundsContract.Identity));
```

Declare uniforms and resources through `ShaderBindingBuilder`. Runtime bounds, device size, and working
scale come from `ShaderExecutionContext` inside deferred binders; do not bake them into source or cache keys.

### Geometry

Use `GeometryDescription` for guarded execution-time canvas work, custom hit testing, or declared readback.

```csharp
context.Geometry(GeometryDescription.Create(
    static session => session.Canvas.Use(canvas => session.Input.Draw(canvas)),
    RenderBoundsContract.Identity,
    RenderHitTestContract.AnyInput,
    structuralKey: "identity-geometry"));
```

Set `requiresReadback: true` before calling `session.Input.UseSnapshot`. The execution-scoped session,
input, canvas, binding writers, and snapshots must not be retained.

### GLSL (opaque fallback)

There is no public declarative GLSL description. Existing built-in GLSL effects use an internal backend;
plugin authors must keep unavoidable GLSL work inside `CustomEffect`. That callback executes later as an
opaque external island and prevents GPU-pass fusion across it. Do not reference the internal
`GLSLFilterPipeline`, and never compile or execute GLSL during `ApplyTo`.

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
using var image = target.RenderTarget!.Snapshot();

// Create a new target
EffectTarget newTarget = context.CreateTarget(target.Bounds);

// Get a canvas
using ImmediateCanvas canvas = context.Open(newTarget);

// Dispose (important: always dispose after use)
target.Dispose();
```
