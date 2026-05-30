# Contract: Effect / drawable / brush / pen scale contract

**Feature**: 003 | FR-008/FR-009/FR-010/FR-011/FR-012/FR-015. Audience: authors of `FilterEffect`, `CustomEffect`, `Drawable`, brushes, and C# script effects (in-tree and plugin).

## The one rule (FR-008)

When an effect's **working scale** is `w` (the supply-driven scale it actually runs at — FR-036, **NOT** the output scale `s_out`):
- **Multiply by `w`** every **spatial-length / pixel-magnitude** parameter.
- **Leave unchanged** every **magnitude-invariant** parameter.

| Multiply by `w` (spatial length) | Leave unchanged (magnitude-invariant) |
|---|---|
| blur sigma; drop/inner-shadow offset & sigma; flat-shadow length; dilate/erode radius; mosaic tile size; color-shift offset; displacement translate; pen thickness & offset & dash; stroke offset; particle pixel sizes; audio-visualizer bar width / block gap / pixel minimums; tile/drawable intermediate raster resolution | color; angle/degrees/radians; percentage; ratio; 0..1 value; `RelativePoint`/`RelativeRect`; blend mode; division count; enum; `MiterLimit`; caps/joins/alignment; `Trim*` |

**Non-obvious cases**:
- `PerlinNoiseBrush.BaseFrequency` is **divided** by `w` (period invariant in logical units), centralized in `BrushConstructor.CreatePerlinNoiseShader`.
- Text is **re-shaped** at `Size × w` (font size, spacing, stroke; `Hinting=Full`) — never matrix- or bitmap-scaled (FR-012).
- Strokes are pre-outlined in logical space and scaled by the root CTM — pen authors do **nothing** (D3).

## How an author reads the active scale (FR-015)

Two accessors, both default `1.0`. **They expose the `WorkingScale` `w`** (what the effect runs at), not the output scale. An effect that needs the eventual delivery target reads `FilterEffectContext.OutputScale`.

1. **`FilterEffectContext.WorkingScale`** — for `CSharpScriptEffect` and out-of-tree `FilterEffect`s built from the context primitives. Built-in primitives (`Blur`/`DropShadow`/`Dilate`/`Erode`/`Transform`/`MatrixConvolution`) already multiply their spatial-length args by `WorkingScale` **centrally**, so an effect that forwards through them inherits scale-correctness for free.
2. **`CustomFilterEffectContext.WorkingScale`** — for `CustomEffect` / SKSL / GLSL. `CreateTarget(bounds)` allocates `ceil(bounds × WorkingScale)` and `Open` returns a pre-scaled canvas, so a custom effect drawing into its target gets the right resolution automatically; absolute pixel literals in the author's draw code are multiplied by `WorkingScale`.

```csharp
// out-of-tree FilterEffect example
public override void ApplyTo(FilterEffectContext context)
{
    // sigma is a logical length -> the primitive multiplies by context.WorkingScale internally
    context.Blur(new Size(BlurRadius, BlurRadius));

    // a custom step that hand-picks a pixel literal:
    context.CustomEffect(state, (d, c) =>
    {
        float devRadius = MyPixelRadius * c.WorkingScale; // explicit
        // draw into c.CreateTarget(bounds) which is already ceil(bounds*WorkingScale)
    });
}

// a quality effect that wants a high-res source kept through it:
public override ResolutionPolicy ResolutionPolicy => ResolutionPolicy.PreserveSource;
```

## Resolution policy (FR-036) — what scale an effect runs at

Each effect/node declares a `ResolutionPolicy` (default `Inherit`) that decides its **working scale `w`** from its inputs' effective scales and the output scale `s_out`:

| Policy | `w` | Use |
|---|---|---|
| **`Inherit`** (default) | input supply density | preserve input as-is: a 0.5 proxy stays 0.5 (no upsample), a 2.0 source stays 2.0 (no downsample). `s_out` is not a ceiling. |
| **`ClampToOutput`** | `min(supply, s_out)` | **perf opt-out** for a heavy effect: drop a too-high input early. |
| **`Oversample(k)`** | `max(supply, k·s_out)` | **quality opt-in**: force ≥ k× the final target even from a low input (SSAA-on-demand). |
| **`PreserveSource`** | input supply density, **floors any ancestor `ClampToOutput`** | keep a high source's detail through a quality effect. |

`w` is finally capped by a global ceiling `MaxWorkingScale` (FR-037, memory backstop). The **default is `Inherit` for every effect** (built-in and plugin); declare a different policy by overriding `FilterEffect.ResolutionPolicy`. **Built-in assignments** (pinned in the FR-009 manifest): resolution-sensitive effects (below) → `PreserveSource`; heavy effects MAY override to `ClampToOutput`; everything else stays `Inherit`. A policy MUST NOT change the `s_out = 1.0` output.

## Resolution-sensitive effects (FR-013)

`PixelSort`, contour-based `Stroke`/`FlatShadow`/`PartsSplit`, `AutoClip`, integer `Dilate`/`Erode`, `Mosaic`, Perlin-driven, and custom per-texel shaders **still apply the multiply rule**, but their reduced-scale preview is a **best-effort approximation** (not bit-identical) and is full-fidelity only at export `s_out=1.0`. They MUST declare `PreserveSource` (or `Oversample`) so a higher-resolution source is kept through them and downsampled only at the final stage; `ClampToOutput` on them is forbidden. No force-full-scale subtree mechanism and no warning UI in v1. Their tests assert (a) byte-equality at `s_out=1.0` and (b) a documented structural invariant at a reduced scale (e.g. `mosaic tile == ceil(tileSize × w)` device px), not SSIM.

## Mechanism summary

Centralized scaling lives in the `FilterEffectContext` primitives (covers built-ins and their forwarders for free); the per-effect read accessor (`WorkingScale`) is the escape hatch for pixel-reading custom/shader/script effects, and `ResolutionPolicy` is the declarative knob for *what scale* the effect runs at. A plugin effect that ignores both still renders correctly at `s_out=1.0` (default `Inherit` + `Unbounded` inputs → `w=1.0`); it simply runs supply-driven and won't drive oversampling until it adopts this contract.
